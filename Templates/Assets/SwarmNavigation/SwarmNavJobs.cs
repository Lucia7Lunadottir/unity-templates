using Unity.AI.Navigation.LowLevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace PG.SwarmNavigation
{
    public static class SwarmAgentFlags
    {
        public const byte HasGoal = 1;
        public const byte Stopped = 2;
        public const byte Direct = 4;    // идёт к цели напрямую (чистый Raycast по NavMesh), путь не нужен
        public const byte LocStale = 8;  // Far-агент двигался вдоль углов без MoveLocation: привязка к полигону устарела, перед использованием - MapLocation
    }

    public static class SwarmPathState
    {
        public const byte Idle = 0;       // пути нет или дошёл
        public const byte Requested = 1;  // ждёт расчёта пути
        public const byte Following = 2;  // идёт по углам
    }

    // Серверная часть агента, которую читают и пишут только джобы (каждый - только свой индекс).
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SwarmAgentNav
    {
        public NavLocation Loc;          // привязка к полигону NavMesh
        public float3 Goal;              // личная цель (Group < 0)
        public float3 GoalAtRequest;     // цель в момент расчёта пути - для пересчёта при движущейся цели
        public float3 PathGoal;          // цель, привязанная к NavMesh (конец пути)
        public int Group;                // индекс общей цели (погоня), -1 - личная
        public float StoppingDistance;
        public float Remaining;
        public float PendingDt;
        public float Jump;               // 0 - не прыгает, иначе доля пути по NavMeshLink
        public float LastPathTime;
        public float CurrentSpeed;       // фактическая скорость (разгон/торможение), м/с
        public float Acceleration;       // как NavMeshAgent.acceleration; 0 - мгновенно
        public uint Serial;              // растёт при смене цели/телепорте: результат пути с другим номером устарел и отбрасывается
        public uint LinkMask;            // бит угла: вход в NavMeshLink (следующий угол - выход)
        public byte CornerCount;
        public byte CornerIndex;
        public byte State;
        public byte Status;              // SwarmPathStatus
        public byte Flags;               // SwarmAgentFlags
    }

    // Результат SteerJob для MoveJob: направление шага и накопленное dt (0 - в этом кадре не шагает).
    public struct SwarmStep
    {
        public float3 Dir;
        public float Dt;
    }

    // Толчок агентов от точки (взрыв, удар): параллельно по всем агентам, с учётом NavMesh через MoveLocation.
    [BurstCompile]
    public struct SwarmImpulseJob : IJobParallelFor
    {
        public NavWorld World;
        public NativeArray<SwarmAgentState> Agents;
        public NativeArray<SwarmAgentNav> Nav;
        public float3 Center, Extents;
        public float Radius, Strength;
        public int AgentType, AreaMask;

        public void Execute(int index)
        {
            SwarmAgentState agent = Agents[index];
            if (agent.Position.x > 1e5f) return;

            float3 offset = agent.Position - Center;
            offset.y = 0f;
            float distanceSq = math.lengthsq(offset);
            if (distanceSq >= Radius * Radius || distanceSq < 1e-8f) return;

            SwarmAgentNav nav = Nav[index];
            if (!World.IsValid(nav.Loc) || (nav.Flags & SwarmAgentFlags.LocStale) != 0)
            {
                nav.Loc = World.MapLocation(agent.Position, Extents, AgentType, AreaMask);
                nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
            }
            if (!World.IsValid(nav.Loc)) return;

            float distance = math.sqrt(distanceSq);
            float3 push = offset / distance * Strength * (1f - distance / Radius);
            nav.Loc = World.MoveLocation(nav.Loc, agent.Position + push, AreaMask);
            agent.Position = nav.Loc.position;
            Nav[index] = nav;
            Agents[index] = agent;
        }
    }

    // Упаковка состояния агентов для GPU-разведения: позиция + флаги (активен, "прибыл"/неподвижен) и группа цели.
    // "Прибывший" - неподвижное препятствие для остальных: дошедший до цели, остановленный или стоящий без цели.
    [BurstCompile]
    public struct SwarmPackAgentsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SwarmAgentState> Agents;
        [ReadOnly] public NativeArray<SwarmAgentNav> Nav;
        [ReadOnly] public NativeArray<float> Radii;
        public float DefaultRadius;
        public NativeArray<float4> Packed;

        public void Execute(int index)
        {
            SwarmAgentState agent = Agents[index];
            SwarmAgentNav nav = Nav[index];

            uint flags = 0;
            if (agent.Position.x < 1e5f)
            {
                flags |= SwarmAvoidanceGpu.FlagActive;
                bool stopped = (nav.Flags & SwarmAgentFlags.Stopped) != 0;

                // Прибывший - тот, кто реально дошёл по пути (или стоит без цели). Агент с недействительным путём не прибыл:
                // он не должен останавливать остальных.
                bool finished = nav.Status == (byte)SwarmPathStatus.Complete || nav.Status == (byte)SwarmPathStatus.Partial;
                bool arrived = nav.State == SwarmPathState.Idle
                               && (nav.Group >= 0 ? (nav.Flags & SwarmAgentFlags.HasGoal) != 0 && finished
                                                  : nav.Status != (byte)SwarmPathStatus.Invalid);
                if (stopped || arrived) flags |= SwarmAvoidanceGpu.FlagArrived;
            }

            // Радиус агента - в битах 2..7 (шаг 0.1 м, до 6.3 м).
            float radius = Radii[index] > 0f ? Radii[index] : DefaultRadius;
            flags |= (uint)math.clamp((int)math.round(radius * 10f), 1, 63) << 2;

            // Группа цели - в старших 24 битах флагов (0xFFFFFF - личная цель).
            flags |= (nav.Group >= 0 ? (uint)nav.Group : 0xFFFFFFu) << 8;
            Packed[index] = new float4(agent.Position, math.asfloat(flags));
        }
    }

    // Запрос пути: копия всего, что нужно воркеру. Пути не читают и не пишут массивы агентов, поэтому вызовы API не ждут путей.
    public struct SwarmPathRequest
    {
        public int Agent;
        public byte Generation;
        public uint Serial;
        public float3 StartPos;
        public NavLocation StartLoc;
        public bool StartLocUsable;
        public float3 Goal;
        public NavLocation GoalLoc;      // для общей цели (Group >= 0); для личной привязка делается в джобе
        public bool PersonalGoal;
    }

    // Результат пути; сливается в SwarmAgentNav в начале следующего кадра (SwarmMergePathsJob). Углы пишутся сразу в общий массив Corners.
    public struct SwarmPathResult
    {
        public bool Valid;
        public byte State;
        public byte Status;
        public byte CornerCount;
        public byte CornerIndex;
        public bool ClearGoal;
        public bool LocChanged;
        public uint LinkMask;
        public float3 PathGoal;
        public float3 GoalAtRequest;
        public NavLocation Loc;
        public float Time;
    }

    // Выбирает агентов, которым нужен путь: явные запросы и погоня за сместившейся целью (бюджет на кадр). Только читает агентов и пишет запросы.
    [BurstCompile]
    public struct SwarmCollectRequestsJob : IJob
    {
        [ReadOnly] public NativeArray<SwarmAgentState> Agents;
        [ReadOnly] public NativeArray<float3> GroupGoalPos;
        [ReadOnly] public NativeArray<NavLocation> GroupGoalLoc;
        [ReadOnly] public NativeArray<SwarmAgentNav> Nav;
        public NativeArray<SwarmPathRequest> Requests;
        public NativeArray<int> RequestCount;
        public int HighWater, Budget, StartOffset;
        public float Time, ChaseDistance, ChaseFraction;
        public float3 Cooldown;

        public void Execute()
        {
            int count = 0;
            for (int n = 0; n < HighWater && count < Budget; n++)
            {
                int a = (StartOffset + n) % HighWater;
                SwarmAgentNav nav = Nav[a];
                if ((nav.Flags & SwarmAgentFlags.HasGoal) == 0 || (nav.Flags & SwarmAgentFlags.Stopped) != 0) continue;

                SwarmAgentState agent = Agents[a];
                float3 goal = nav.Group >= 0 ? GroupGoalPos[nav.Group] : nav.Goal;
                bool want = nav.State == SwarmPathState.Requested;
                if (!want && nav.Group >= 0 && (nav.Flags & SwarmAgentFlags.Direct) == 0)
                {
                    float cooldown = agent.Tier == SwarmLodTier.Near ? Cooldown.x : agent.Tier == SwarmLodTier.Mid ? Cooldown.y : Cooldown.z;
                    if (Time - nav.LastPathTime >= cooldown)
                    {
                        float moved = math.distance(goal.xz, nav.GoalAtRequest.xz);
                        float distance = math.distance(goal.xz, agent.Position.xz);
                        want = moved > math.max(ChaseDistance, ChaseFraction * distance);
                    }
                }

                if (!want) continue;

                Requests[count++] = new SwarmPathRequest
                {
                    Agent = a, Generation = agent.Generation, Serial = nav.Serial, StartPos = agent.Position,
                    StartLoc = (nav.Flags & SwarmAgentFlags.LocStale) != 0 ? default : nav.Loc, StartLocUsable = (nav.Flags & SwarmAgentFlags.LocStale) == 0,
                    Goal = goal, GoalLoc = nav.Group >= 0 ? GroupGoalLoc[nav.Group] : default, PersonalGoal = nav.Group < 0
                };
            }
            RequestCount[0] = count;
        }
    }

    // NavWorld-путь + funnel: полигоны пути превращаются в углы (участки между NavMeshLink считаются отдельно).
    [BurstCompile]
    public struct SwarmPathJob : IJob
    {
        public NavWorld World;
        public NavQueryBuffer Buffer;
        [ReadOnly] public NativeArray<SwarmPathRequest> Requests;
        [ReadOnly] public NativeArray<int> RequestCount;
        [NativeDisableContainerSafetyRestriction] public NativeArray<SwarmPathResult> Results;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> Corners;
        public int Worker, Workers;
        public int AgentType, AreaMask;
        public float3 Extents;
        public float Time;

        public void Execute()
        {
            int n = RequestCount[0];
            for (int k = Worker; k < n; k += Workers)
            {
                SwarmPathRequest req = Requests[k];
                SwarmPathResult res = new SwarmPathResult { Valid = true, GoalAtRequest = req.Goal, Time = Time, State = SwarmPathState.Idle };

                NavLocation start = req.StartLoc;
                if (!req.StartLocUsable || !World.IsValid(start))
                {
                    start = World.MapLocation(req.StartPos, Extents, AgentType, AreaMask);
                    res.LocChanged = true;
                    res.Loc = start;
                }
                NavLocation goal = req.PersonalGoal ? World.MapLocation(req.Goal, Extents, AgentType, AreaMask) : req.GoalLoc;
                if (!World.IsValid(start) || !World.IsValid(goal))
                {
                    res.Status = (byte)SwarmPathStatus.Invalid;
                    res.ClearGoal = req.PersonalGoal;
                    Results[k] = res;
                    continue;
                }

                res.PathGoal = goal.position;

                NavQueryStatus status = World.BeginFindPath(Buffer, start, goal, AreaMask, default);
                int guard = 0;
                while ((status & NavQueryStatus.InProgress) != 0 && guard++ < 64) status = World.ContinueFindPath(Buffer, 512, out int _);

                // Признак недостижимой цели (путь только до ближайшей точки) приходит в статусе EndFindPath.
                NavQueryStatus end = World.EndFindPath(Buffer, out int pathSize);
                bool partial = (end & NavQueryStatus.PartialResult) != 0 || (status & NavQueryStatus.PartialResult) != 0;
                if ((status & NavQueryStatus.Success) == 0 || pathSize <= 0)
                {
                    res.Status = (byte)SwarmPathStatus.Invalid;
                    res.ClearGoal = req.PersonalGoal;
                    Results[k] = res;
                    continue;
                }

                NativeArray<NavNode> polys = new NativeArray<NavNode>(pathSize, Allocator.Temp);
                World.GetResultFromFindPath(Buffer, polys);

                int count = BuildCorners(polys, pathSize, start.position, goal.position, partial, req.Agent * SwarmNav.MaxCorners, out uint linkMask);
                polys.Dispose();

                res.LinkMask = linkMask;
                res.CornerCount = (byte)count;
                res.CornerIndex = (byte)(count > 1 ? 1 : 0);
                res.Status = (byte)(partial ? SwarmPathStatus.Partial : SwarmPathStatus.Complete);
                res.State = count > 1 ? SwarmPathState.Following : SwarmPathState.Idle;
                res.ClearGoal = count <= 1 && req.PersonalGoal;
                Results[k] = res;
            }
        }

        private int BuildCorners(NativeArray<NavNode> polys, int pathSize, float3 startPos, float3 goalPos, bool partial, int cornerBase, out uint linkMask)
        {
            linkMask = 0;
            int count = 0;
            float3 runStart = startPos;
            int runFirst = 0;
            for (int i = 0; i < pathSize && count < SwarmNav.MaxCorners; i++)
            {
                bool lastPolyOfRun = i == pathSize - 1 || World.GetNodeType(polys[i + 1]) == NavNodeType.Link;
                if (World.GetNodeType(polys[i]) == NavNodeType.Link || !lastPolyOfRun) continue;

                // Участок полигонов runFirst..i: funnel от runStart до входа в связь (или до цели).
                bool ends = i == pathSize - 1;
                float3 runEnd = goalPos;
                if (!ends)
                {
                    World.GetPortalPoints(polys[i], polys[i + 1], out Vector3 el, out Vector3 er);
                    runEnd = ((float3)el + (float3)er) * 0.5f;
                }
                else if (partial)
                {
                    // Цель недостижима: идём к середине последнего портала (ближайшая достижимая точка).
                    if (i > runFirst)
                    {
                        World.GetPortalPoints(polys[i - 1], polys[i], out Vector3 pl, out Vector3 pr);
                        runEnd = ((float3)pl + (float3)pr) * 0.5f;
                    }
                    else
                    {
                        runEnd = runStart;
                    }
                }

                int portals = i - runFirst;
                NativeArray<float3> left = new NativeArray<float3>(portals + 2, Allocator.Temp);
                NativeArray<float3> right = new NativeArray<float3>(portals + 2, Allocator.Temp);
                left[0] = runStart;
                right[0] = runStart;
                for (int p = 0; p < portals; p++)
                {
                    World.GetPortalPoints(polys[runFirst + p], polys[runFirst + p + 1], out Vector3 l, out Vector3 r);
                    left[p + 1] = l;
                    right[p + 1] = r;
                }
                left[portals + 1] = runEnd;
                right[portals + 1] = runEnd;

                count += SwarmFunnel.Run(left, right, portals + 2, Corners, cornerBase + count, SwarmNav.MaxCorners - count);
                left.Dispose();
                right.Dispose();

                if (ends) break;

                // Связь: последний угол участка - вход (прыжок к следующему углу = выход).
                linkMask |= 1u << (count - 1);
                World.GetPortalPoints(polys[i + 1], polys[i + 2 < pathSize ? i + 2 : i + 1], out Vector3 xl, out Vector3 xr);
                runStart = ((float3)xl + (float3)xr) * 0.5f;
                runFirst = i + 2;
                i += 1; // перескочить узел-связь
            }
            return count;
        }
    }

    // Сливает результаты путей прошлого кадра в состояние агентов (в начале кадра, до Steer). Устаревшие результаты отбрасываются:
    // агент удалён, его слот занят другим (Generation) или цель/позиция менялись после запроса (Serial).
    [BurstCompile]
    public struct SwarmMergePathsJob : IJob
    {
        [ReadOnly] public NativeArray<SwarmAgentState> Agents;
        [ReadOnly] public NativeArray<SwarmPathRequest> Requests;
        [ReadOnly] public NativeArray<int> RequestCount;
        [ReadOnly] public NativeArray<SwarmPathResult> Results;
        public NativeArray<SwarmAgentNav> Nav;

        public void Execute()
        {
            int n = RequestCount[0];
            for (int k = 0; k < n; k++)
            {
                SwarmPathResult res = Results[k];
                if (!res.Valid) continue;

                SwarmPathRequest req = Requests[k];
                if (Agents[req.Agent].Generation != req.Generation) continue;

                SwarmAgentNav nav = Nav[req.Agent];
                if (nav.Serial != req.Serial) continue;

                nav.GoalAtRequest = res.GoalAtRequest;
                nav.LastPathTime = res.Time;
                nav.PathGoal = res.PathGoal;
                nav.CornerCount = res.CornerCount;
                nav.CornerIndex = res.CornerIndex;
                nav.LinkMask = res.LinkMask;
                nav.Jump = 0f;
                nav.Status = res.Status;
                nav.State = res.State;
                if (res.LocChanged)
                {
                    nav.Loc = res.Loc;
                    nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
                }
                if (res.ClearGoal) nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.HasGoal);
                Nav[req.Agent] = nav;
            }
        }
    }

    // Куда и как идёт агент: углы пути, прыжок по NavMeshLink, прямой ход к близкой цели, прибытие + разведение с соседями
    // (spatial hash). Позиции соседей читаются из Agents, пишет их только MoveJob (следующий в цепочке) - гонок нет.
    [BurstCompile]
    public struct SwarmSteerJob : IJobParallelFor
    {
        public NavWorld World;
        [ReadOnly] public NativeArray<SwarmAgentState> Agents;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> Buckets;
        [ReadOnly] public NativeArray<float3> Corners;
        [ReadOnly] public NativeArray<float3> GroupGoalPos;
        public NativeArray<SwarmAgentNav> Nav;
        public NativeArray<SwarmStep> Steps;
        [ReadOnly] public NativeArray<float> Radii;

        public float DeltaTime;
        public int Frame, MidEvery, FarEvery;
        public float AvoidanceRadius, SeparationWeight, FlowWeight, BucketSize;
        public int MaxNeighbors;
        public bool SeparateFar;
        public float DirectRange;
        public int AreaMask;

        // Разведение на GPU (SwarmAvoidanceGpu): толчок, давление и касания прибывших своей группы; без него - хэш на CPU.
        public bool UseGpuAvoid;
        [ReadOnly] public NativeArray<float4> Avoid;
        [ReadOnly] public NativeArray<float> GroupJamRadius;
        public float PersonalJamRadius;
        public bool JamEnabled;
        public bool WalkWhilePending;

        public void Execute(int index)
        {
            Steps[index] = default;

            SwarmAgentNav nav = Nav[index];
            if ((nav.Flags & SwarmAgentFlags.HasGoal) == 0 || (nav.Flags & SwarmAgentFlags.Stopped) != 0) return;
            bool following = nav.State == SwarmPathState.Following;
            bool waiting = WalkWhilePending && nav.State == SwarmPathState.Requested;   // пути ещё нет: идёт по прямой к цели, пока он считается
            if (!following && !waiting && DirectRange <= 0f) return;

            SwarmAgentState agent = Agents[index];
            float dt = DeltaTime;
            int every = agent.Tier == SwarmLodTier.Near ? 1 : agent.Tier == SwarmLodTier.Mid ? MidEvery : FarEvery;
            if (every > 1)
            {
                if ((Frame + index) % every != 0)
                {
                    if (following || waiting || nav.Group >= 0)
                    {
                        nav.PendingDt += DeltaTime;
                        Nav[index] = nav;
                    }
                    return;
                }

                dt += nav.PendingDt;
                nav.PendingDt = 0f;
            }

            float3 pos = agent.Position;
            float3 goalPos = nav.Group >= 0 ? GroupGoalPos[nav.Group] : nav.Goal;
            float remaining = math.distance(pos.xz, goalPos.xz);
            bool near = DirectRange > 0f && remaining <= DirectRange && nav.Jump <= 0f && (nav.Flags & SwarmAgentFlags.LocStale) == 0 && World.IsValid(nav.Loc);
            if (!following && !near && !waiting)
            {
                nav.PendingDt = 0f;
                Nav[index] = nav;
                return;
            }
            nav.Remaining = remaining;

            if (nav.Jump > 0f)
            {
                Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                Nav[index] = nav;
                return;
            }

            float4 avoid = UseGpuAvoid ? Avoid[index] : float4.zero;

            // Толпа у общей цели: коснулся прибывшего из своей группы - значит место занято, встаём (диск растёт наружу).
            // Не ближе радиуса, который займёт вся группа, и не для недостижимой цели.
            if (JamEnabled && avoid.w > 0.5f && nav.Status != (byte)SwarmPathStatus.Partial)
            {
                float jamRadius = nav.Group >= 0 ? GroupJamRadius[nav.Group] + nav.StoppingDistance + 3f : PersonalJamRadius;
                if (remaining <= jamRadius)
                {
                    Arrive(ref nav);
                    nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.Direct);
                    Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                    Nav[index] = nav;
                    return;
                }
            }

            bool wasDirect = (nav.Flags & SwarmAgentFlags.Direct) != 0;
            bool direct = false;
            if (near)
            {
                // Остановка за StoppingDistance до цели (как NavMeshAgent); у стоящего у цели - с запасом, чтобы не дёргался на границе.
                float stop = nav.StoppingDistance + (nav.State == SwarmPathState.Idle ? 0.5f : 0f);
                if (remaining <= stop && nav.Status != (byte)SwarmPathStatus.Partial)
                {
                    Arrive(ref nav);
                    nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.Direct);
                    Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                    Nav[index] = nav;
                    return;
                }

                NavQueryStatus rs = World.Raycast(out NavMeshHit hit, nav.Loc, goalPos, AreaMask, default);
                direct = (rs & NavQueryStatus.Success) != 0 && !hit.hit;
            }

            float2 toTarget = float2.zero;
            if (direct)
            {
                nav.Flags = (byte)(nav.Flags | SwarmAgentFlags.Direct);
                toTarget = goalPos.xz - pos.xz;
            }
            else
            {
                if (wasDirect)
                {
                    // Потеряли прямую видимость: старый путь устарел, нужен новый.
                    nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.Direct);
                    nav.State = SwarmPathState.Requested;
                    nav.LastPathTime = -1e9f;
                    Nav[index] = nav;
                    return;
                }
                if (!following && !waiting)
                {
                    Nav[index] = nav;
                    return;
                }

                if (waiting)
                {
                    // Пути ещё нет: идём к цели по прямой (MoveLocation остановит у края NavMesh), путь придёт и поправит маршрут.
                    toTarget = remaining > nav.StoppingDistance ? goalPos.xz - pos.xz : float2.zero;
                }
                else
                {
                // Остановка за StoppingDistance до цели; недостижимая цель - только по концу пути.
                if (nav.Status != (byte)SwarmPathStatus.Partial && nav.StoppingDistance > 0.01f
                    && math.distance(pos.xz, nav.PathGoal.xz) <= nav.StoppingDistance)
                {
                    Arrive(ref nav);
                    Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                    Nav[index] = nav;
                    return;
                }

                int count = nav.CornerCount;
                int ci = nav.CornerIndex;
                int cornerBase = index * SwarmNav.MaxCorners;
                float reach = math.max(agent.Speed * dt, 0.2f);
                bool moving = false;
                for (int guard = 0; guard < 4; guard++)
                {
                    float3 corner = Corners[cornerBase + ci];
                    toTarget = corner.xz - pos.xz;
                    if (math.length(toTarget) > reach)
                    {
                        moving = true;
                        break;
                    }

                    // Угол-вход в NavMeshLink: следующий угол - выход, начинаем прыжок.
                    if ((nav.LinkMask & (1u << ci)) != 0 && ci + 1 < count)
                    {
                        nav.CornerIndex = (byte)(ci + 1);
                        nav.Jump = 0.0001f;
                        Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                        Nav[index] = nav;
                        return;
                    }

                    ci++;
                    if (ci >= count)
                    {
                        FinishPath(ref nav, pos);
                        Steps[index] = new SwarmStep { Dir = float3.zero, Dt = dt };
                        Nav[index] = nav;
                        return;
                    }
                }
                nav.CornerIndex = (byte)ci;
                if (!moving) toTarget = float2.zero;
                }
            }

            float3 flow = math.lengthsq(toTarget) > 1e-8f ? math.normalize(new float3(toTarget.x, 0f, toTarget.y)) : float3.zero;
            float3 separation = UseGpuAvoid ? new float3(avoid.x, 0f, avoid.y)
                : agent.Tier != SwarmLodTier.Far || SeparateFar ? Separate(index, pos) : float3.zero;
            float3 blended = flow * FlowWeight + separation * SeparationWeight;

            // Длина направления - доля скорости: упёршийся в плотную группу агент ползёт, а не рвётся вперёд на полной.
            float lengthSq = math.lengthsq(blended);
            float3 dir = lengthSq > 1f ? blended * math.rsqrt(lengthSq) : blended;

            Steps[index] = new SwarmStep { Dir = dir, Dt = dt };
            Nav[index] = nav;
        }

        // Конец углов: у цели - прибытие, иначе (путь усечён по MaxCorners или цель сместилась) - новый путь.
        private static void FinishPath(ref SwarmAgentNav nav, float3 pos)
        {
            float reach = math.max(nav.StoppingDistance, 0.5f) + 0.5f;
            if (nav.Status == (byte)SwarmPathStatus.Partial || math.distance(pos.xz, nav.PathGoal.xz) <= reach) Arrive(ref nav);
            else nav.State = SwarmPathState.Requested;
        }

        private static void Arrive(ref SwarmAgentNav nav)
        {
            nav.State = SwarmPathState.Idle;
            nav.CurrentSpeed = 0f;
            if (nav.Group < 0) nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.HasGoal);
        }

        private float3 Separate(int index, float3 position)
        {
            float3 separation = float3.zero;
            int2 cell = (int2)math.floor(position.xz / BucketSize);
            float selfRadius = Radii[index] > 0f ? Radii[index] : AvoidanceRadius * 0.5f;
            int considered = 0;

            for (int dz = -1; dz <= 1 && considered < MaxNeighbors; dz++)
            {
                for (int dx = -1; dx <= 1 && considered < MaxNeighbors; dx++)
                {
                    int key = SwarmSpatialHash.BucketKeyFromCell(cell + new int2(dx, dz));
                    if (!Buckets.TryGetFirstValue(key, out int other, out NativeParallelMultiHashMapIterator<int> iterator)) continue;

                    do
                    {
                        if (other == index) continue;

                        float3 offset = position - Agents[other].Position;
                        offset.y = 0f;
                        float distance = math.length(offset);
                        considered++;
                        float pairRadius = selfRadius + (Radii[other] > 0f ? Radii[other] : AvoidanceRadius * 0.5f);
                        if (distance <= 0.0001f || distance >= pairRadius) continue;

                        separation += offset / distance * ((pairRadius - distance) / pairRadius);
                    }
                    while (considered < MaxNeighbors && Buckets.TryGetNextValue(out other, ref iterator));
                }
            }
            return separation;
        }
    }

    // Шаг агента по NavMesh: MoveLocation даёт и контакт с поверхностью (стены), и высоту; прыжок по NavMeshLink - прямая между входом и выходом.
    [BurstCompile]
    public struct SwarmMoveJob : IJobParallelFor
    {
        public NavWorld World;
        [ReadOnly] public NativeArray<SwarmStep> Steps;
        [ReadOnly] public NativeArray<float3> Corners;
        public NativeArray<SwarmAgentNav> Nav;
        public NativeArray<SwarmAgentState> Agents;
        public int AgentType, AreaMask;
        public float3 Extents;
        public float LinkSpeedMultiplier;
        public bool FarDeadReckoning;
        [NativeDisableParallelForRestriction] public NativeArray<int> MovedCounters;
        [NativeSetThreadIndex] private int _threadIndex;

        public void Execute(int index)
        {
            SwarmStep step = Steps[index];
            if (step.Dt <= 0f) return;

            SwarmAgentNav nav = Nav[index];
            SwarmAgentState agent = Agents[index];

            // Far-агент (вне кадра) двигается вдоль углов пути без MoveLocation: цена MoveLocation растёт с пройденным
            // расстоянием, а Far шагает раз в N кадров сразу на N шагов, так что LOD её не экономил.
            bool farWalk = FarDeadReckoning && agent.Tier == SwarmLodTier.Far && nav.State == SwarmPathState.Following
                           && nav.Jump <= 0f && (nav.Flags & SwarmAgentFlags.Direct) == 0 && math.lengthsq(step.Dir) > 1e-6f;
            if (farWalk)
            {
                float3 corner = Corners[index * SwarmNav.MaxCorners + nav.CornerIndex];
                float3 position = agent.Position;
                float2 toCorner = corner.xz - position.xz;
                float distance = math.length(toCorner);
                if (distance > 1e-4f)
                {
                    float travel = math.min(agent.Speed * step.Dt * math.min(1f, math.length(step.Dir)), distance);
                    position.xz += toCorner / distance * travel;
                    position.y = math.lerp(position.y, corner.y, travel / distance);
                    agent.Position = position;
                    agent.Velocity = step.Dir;
                    nav.Flags = (byte)(nav.Flags | SwarmAgentFlags.LocStale);
                    MovedCounters[_threadIndex * 16]++;
                    Nav[index] = nav;
                    Agents[index] = agent;
                    return;
                }
            }

            if ((nav.Flags & SwarmAgentFlags.LocStale) != 0)
            {
                // Вышел из Far (или встал на путь заново): привязка к полигону там, где агент сейчас.
                nav.Loc = World.MapLocation(agent.Position, Extents, AgentType, AreaMask);
                nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
            }

            if (!World.IsValid(nav.Loc))
            {
                nav.Loc = World.MapLocation(agent.Position, Extents, AgentType, AreaMask);
                if (!World.IsValid(nav.Loc))
                {
                    Nav[index] = nav;
                    return;
                }
            }

            if (nav.Jump > 0f || math.lengthsq(step.Dir) > 1e-6f) MovedCounters[_threadIndex * 16]++;

            if (nav.Jump > 0f)
            {
                MoveAlongLink(ref nav, ref agent, index, step.Dt);
                Nav[index] = nav;
                Agents[index] = agent;
                return;
            }

            // Разгон и торможение у цели (как acceleration + autoBraking у NavMeshAgent); Acceleration 0 - скорость сразу полная.
            float dirLength = math.length(step.Dir);
            float targetSpeed = agent.Speed * math.min(1f, dirLength);
            if (nav.Acceleration > 0f)
            {
                if ((nav.Flags & SwarmAgentFlags.HasGoal) != 0 && nav.Status != (byte)SwarmPathStatus.Partial)
                {
                    float brakeDistance = math.max(0f, nav.Remaining - nav.StoppingDistance);
                    targetSpeed = math.min(targetSpeed, math.max(math.sqrt(2f * nav.Acceleration * brakeDistance), 0.5f));
                }
                float change = nav.Acceleration * step.Dt;
                nav.CurrentSpeed = math.clamp(targetSpeed, nav.CurrentSpeed - change, nav.CurrentSpeed + change);
            }
            else
            {
                nav.CurrentSpeed = targetSpeed;
            }

            float3 direction = dirLength > 1e-3f ? step.Dir / dirLength : float3.zero;
            agent.Velocity = agent.Speed > 0f ? direction * (nav.CurrentSpeed / agent.Speed) : float3.zero;
            if (dirLength > 1e-3f && nav.CurrentSpeed > 0f)
            {
                float3 pos = agent.Position;
                float3 next = pos + new float3(direction.x, 0f, direction.z) * (nav.CurrentSpeed * step.Dt);
                nav.Loc = World.MoveLocation(nav.Loc, next, AreaMask);
                agent.Position = nav.Loc.position;
            }

            Nav[index] = nav;
            Agents[index] = agent;
        }

        private void MoveAlongLink(ref SwarmAgentNav nav, ref SwarmAgentState agent, int index, float dt)
        {
            int ci = nav.CornerIndex;
            float3 from = Corners[index * SwarmNav.MaxCorners + ci - 1];
            float3 to = Corners[index * SwarmNav.MaxCorners + ci];
            float length = math.max(0.01f, math.distance(from, to));
            float t = nav.Jump + agent.Speed * LinkSpeedMultiplier * dt / length;

            float2 flat = to.xz - from.xz;
            agent.Velocity = math.lengthsq(flat) > 1e-6f ? math.normalizesafe(new float3(flat.x, 0f, flat.y)) : float3.zero;
            if (t < 1f)
            {
                nav.Jump = t;
                agent.Position = math.lerp(from, to, t);
                return;
            }

            // Приземление: привязка к полигону на выходе связи.
            nav.Jump = 0f;
            NavLocation landed = World.MapLocation(to, Extents, AgentType, AreaMask);
            if (!World.IsValid(landed))
            {
                nav.State = SwarmPathState.Requested;
                return;
            }

            nav.Loc = landed;
            agent.Position = landed.position;
            if (ci + 1 >= nav.CornerCount) nav.State = SwarmPathState.Requested;
            else nav.CornerIndex = (byte)(ci + 1);
        }
    }

    // Simple Stupid Funnel Algorithm: углы кратчайшего пути внутри коридора порталов. Если углов больше maxCorners,
    // путь усекается (агент дойдёт до последнего угла и запросит остаток).
    [BurstCompile]
    internal static class SwarmFunnel
    {
        public static int Run(NativeArray<float3> portalLeft, NativeArray<float3> portalRight, int portalCount,
            NativeArray<float3> corners, int cornerBase, int maxCorners)
        {
            int count = 0;
            float3 apex = portalLeft[0], leftPoint = portalLeft[0], rightPoint = portalRight[0];
            int apexIndex = 0, leftIndex = 0, rightIndex = 0;
            corners[cornerBase + count++] = apex;

            for (int i = 1; i < portalCount; i++)
            {
                float3 newLeft = portalLeft[i], newRight = portalRight[i];

                if (TriArea2(apex, rightPoint, newRight) <= 0f)
                {
                    if (Equal(apex, rightPoint) || TriArea2(apex, leftPoint, newRight) > 0f)
                    {
                        rightPoint = newRight;
                        rightIndex = i;
                    }
                    else
                    {
                        if (count >= maxCorners) return count;
                        corners[cornerBase + count++] = leftPoint;
                        apex = leftPoint;
                        apexIndex = leftIndex;
                        leftPoint = apex;
                        rightPoint = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                if (TriArea2(apex, leftPoint, newLeft) >= 0f)
                {
                    if (Equal(apex, leftPoint) || TriArea2(apex, rightPoint, newLeft) < 0f)
                    {
                        leftPoint = newLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        if (count >= maxCorners) return count;
                        corners[cornerBase + count++] = rightPoint;
                        apex = rightPoint;
                        apexIndex = rightIndex;
                        leftPoint = apex;
                        rightPoint = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            float3 goalPoint = portalLeft[portalCount - 1];
            if (count < maxCorners && !Equal(corners[cornerBase + count - 1], goalPoint)) corners[cornerBase + count++] = goalPoint;
            return count;
        }

        private static float TriArea2(float3 a, float3 b, float3 c) => (c.x - a.x) * (b.z - a.z) - (b.x - a.x) * (c.z - a.z);
        private static bool Equal(float3 a, float3 b) => math.distancesq(a, b) < 1e-8f;
    }
}
