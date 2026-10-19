using System.Collections.Generic;
using Unity.AI.Navigation.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace PG.SwarmNavigation
{
    // Ядро навигации: пути, высота и контакт со стенами берутся у NavMesh Unity (NavWorld), поэтому всё, что
    // видит NavMeshSurface (земля, острова, NavMeshLink), работает без своего бейка. Агентов и цели SwarmNav
    // хранит в blittable-массивах и гонит весь кадр Burst-джобами: LOD -> spatial hash -> пути (воркеры) ->
    // направление + разведение -> шаг по NavMesh. Внешний код (SwarmCrowd, свой RenderMeshIndirect) читает Agents.
    //
    // Любой метод этого класса сам дожидается джобов текущего кадра (Sync), так что вызывать их можно из любого Update.
    public sealed class SwarmNav : System.IDisposable
    {
        public const int MaxCorners = 24;
        private const int MaxGroups = 256;

        // Свободный слот: далеко от мира, чтобы не попадать в spatial hash рядом с настоящими агентами.
        private static readonly SwarmAgentState Ghost = new SwarmAgentState { Position = new float3(1e6f, 0f, 1e6f), Tier = SwarmLodTier.Far };

        public static SwarmNav Instance { get; private set; }

        // Только после Complete()/Tick(): пока джобы идут, массив занят ими.
        public NativeArray<SwarmAgentState> Agents { get { SyncMain(); return _agents; } }
        internal NativeArray<SwarmAgentState> AgentsRaw => _agents;
        public SwarmLodSettings LodSettings => _lod.Settings;
        public SwarmNavSettings Settings => _settings;
        public int HighWaterMark => _highWater;
        public int PathsLastFrame { get; private set; }
        public double LastWaitMs { get; private set; }
        public bool IsRunning => _running;
        public bool GpuAvoidanceActive => _avoidGpu != null;
        // Сколько агентов сдвинулось в последнем кадре (0 - вся толпа стоит: GPU-разведение и выгрузку можно пропускать).
        public int MovedLastFrame { get; private set; }

        // Диагностика: true - после каждой стадии кадра ждём её джобы и пишем время в StageMs (без перекрытия, кадр медленнее).
        // Стадии: 0 LOD+hash, 3 направление+разведение, 4 шаг по NavMesh, 1 выбор заявок, 2 пути (после шага), 5 запись Transform.
        public bool ProfileStages;
        public readonly double[] StageMs = new double[6];
        public static readonly string[] StageNames = { "LOD+hash", "заявки", "пути", "направление+разведение", "шаг по NavMesh", "запись Transform" };

        // GPU-копия Agents (раскладка SwarmAgentState) для RenderMeshIndirect/ComputeShader; null, пока не запрошена.
        public GraphicsBuffer AgentsGpu => _agentsGpu;

        private readonly SwarmNavSettings _settings;
        private readonly int _maxAgents;
        private readonly NavWorld _world;
        private readonly SwarmLodScheduler _lod;
        private readonly SwarmSpatialHash _hash;
        private readonly int _workers;
        private readonly NavQueryBuffer[] _buffers;

        private NativeArray<SwarmAgentState> _agents;
        private NativeArray<SwarmAgentNav> _nav;
        private NativeArray<float3> _corners;
        private NativeArray<SwarmStep> _steps;
        private NativeArray<SwarmPathRequest> _requests;
        private NativeArray<SwarmPathResult> _results;
        private NativeArray<int> _requestCount;
        private NativeArray<float3> _groupGoalPos;
        private NativeArray<NavLocation> _groupGoalLoc;
        private GraphicsBuffer _agentsGpu;
        private SwarmAvoidanceGpu _avoidGpu;
        private NativeArray<float4> _packed;
        private NativeArray<float4> _avoidDummy;   // пустой вход SteerJob, когда разведение на CPU
        private NativeArray<float> _groupJamRadius;
        private NativeArray<int> _movedCounters;     // по 16 int (отдельная кэш-линия) на поток джоб-системы
        private NativeArray<float> _radii;           // радиус агента для разведения (0 - AvoidanceRadius / 2)
        private float _maxRadius;
        private JobHandle _packHandle;
        private bool _packPending;

        private readonly bool[] _slotActive;
        private readonly byte[] _generations;
        private readonly Stack<int> _freeSlots = new Stack<int>();
        private readonly Dictionary<int, int> _groupByKey = new Dictionary<int, int>();
        private readonly int[] _groupRefs = new int[MaxGroups];
        private readonly int[] _groupKeys = new int[MaxGroups];
        private readonly Stack<int> _freeGroups = new Stack<int>();

        private int _highWater;
        private int _frame;
        private int _nextFocusId;
        private bool _running;
        private bool _disposed;
        private JobHandle _tail;
        private JobHandle _pathsHandle;
        private bool _pathsPending;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        public SwarmNav(SwarmNavSettings settings, int maxAgents)
        {
            _settings = settings;
            _maxAgents = math.max(1, maxAgents);
            _world = NavWorld.GetDefaultWorld();
            if (!_world.IsValid()) Debug.LogError("SwarmNav: NavWorld недоступен - на сцене нет запечённого NavMesh (NavMeshSurface).");

            _lod = new SwarmLodScheduler(settings.Lod);
            _hash = new SwarmSpatialHash(settings.SpatialHashBucketSize, math.max(_maxAgents, 16));

            int totalWorkers = math.max(1, JobsUtility.JobWorkerCount);
            _workers = settings.PathWorkers > 0 ? math.min(settings.PathWorkers, totalWorkers) : totalWorkers;
            _buffers = new NavQueryBuffer[_workers];
            for (int i = 0; i < _workers; i++) _buffers[i] = new NavQueryBuffer(_world, Allocator.Persistent, settings.PathBufferSize);

            _agents = new NativeArray<SwarmAgentState>(_maxAgents, Allocator.Persistent);
            for (int i = 0; i < _maxAgents; i++) _agents[i] = Ghost;
            _nav = new NativeArray<SwarmAgentNav>(_maxAgents, Allocator.Persistent);
            _corners = new NativeArray<float3>(_maxAgents * MaxCorners, Allocator.Persistent);
            _steps = new NativeArray<SwarmStep>(_maxAgents, Allocator.Persistent);
            int requestCapacity = math.max(1, math.min(_maxAgents, settings.PathRequestsPerFrame));
            _requests = new NativeArray<SwarmPathRequest>(requestCapacity, Allocator.Persistent);
            _results = new NativeArray<SwarmPathResult>(requestCapacity, Allocator.Persistent);
            _requestCount = new NativeArray<int>(1, Allocator.Persistent);
            _groupGoalPos = new NativeArray<float3>(MaxGroups, Allocator.Persistent);
            _groupGoalLoc = new NativeArray<NavLocation>(MaxGroups, Allocator.Persistent);
            _groupJamRadius = new NativeArray<float>(MaxGroups, Allocator.Persistent);
            _avoidDummy = new NativeArray<float4>(1, Allocator.Persistent);
            _movedCounters = new NativeArray<int>(JobsUtility.MaxJobThreadCount * 16, Allocator.Persistent);
            _radii = new NativeArray<float>(_maxAgents, Allocator.Persistent);

            if (settings.GpuAvoidance)
            {
                _avoidGpu = SwarmAvoidanceGpu.TryCreate(_maxAgents);
                if (_avoidGpu != null)
                {
                    _packed = new NativeArray<float4>(_maxAgents, Allocator.Persistent);
                }
            }

            _slotActive = new bool[_maxAgents];
            _generations = new byte[_maxAgents];
            for (int i = _maxAgents - 1; i >= 0; i--) _freeSlots.Push(i);
            for (int g = MaxGroups - 1; g >= 0; g--) _freeGroups.Push(g);

            Instance = this;
        }

        // Создаёт GraphicsBuffer с агентами; после этого он обновляется в конце каждого Complete().
        public GraphicsBuffer EnableGpuBuffer()
        {
            if (_agentsGpu == null) _agentsGpu = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxAgents, UnsafeUtility.SizeOf<SwarmAgentState>());
            return _agentsGpu;
        }

        // ---------------------------------------------------------------------------------------------
        // Агенты
        // ---------------------------------------------------------------------------------------------

        public SwarmNavHandle RegisterAgent(float3 startPosition, float speed = 3.5f, float stoppingDistance = 0.5f)
        {
            Sync();
            if (_freeSlots.Count == 0)
            {
                Debug.LogWarning("SwarmNav: пул агентов исчерпан (SwarmNavSettings.MaxAgents), RegisterAgent игнорируется.");
                return SwarmNavHandle.Invalid;
            }

            int slot = _freeSlots.Pop();
            _slotActive[slot] = true;
            if (slot >= _highWater) _highWater = slot + 1;

            NavLocation loc = MapLocation(startPosition);
            if (!_world.IsValid(loc) && _world.IsValid()) loc = _world.MapLocation(startPosition, _settings.SpawnSnapExtents, _settings.AgentTypeId, _settings.AreaMask);
            SwarmAgentState agent = SwarmAgentState.Create(_world.IsValid(loc) ? (float3)loc.position : startPosition, speed);
            agent.Generation = _generations[slot];
            _agents[slot] = agent;
            _nav[slot] = new SwarmAgentNav { Loc = loc, Group = -1, StoppingDistance = stoppingDistance };
            _steps[slot] = default;
            _radii[slot] = 0f;

            return new SwarmNavHandle(slot, _generations[slot]);
        }

        public void UnregisterAgent(SwarmNavHandle handle)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            int slot = handle.Slot;

            ReleaseGroupRef(_nav[slot].Group);
            _generations[slot]++;
            _agents[slot] = Ghost;
            _nav[slot] = new SwarmAgentNav { Group = -1 };
            _steps[slot] = default;
            _slotActive[slot] = false;
            _freeSlots.Push(slot);

            while (_highWater > 0 && !_slotActive[_highWater - 1]) _highWater--;
        }

        public bool IsHandleValid(SwarmNavHandle handle) =>
            handle.Slot >= 0 && handle.Slot < _maxAgents && _slotActive[handle.Slot] && _generations[handle.Slot] == handle.Generation;

        // ---------------------------------------------------------------------------------------------
        // Цели
        // ---------------------------------------------------------------------------------------------

        // Личная цель агента: свой путь по NavMesh.
        public void SetDestination(SwarmNavHandle handle, float3 goal)
        {
            Sync();
            if (!IsHandleValid(handle)) return;

            SwarmAgentNav nav = _nav[handle.Slot];
            ReleaseGroupRef(nav.Group);
            nav.Group = -1;
            nav.Goal = goal;
            BeginGoal(ref nav);
            _nav[handle.Slot] = nav;
        }

        // Общая цель (погоня, сбор у точки): агенты с одним groupKey идут к одной, возможно движущейся точке
        // (UpdateSharedGoalPosition); путь у каждого свой и пересчитывается, когда цель заметно сместилась.
        public void SetSharedGoal(SwarmNavHandle handle, float3 goal, int groupKey)
        {
            Sync();
            if (!IsHandleValid(handle)) return;

            SwarmAgentNav nav = _nav[handle.Slot];
            if (!_groupByKey.TryGetValue(groupKey, out int group) || nav.Group != group)
            {
                ReleaseGroupRef(nav.Group);
                if (!_groupByKey.TryGetValue(groupKey, out group))
                {
                    if (_freeGroups.Count == 0)
                    {
                        nav.Group = -1;
                        nav.Goal = goal;
                        BeginGoal(ref nav);
                        _nav[handle.Slot] = nav;
                        return;
                    }

                    group = _freeGroups.Pop();
                    _groupByKey[groupKey] = group;
                    _groupKeys[group] = groupKey;
                    _groupRefs[group] = 0;
                }

                _groupRefs[group]++;
                nav.Group = group;
                BeginGoal(ref nav);
            }

            SetGroupGoal(group, goal);
            _nav[handle.Slot] = nav;
        }

        public void UpdateSharedGoalPosition(int groupKey, float3 position)
        {
            if (!_groupByKey.TryGetValue(groupKey, out int group)) return;

            Sync();
            SetGroupGoal(group, position);
        }

        public void ReleaseSharedGoal(int groupKey)
        {
            Sync();
            if (_groupByKey.TryGetValue(groupKey, out int group)) ReleaseGroupRef(group);
        }

        // Аналог NavMeshAgent.ResetPath().
        public void ResetPath(SwarmNavHandle handle)
        {
            Sync();
            if (!IsHandleValid(handle)) return;

            SwarmAgentNav nav = _nav[handle.Slot];
            ReleaseGroupRef(nav.Group);
            nav.Group = -1;
            nav.Serial++;
            nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.HasGoal);
            nav.State = SwarmPathState.Idle;
            nav.Status = (byte)SwarmPathStatus.None;
            nav.CornerCount = 0;
            nav.Jump = 0f;
            nav.CurrentSpeed = 0f;
            nav.Remaining = float.PositiveInfinity;
            _nav[handle.Slot] = nav;

            SwarmAgentState agent = _agents[handle.Slot];
            agent.Velocity = float3.zero;
            _agents[handle.Slot] = agent;
        }

        private static void BeginGoal(ref SwarmAgentNav nav)
        {
            nav.Serial++;   // старый результат пути для прежней цели станет недействительным
            nav.Flags = (byte)(nav.Flags | SwarmAgentFlags.HasGoal);
            nav.State = SwarmPathState.Requested;
            nav.Status = (byte)SwarmPathStatus.Pending;
            nav.Jump = 0f;
            nav.Remaining = float.PositiveInfinity;
        }

        private void SetGroupGoal(int group, float3 position)
        {
            _groupGoalPos[group] = position;
            NavLocation loc = MapLocation(position);
            if (_world.IsValid(loc)) _groupGoalLoc[group] = loc;
        }

        private void ReleaseGroupRef(int group)
        {
            if (group < 0) return;

            if (--_groupRefs[group] > 0) return;
            _groupByKey.Remove(_groupKeys[group]);
            _freeGroups.Push(group);
        }

        // ---------------------------------------------------------------------------------------------
        // Состояние и параметры агента
        // ---------------------------------------------------------------------------------------------

        public void SetSpeed(SwarmNavHandle handle, float speed)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            SwarmAgentState agent = _agents[handle.Slot];
            agent.Speed = speed;
            _agents[handle.Slot] = agent;
        }

        // Как NavMeshAgent.acceleration: м/с². 0 - скорость меняется мгновенно.
        public void SetAcceleration(SwarmNavHandle handle, float acceleration)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            SwarmAgentNav nav = _nav[handle.Slot];
            nav.Acceleration = math.max(0f, acceleration);
            _nav[handle.Slot] = nav;
        }

        // Как NavMeshAgent.radius: два агента расходятся на сумму радиусов. 0 - AvoidanceRadius / 2 из настроек.
        public void SetRadius(SwarmNavHandle handle, float radius)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            _radii[handle.Slot] = math.max(0f, radius);
            _maxRadius = math.max(_maxRadius, radius);
        }

        public void SetStoppingDistance(SwarmNavHandle handle, float stoppingDistance)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            SwarmAgentNav nav = _nav[handle.Slot];
            nav.StoppingDistance = stoppingDistance;
            _nav[handle.Slot] = nav;
        }

        public void SetStopped(SwarmNavHandle handle, bool stopped)
        {
            Sync();
            if (!IsHandleValid(handle)) return;
            SwarmAgentNav nav = _nav[handle.Slot];
            nav.Flags = stopped ? (byte)(nav.Flags | SwarmAgentFlags.Stopped) : (byte)(nav.Flags & ~SwarmAgentFlags.Stopped);
            _nav[handle.Slot] = nav;

            if (!stopped) return;
            nav.CurrentSpeed = 0f;
            _nav[handle.Slot] = nav;
            SwarmAgentState agent = _agents[handle.Slot];
            agent.Velocity = float3.zero;
            _agents[handle.Slot] = agent;
        }

        public float GetRemainingDistance(SwarmNavHandle handle)
        {
            Sync();
            return IsHandleValid(handle) ? _nav[handle.Slot].Remaining : float.PositiveInfinity;
        }

        public bool HasDestination(SwarmNavHandle handle)
        {
            Sync();
            return IsHandleValid(handle) && (_nav[handle.Slot].Flags & SwarmAgentFlags.HasGoal) != 0;
        }

        public SwarmPathStatus GetPathStatus(SwarmNavHandle handle)
        {
            Sync();
            if (!IsHandleValid(handle)) return SwarmPathStatus.None;
            SwarmAgentNav nav = _nav[handle.Slot];
            return nav.State == SwarmPathState.Requested ? SwarmPathStatus.Pending : (SwarmPathStatus)nav.Status;
        }

        // Аналог NavMeshAgent.isOnNavMesh.
        public bool IsAgentOnNavMesh(SwarmNavHandle handle)
        {
            Sync();
            return IsHandleValid(handle) && _world.IsValid(_nav[handle.Slot].Loc);
        }

        public float3 GetAgentPosition(SwarmNavHandle handle)
        {
            SyncMain();
            return IsHandleValid(handle) ? _agents[handle.Slot].Position : float3.zero;
        }

        public float3 GetSteeringDirection(SwarmNavHandle handle)
        {
            SyncMain();
            return IsHandleValid(handle) ? _agents[handle.Slot].Velocity : float3.zero;
        }

        // Телепорт: привязка к NavMesh в новой точке, путь сбрасывается на повторный расчёт.
        public void SetAgentPosition(SwarmNavHandle handle, float3 worldPos)
        {
            Sync();
            if (!IsHandleValid(handle)) return;

            SwarmAgentNav nav = _nav[handle.Slot];
            nav.Loc = MapLocation(worldPos);
            nav.Serial++;
            nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
            nav.Jump = 0f;
            nav.CurrentSpeed = 0f;
            if ((nav.Flags & SwarmAgentFlags.HasGoal) != 0) nav.State = SwarmPathState.Requested;
            _nav[handle.Slot] = nav;

            SwarmAgentState agent = _agents[handle.Slot];
            agent.Position = _world.IsValid(nav.Loc) ? (float3)nav.Loc.position : worldPos;
            _agents[handle.Slot] = agent;
        }

        // Аналог NavMeshAgent.nextPosition: агент сдвинут снаружи (root motion) - сдвигаем привязку по NavMesh, путь не трогаем.
        public void SyncAgentPosition(SwarmNavHandle handle, float3 worldPos)
        {
            Sync();
            if (!IsHandleValid(handle)) return;

            SwarmAgentNav nav = _nav[handle.Slot];
            SwarmAgentState agent = _agents[handle.Slot];
            if (!_world.IsValid(nav.Loc) || (nav.Flags & SwarmAgentFlags.LocStale) != 0)
            {
                nav.Loc = MapLocation(worldPos);
                nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
            }
            else
            {
                nav.Loc = _world.MoveLocation(nav.Loc, worldPos, _settings.AreaMask);
            }

            agent.Position = _world.IsValid(nav.Loc) ? (float3)nav.Loc.position : worldPos;
            _nav[handle.Slot] = nav;
            _agents[handle.Slot] = agent;
        }

        // Аналог NavMeshAgent.Move: смещение посчитал кто-то другой (RootMotion), поверхность и стены применяются здесь.
        public float3 Move(SwarmNavHandle handle, float3 offset)
        {
            Sync();
            if (!IsHandleValid(handle)) return float3.zero;

            SwarmAgentNav nav = _nav[handle.Slot];
            SwarmAgentState agent = _agents[handle.Slot];
            if (!_world.IsValid(nav.Loc) || (nav.Flags & SwarmAgentFlags.LocStale) != 0)
            {
                nav.Loc = MapLocation(agent.Position);
                nav.Flags = (byte)(nav.Flags & ~SwarmAgentFlags.LocStale);
            }
            if (_world.IsValid(nav.Loc))
            {
                nav.Loc = _world.MoveLocation(nav.Loc, agent.Position + offset, _settings.AreaMask);
                agent.Position = nav.Loc.position;
            }
            else
            {
                agent.Position += offset;
            }

            _nav[handle.Slot] = nav;
            _agents[handle.Slot] = agent;
            return agent.Position;
        }

        // Точечная реакция на внешнее событие (взрыв, удар): толкает агентов рядом с worldPos прочь от него (с учётом NavMesh).
        // Один параллельный Burst-джоб по всем агентам; синхронный, поэтому вызывать для редких событий.
        public void ApplyImpulseAt(float3 worldPos, float radius, float strength)
        {
            Sync();
            if (radius <= 0f || _highWater == 0 || !_world.IsValid()) return;

            JobHandle handle = new SwarmImpulseJob
            {
                World = _world, Agents = _agents, Nav = _nav, Center = worldPos, Radius = radius, Strength = strength,
                AgentType = _settings.AgentTypeId, AreaMask = _settings.AreaMask, Extents = _settings.QueryExtents
            }.Schedule(_highWater, 64);
            _world.AddDependency(handle);
            handle.Complete();
        }

        // ---------------------------------------------------------------------------------------------
        // LOD
        // ---------------------------------------------------------------------------------------------

        public int RegisterFocusPoint(float3 worldPos)
        {
            int id = _nextFocusId++;
            _lod.RegisterFocusPoint(id, worldPos);
            return id;
        }

        public void UpdateFocusPoint(int focusId, float3 worldPos) => _lod.UpdateFocusPoint(focusId, worldPos);

        public void UnregisterFocusPoint(int focusId) => _lod.UnregisterFocusPoint(focusId);

        // ---------------------------------------------------------------------------------------------
        // Кадр
        // ---------------------------------------------------------------------------------------------

        // Синхронный кадр: Schedule + Complete (для кода, который сразу читает Agents).
        public void Tick(float deltaTime)
        {
            ScheduleTick(deltaTime);
            Complete();
        }

        // Ставит джобы кадра и возвращает их хвост; кто добавляет свои джобы после (SwarmCrowd) - передаёт итоговый хэндл в SetTail.
        public JobHandle ScheduleTick(float deltaTime)
        {
            SyncAll();   // единственное место, где ждём пути прошлого кадра: на границе кадров они уже посчитаны
            if (_disposed || _highWater == 0 || !_world.IsValid())
            {
                _requestCount[0] = 0;
                _frame++;
                return default;
            }

            int hw = _highWater;
            int frame = _frame++;
            float time = Time.time;
            SwarmLodSettings lod = _lod.Settings;

            bool gpu = _avoidGpu != null;
            if (gpu) _avoidGpu.Poll();
            for (int t = 0; t < _movedCounters.Length; t += 16) _movedCounters[t] = 0;
            UpdateGroupJamRadius();

            JobHandle lodHandle = _lod.ScheduleTierUpdates(_agents, hw, default);

            // Результаты путей прошлого кадра -> состояние агентов (до Steer).
            JobHandle merge = new SwarmMergePathsJob { Agents = _agents, Requests = _requests, RequestCount = _requestCount, Results = _results, Nav = _nav }.Schedule(lodHandle);
            JobHandle hashHandle = Stage(0, gpu ? lodHandle : _hash.ScheduleBuild(_agents, hw, lodHandle, _settings.SeparateFarAgents));

            JobHandle steer = new SwarmSteerJob
            {
                World = _world, AreaMask = _settings.AreaMask, DirectRange = _settings.DirectMoveRange, Agents = _agents, Buckets = _hash.Buckets, Corners = _corners, GroupGoalPos = _groupGoalPos, Nav = _nav, Steps = _steps, Radii = _radii,
                DeltaTime = deltaTime, Frame = frame, MidEvery = math.max(1, lod.MidUpdateEveryNFrames), FarEvery = math.max(1, lod.FarUpdateEveryNFrames),
                AvoidanceRadius = _settings.AvoidanceRadius, SeparationWeight = _settings.SeparationWeight, FlowWeight = _settings.FlowWeight,
                BucketSize = _hash.BucketSize, MaxNeighbors = _settings.MaxNeighborsPerAgent, SeparateFar = _settings.SeparateFarAgents,
                UseGpuAvoid = gpu, Avoid = gpu ? _avoidGpu.Latest : _avoidDummy, GroupJamRadius = _groupJamRadius,
                PersonalJamRadius = _settings.PersonalJamRadius, JamEnabled = gpu && _settings.ArrivalJam, WalkWhilePending = _settings.WalkWhilePathPending
            }.Schedule(hw, 64, JobHandle.CombineDependencies(hashHandle, merge));
            steer = Stage(3, steer);

            JobHandle move = new SwarmMoveJob
            {
                World = _world, Steps = _steps, Corners = _corners, Nav = _nav, Agents = _agents,
                AgentType = _settings.AgentTypeId, AreaMask = _settings.AreaMask, Extents = _settings.QueryExtents,
                LinkSpeedMultiplier = _settings.LinkSpeedMultiplier, FarDeadReckoning = _settings.FarDeadReckoning, MovedCounters = _movedCounters
            }.Schedule(hw, 64, steer);
            move = Stage(4, move);

            if (gpu)
            {
                _packHandle = new SwarmPackAgentsJob { Agents = _agents, Nav = _nav, Radii = _radii, DefaultRadius = _settings.AvoidanceRadius * 0.5f, Packed = _packed }.Schedule(hw, 256, move);
                _packPending = true;
                move = _packHandle;   // пути и запись Transform идут после упаковки (оба читают Nav/Agents, пишет только пути)
            }

            // Выбор заявок идёт в основной цепочке (он читает Nav, и вызовы API после Complete() его не ждут), сами пути - в фоне после
            // шага: у них только своя копия запроса и общий массив углов, поэтому вызовы API агента пути не ждут. Путь текущего кадра
            // вступает в силу со следующего (SwarmMergePathsJob), ждёт его только следующий ScheduleTick.
            JobHandle collect = new SwarmCollectRequestsJob
            {
                Agents = _agents, GroupGoalPos = _groupGoalPos, GroupGoalLoc = _groupGoalLoc, Nav = _nav, Requests = _requests, RequestCount = _requestCount,
                HighWater = hw, Budget = math.min(_requests.Length, hw), StartOffset = (frame * 997) % hw, Time = time,
                ChaseDistance = _settings.ChaseRepathDistance, ChaseFraction = _settings.ChaseRepathFraction, Cooldown = _settings.RepathCooldown
            }.Schedule(move);
            collect = Stage(1, collect);

            JobHandle paths = default;
            for (int w = 0; w < _workers; w++)
            {
                paths = JobHandle.CombineDependencies(paths, new SwarmPathJob
                {
                    World = _world, Buffer = _buffers[w], Requests = _requests, RequestCount = _requestCount, Results = _results, Corners = _corners,
                    Worker = w, Workers = _workers, AgentType = _settings.AgentTypeId, AreaMask = _settings.AreaMask,
                    Extents = _settings.QueryExtents, Time = time
                }.Schedule(collect));
            }
            paths = Stage(2, paths);
            _pathsHandle = paths;
            _pathsPending = true;
            _world.AddDependency(paths);

            SetTail(collect);
            return collect;
        }

        // Радиус, который займёт вся группа вокруг цели при плотной упаковке: дальше него правило "коснулся прибывшего" не действует.
        private void UpdateGroupJamRadius()
        {
            float area = _settings.AvoidanceRadius * _settings.AvoidanceRadius * 0.85f;
            foreach (int group in _groupByKey.Values)
            {
                _groupJamRadius[group] = math.sqrt(_groupRefs[group] * area / math.PI) * 1.3f;
            }
        }

        internal JobHandle Stage(int index, JobHandle handle)
        {
            if (!ProfileStages) return handle;

            _sw.Restart();
            handle.Complete();
            StageMs[index] = _sw.Elapsed.TotalMilliseconds;
            return handle;
        }

        // Итоговый хэндл кадра (с джобами, которые SwarmCrowd поставил после ядра): Complete() и Sync() ждут именно его.
        public void SetTail(JobHandle tail)
        {
            _tail = tail;
            _running = true;
            _world.AddDependency(tail);
        }

        public void Complete()
        {
            if (!_running) return;

            _sw.Restart();
            _tail.Complete();
            _sw.Stop();
            LastWaitMs = _sw.Elapsed.TotalMilliseconds;
            _running = false;

            int moved = 0;
            for (int t = 0; t < _movedCounters.Length; t += 16) moved += _movedCounters[t];
            MovedLastFrame = moved;

            // Упакованные позиции -> GPU: разведение считается, пока рисуется кадр, результат придёт через 1-3 кадра.
            if (_packPending)
            {
                _packHandle.Complete();
                _packPending = false;
                if (moved > 0 && _frame % math.max(1, _settings.GpuAvoidEvery) == 0) _avoidGpu.Dispatch(_packed, _highWater, math.max(_settings.AvoidanceRadius, _maxRadius * 2f), _settings.AvoidanceRadius * 1.1f, _settings.GpuMaxNeighbors);
            }

            if (_agentsGpu != null && _highWater > 0) _agentsGpu.SetData(_agents, 0, 0, _highWater);
        }

        // Любой доступ к данным агентов из главного потока: дождаться основной цепочки кадра. Фоновые пути её не касаются
        // (у них своя копия запроса, углы пишутся в массив, который API не трогает), поэтому вызовы API пути не ждут.
        private void Sync()
        {
            if (_running) Complete();
        }

        private void SyncMain() => Sync();

        // Граница кадров: дождаться и путей прошлого кадра (результаты сольёт SwarmMergePathsJob).
        private void SyncAll()
        {
            Sync();
            if (!_pathsPending) return;

            _pathsHandle.Complete();
            _pathsPending = false;
            PathsLastFrame = _requestCount[0];
        }

        private NavLocation MapLocation(float3 position)
        {
            if (!_world.IsValid()) return default;
            return _world.MapLocation(position, _settings.QueryExtents, _settings.AgentTypeId, _settings.AreaMask);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_running) _tail.Complete();
            if (_pathsPending) _pathsHandle.Complete();
            if (_packPending) _packHandle.Complete();
            _running = false;
            _pathsPending = false;
            _disposed = true;

            _lod.Dispose();
            _hash.Dispose();
            foreach (NavQueryBuffer buffer in _buffers) buffer.Dispose();
            _agentsGpu?.Dispose();
            _agentsGpu = null;

            if (_agents.IsCreated) _agents.Dispose();
            if (_nav.IsCreated) _nav.Dispose();
            if (_corners.IsCreated) _corners.Dispose();
            if (_steps.IsCreated) _steps.Dispose();
            if (_requests.IsCreated) _requests.Dispose();
            if (_results.IsCreated) _results.Dispose();
            if (_requestCount.IsCreated) _requestCount.Dispose();
            if (_groupGoalPos.IsCreated) _groupGoalPos.Dispose();
            if (_groupGoalLoc.IsCreated) _groupGoalLoc.Dispose();
            if (_groupJamRadius.IsCreated) _groupJamRadius.Dispose();
            if (_packed.IsCreated) _packed.Dispose();
            if (_avoidDummy.IsCreated) _avoidDummy.Dispose();
            if (_movedCounters.IsCreated) _movedCounters.Dispose();
            if (_radii.IsCreated) _radii.Dispose();
            _avoidGpu?.Dispose();

            if (Instance == this) Instance = null;
        }
    }
}
