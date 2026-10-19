using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace PG.SwarmNavigation
{
    // Мост между SwarmNav и настоящими GameObject: хранит TransformAccessArray (из TransformHandle) и параметры,
    // которые касаются только Transform (поворот, baseOffset, updatePosition/updateRotation), а навигацию целиком
    // отдаёт SwarmNav. За кадр - один IJobParallelForTransform, поставленный после джобов SwarmNav.
    //
    // SwarmAgent - тонкая обёртка над одним общим SwarmCrowd (через SwarmNavRuntime): у него нет ни своей математики,
    // ни своего Update().
    public class SwarmCrowd : System.IDisposable
    {
        private readonly SwarmNav _nav;

        private TransformAccessArray _transforms;
        private NativeList<int> _slots;                 // слот SwarmNav, индекс == индекс в _transforms
        private NativeList<quaternion> _rotations;
        private NativeList<float> _angularSpeeds;
        private NativeList<float> _baseOffsets;         // как NavMeshAgent.baseOffset: смещение Y над поверхностью
        private NativeList<byte> _applyPosition;        // как NavMeshAgent.updatePosition/updateRotation
        private NativeList<byte> _applyRotation;
        private NativeList<float3> _lastPositions;      // последняя записанная в Transform позиция - пишем только при изменении
        private NativeList<int> _chaseIndex;            // индекс в _chaseGroups, -1 - нет погони за Transform

        // Движущиеся цели (погоня) - ОДНА запись на уникальный Transform, а не на агента: тысячи агентов за одним игроком
        // читают Transform.position один раз за кадр.
        private struct ChaseGroup
        {
            public TransformHandle Target;
            public int GroupKey;
            public int Refs;
        }
        private readonly List<ChaseGroup> _chaseGroups = new List<ChaseGroup>();
        private readonly Stack<int> _freeChaseGroups = new Stack<int>();
        private readonly Dictionary<int, int> _chaseKeyToGroup = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _slotToIndex = new Dictionary<int, int>();

        private NativeArray<float4> _frustum;          // 6 плоскостей камеры для сглаживания только видимых Mid/Far
        private readonly Plane[] _planeCache = new Plane[6];
        private bool _hasFrustum;
        private bool _disposed;

        public bool IsDisposed => _disposed;
        public int Count => _transforms.isCreated ? _transforms.length : 0;
        public SwarmNav Nav => _nav;

        public SwarmCrowd(SwarmNavSettings settings, int maxAgents)
        {
            _nav = new SwarmNav(settings, maxAgents);

            int capacity = math.min(maxAgents, 1024);
            _transforms = new TransformAccessArray(capacity, settings.TransformJobCount);
            _slots = new NativeList<int>(capacity, Allocator.Persistent);
            _rotations = new NativeList<quaternion>(capacity, Allocator.Persistent);
            _angularSpeeds = new NativeList<float>(capacity, Allocator.Persistent);
            _baseOffsets = new NativeList<float>(capacity, Allocator.Persistent);
            _applyPosition = new NativeList<byte>(capacity, Allocator.Persistent);
            _applyRotation = new NativeList<byte>(capacity, Allocator.Persistent);
            _lastPositions = new NativeList<float3>(capacity, Allocator.Persistent);
            _chaseIndex = new NativeList<int>(capacity, Allocator.Persistent);
            _frustum = new NativeArray<float4>(6, Allocator.Persistent);
        }

        private bool TryGetIndex(SwarmNavHandle handle, out int index)
        {
            index = -1;
            return _nav.IsHandleValid(handle) && _slotToIndex.TryGetValue(handle.Slot, out index);
        }

        public SwarmNavHandle AddAgent(GameObject go, float speed = 3.5f, float stoppingDistance = 0.5f,
            float angularSpeed = 360f, bool updatePosition = true, bool updateRotation = true, float baseOffset = 0f)
        {
            SyncTransforms();
            Vector3 position = go.transform.position;
            SwarmNavHandle handle = _nav.RegisterAgent((float3)position - new float3(0f, baseOffset, 0f), speed, stoppingDistance);
            if (!_nav.IsHandleValid(handle)) return handle;

            int index = _transforms.length;
            _transforms.Add(go.transformHandle);
            _slots.Add(handle.Slot);
            _rotations.Add((quaternion)go.transform.rotation);
            _angularSpeeds.Add(angularSpeed);
            _baseOffsets.Add(baseOffset);
            _applyPosition.Add(updatePosition ? (byte)1 : (byte)0);
            _applyRotation.Add(updateRotation ? (byte)1 : (byte)0);
            _lastPositions.Add((float3)position);
            _chaseIndex.Add(-1);

            _slotToIndex[handle.Slot] = index;
            return handle;
        }

        public void RemoveAgent(SwarmNavHandle handle)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();

            ReleaseChaseGroup(_chaseIndex[index]);

            _transforms.RemoveAtSwapBack(index);
            _slots.RemoveAtSwapBack(index);
            _rotations.RemoveAtSwapBack(index);
            _angularSpeeds.RemoveAtSwapBack(index);
            _baseOffsets.RemoveAtSwapBack(index);
            _applyPosition.RemoveAtSwapBack(index);
            _applyRotation.RemoveAtSwapBack(index);
            _lastPositions.RemoveAtSwapBack(index);
            _chaseIndex.RemoveAtSwapBack(index);

            // _slots[index] теперь хранит слот перемещённого агента (если удалили не последнего)
            if (index < _slots.Length) _slotToIndex[_slots[index]] = index;

            _slotToIndex.Remove(handle.Slot);
            _nav.UnregisterAgent(handle);
        }

        // Неподвижная точка.
        public void SetDestination(SwarmNavHandle handle, Vector3 destination)
        {
            if (!TryGetIndex(handle, out int index)) return;

            ReleaseChaseGroup(_chaseIndex[index]);
            _chaseIndex[index] = -1;
            _nav.SetDestination(handle, (float3)destination);
        }

        // Движущаяся цель (погоня за игроком/врагом): позиция читается из target каждый кадр, повторно вызывать не нужно.
        public void SetDestination(SwarmNavHandle handle, TransformHandle target)
        {
            if (target == TransformHandle.None || !TryGetIndex(handle, out int index)) return;

            int groupKey = SwarmNavRuntime.GroupKeyForTarget(target);
            int current = _chaseIndex[index];
            if (current < 0 || _chaseGroups[current].GroupKey != groupKey)
            {
                ReleaseChaseGroup(current);
                _chaseIndex[index] = AcquireChaseGroup(target, groupKey);
            }

            _nav.SetSharedGoal(handle, (float3)target.position, groupKey);
        }

        public void ResetPath(SwarmNavHandle handle)
        {
            if (!TryGetIndex(handle, out int index)) return;

            ReleaseChaseGroup(_chaseIndex[index]);
            _chaseIndex[index] = -1;
            _nav.ResetPath(handle);
        }

        public void SetStopped(SwarmNavHandle handle, bool stopped) => _nav.SetStopped(handle, stopped);
        public void SetSpeed(SwarmNavHandle handle, float speed) => _nav.SetSpeed(handle, speed);
        public void SetStoppingDistance(SwarmNavHandle handle, float stoppingDistance) => _nav.SetStoppingDistance(handle, stoppingDistance);
        public void SetAcceleration(SwarmNavHandle handle, float acceleration) => _nav.SetAcceleration(handle, acceleration);
        public void SetRadius(SwarmNavHandle handle, float radius) => _nav.SetRadius(handle, radius);

        public void SetBaseOffset(SwarmNavHandle handle, float baseOffset)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();
            _baseOffsets[index] = baseOffset;
        }

        public void SetAngularSpeed(SwarmNavHandle handle, float angularSpeedDegrees)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();
            _angularSpeeds[index] = angularSpeedDegrees;
        }

        public void SetUpdatePosition(SwarmNavHandle handle, bool value)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();
            _applyPosition[index] = value ? (byte)1 : (byte)0;
        }

        public void SetUpdateRotation(SwarmNavHandle handle, bool value)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();
            _applyRotation[index] = value ? (byte)1 : (byte)0;
        }

        public float GetRemainingDistance(SwarmNavHandle handle) => _nav.GetRemainingDistance(handle);
        public bool HasDestination(SwarmNavHandle handle) => _nav.HasDestination(handle);
        public SwarmPathStatus GetPathStatus(SwarmNavHandle handle) => _nav.GetPathStatus(handle);
        public bool IsOnNavMesh(SwarmNavHandle handle) => _nav.IsAgentOnNavMesh(handle);
        public Vector3 GetVelocityDirection(SwarmNavHandle handle) => (Vector3)_nav.GetSteeringDirection(handle);

        // Телепорт: сразу и Transform (напрямую через хранимый TransformHandle), и позиция внутри SwarmNav.
        public void Warp(SwarmNavHandle handle, Vector3 position)
        {
            if (!TryGetIndex(handle, out int index)) return;
            SyncTransforms();

            _nav.SetAgentPosition(handle, (float3)position - new float3(0f, _baseOffsets[index], 0f));
            Vector3 snapped = (Vector3)(_nav.GetAgentPosition(handle) + new float3(0f, _baseOffsets[index], 0f));
            TransformHandle transformHandle = _transforms.GetTransformHandle(index);
            transformHandle.position = snapped;
            _lastPositions[index] = (float3)snapped;
        }

        // Аналог "agent.nextPosition = selfHandle.position" у NavMeshAgent - сообщить SwarmNav, где агент реально сейчас.
        public void SyncPosition(SwarmNavHandle handle, Vector3 actualPosition)
        {
            if (!TryGetIndex(handle, out int index)) return;
            _nav.SyncAgentPosition(handle, (float3)actualPosition - new float3(0f, _baseOffsets[index], 0f));
        }

        // Аналог NavMeshAgent.Move(offset) при updatePosition = false.
        public Vector3 Move(SwarmNavHandle handle, Vector3 offset)
        {
            if (!TryGetIndex(handle, out int index)) return Vector3.zero;
            return (Vector3)(_nav.Move(handle, (float3)offset) + new float3(0f, _baseOffsets[index], 0f));
        }

        // Камера, по которой сглаживаются дальние агенты (null - сглаживание выключено).
        public void SetSmoothingCamera(Camera camera)
        {
            _hasFrustum = camera != null;
            if (!_hasFrustum) return;

            GeometryUtility.CalculateFrustumPlanes(camera, _planeCache);
            for (int i = 0; i < 6; i++) _frustum[i] = new float4(_planeCache[i].normal.x, _planeCache[i].normal.y, _planeCache[i].normal.z, _planeCache[i].distance);
        }

        // Раз в кадр: цели погони -> джобы SwarmNav -> запись Transform. Завершает Complete() (в LateUpdate драйвера).
        public void Schedule(float deltaTime)
        {
            for (int g = 0; g < _chaseGroups.Count; g++)
            {
                ChaseGroup group = _chaseGroups[g];
                if (group.Refs <= 0 || !group.Target.IsValid()) continue;

                _nav.UpdateSharedGoalPosition(group.GroupKey, (float3)group.Target.position);
            }

            JobHandle handle = _nav.ScheduleTick(deltaTime);
            if (!_nav.IsRunning || _transforms.length == 0) return;

            handle = new WriteTransformsJob
            {
                Agents = _nav.AgentsRaw,
                Slots = _slots.AsArray(),
                Rotations = _rotations.AsArray(),
                AngularSpeeds = _angularSpeeds.AsArray(),
                BaseOffsets = _baseOffsets.AsArray(),
                ApplyPosition = _applyPosition.AsArray(),
                ApplyRotation = _applyRotation.AsArray(),
                LastPositions = _lastPositions.AsArray(),
                DeltaTime = deltaTime,
                SmoothCatchUp = _hasFrustum ? math.max(0f, _nav.Settings.LodSmoothing) : 0f,
                SnapDistance = 8f,
                Frustum = _frustum
            }.Schedule(_transforms, handle);
            handle = _nav.Stage(5, handle);
            _nav.SetTail(handle);
        }

        public void Complete() => _nav.Complete();

        // Данные Transform-джоба (поворот, LastPositions) трогаем только когда он не идёт.
        private void SyncTransforms() => _nav.Complete();

        private int AcquireChaseGroup(TransformHandle target, int groupKey)
        {
            if (_chaseKeyToGroup.TryGetValue(groupKey, out int existing))
            {
                ChaseGroup group = _chaseGroups[existing];
                group.Refs++;
                _chaseGroups[existing] = group;
                return existing;
            }

            ChaseGroup created = new ChaseGroup { Target = target, GroupKey = groupKey, Refs = 1 };
            int index;
            if (_freeChaseGroups.Count > 0)
            {
                index = _freeChaseGroups.Pop();
                _chaseGroups[index] = created;
            }
            else
            {
                index = _chaseGroups.Count;
                _chaseGroups.Add(created);
            }

            _chaseKeyToGroup[groupKey] = index;
            return index;
        }

        private void ReleaseChaseGroup(int index)
        {
            if (index < 0) return;

            ChaseGroup group = _chaseGroups[index];
            group.Refs--;
            if (group.Refs > 0)
            {
                _chaseGroups[index] = group;
                return;
            }

            _chaseKeyToGroup.Remove(group.GroupKey);
            _chaseGroups[index] = default;
            _freeChaseGroups.Push(index);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _nav.Complete();
            _disposed = true;

            if (_transforms.isCreated) _transforms.Dispose();
            if (_slots.IsCreated) _slots.Dispose();
            if (_rotations.IsCreated) _rotations.Dispose();
            if (_angularSpeeds.IsCreated) _angularSpeeds.Dispose();
            if (_baseOffsets.IsCreated) _baseOffsets.Dispose();
            if (_applyPosition.IsCreated) _applyPosition.Dispose();
            if (_applyRotation.IsCreated) _applyRotation.Dispose();
            if (_lastPositions.IsCreated) _lastPositions.Dispose();
            if (_chaseIndex.IsCreated) _chaseIndex.Dispose();
            if (_frustum.IsCreated) _frustum.Dispose();

            _nav.Dispose();
        }

        // Запись Transform по результату SwarmNav: позиция - только при изменении, поворот плавно к направлению шага.
        // Агенты лежат в корне сцены или под разными родителями: параллелизм джоба идёт по иерархиям.
        [BurstCompile]
        private struct WriteTransformsJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<SwarmAgentState> Agents;
            [ReadOnly] public NativeArray<int> Slots;
            [NativeDisableParallelForRestriction] public NativeArray<quaternion> Rotations;
            [ReadOnly] public NativeArray<float> AngularSpeeds;
            [ReadOnly] public NativeArray<float> BaseOffsets;
            [ReadOnly] public NativeArray<byte> ApplyPosition;
            [ReadOnly] public NativeArray<byte> ApplyRotation;
            [NativeDisableParallelForRestriction] public NativeArray<float3> LastPositions;
            public float DeltaTime;
            public float SmoothCatchUp;
            [ReadOnly] public NativeArray<float4> Frustum;
            public float SnapDistance;

            private bool InFrustum(float3 point)
            {
                for (int p = 0; p < 6; p++)
                {
                    float4 plane = Frustum[p];
                    if (math.dot(plane.xyz, point) + plane.w < -3f) return false; // 3 м запаса: агент у края кадра сглаживается заранее
                }
                return true;
            }

            public void Execute(int i, TransformAccess transform)
            {
                SwarmAgentState agent = Agents[Slots[i]];


                bool writePosition = false;
                float3 pos = default;
                if (ApplyPosition[i] != 0)
                {
                    pos = agent.Position + new float3(0f, BaseOffsets[i], 0f);

                    // Mid/Far шагают раз в N кадров - без сглаживания они движутся рывками. Показываем позицию, которая
                    // догоняет настоящую на скорости агента (небольшой запас, чтобы не копить отставание); телепорт и
                    // прыжок по NavMeshLink (далеко) - сразу.
                    if (agent.Tier != SwarmLodTier.Near && SmoothCatchUp > 0f && InFrustum(pos))
                    {
                        float3 shown = LastPositions[i];
                        float3 delta = pos - shown;
                        float distance = math.length(delta);
                        float maxStep = agent.Speed * SmoothCatchUp * DeltaTime;
                        if (distance > SnapDistance) { }
                        else if (distance > maxStep && maxStep > 0f) pos = shown + delta / distance * maxStep;
                    }

                    writePosition = math.any(pos != LastPositions[i]);
                }

                bool writeRotation = false;
                quaternion rotation = Rotations[i];
                // Поворот считаем, когда агент сдвинулся (у шагающих по LOD раз в N кадров - редко) или позицией управляют снаружи.
                if (ApplyRotation[i] != 0 && (writePosition || ApplyPosition[i] == 0))
                {
                    float3 flat = new float3(agent.Velocity.x, 0f, agent.Velocity.z);
                    if (math.lengthsq(flat) > 1e-4f)
                    {
                        quaternion target = quaternion.LookRotationSafe(flat, math.up());
                        quaternion next = RotateTowards(rotation, target, math.radians(AngularSpeeds[i]) * DeltaTime);
                        writeRotation = math.any(next.value != rotation.value);
                        rotation = next;
                        Rotations[i] = next;
                    }
                }

                if (!writePosition && !writeRotation) return;

                if (writePosition && writeRotation) transform.SetPositionAndRotation((Vector3)pos, (Quaternion)rotation);
                else if (writePosition) transform.position = (Vector3)pos;
                else transform.rotation = (Quaternion)rotation;

                if (writePosition) LastPositions[i] = pos;
            }
        }

        // quaternion.RotateTowards нет в Unity.Mathematics - угол между ориентациями + slerp ровно на допустимую долю.
        private static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
        {
            float dot = math.dot(from.value, to.value);
            if (dot < 0f) to.value = -to.value;
            dot = math.abs(dot);
            if (dot > 0.99999f) return to;

            float angle = math.acos(math.clamp(dot, -1f, 1f)) * 2f;
            if (angle <= maxRadians || angle < 1e-5f) return to;

            return math.slerp(from, to, maxRadians / angle);
        }
    }
}
