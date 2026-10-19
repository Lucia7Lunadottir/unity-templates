// Animation Rigging integration through RigConstraint<> base class.
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Jobs;

namespace PonyPhysics
{
    [Serializable]
    public struct SpringBoneJobData : IAnimationJobData
    {
        public bool IsValid() => true;
        public void SetDefaultValues() { }
    }

    [BurstCompile]
    public struct SpringBoneAnimJob : IWeightedAnimationJob
    {
        public FloatProperty jobWeight { get; set; }
        public void ProcessRootMotion(AnimationStream s) { }
        public void ProcessAnimation(AnimationStream s)  { }
    }

    [Serializable]
    public class SpringBoneBinder : AnimationJobBinder<SpringBoneAnimJob, SpringBoneJobData>
    {
        public override SpringBoneAnimJob Create(Animator animator, ref SpringBoneJobData data, Component component)
            => new SpringBoneAnimJob();

        public override void Destroy(SpringBoneAnimJob job) { }
    }

    public struct SpringColliderData
    {
        public float3 worldCenter;
        public float3 worldTail;
        public float radius;
        public int type; // 0 = Sphere, 1 = Capsule
    }

    public struct SpringBoneData
    {
        public float3 restLocalDir;
        public float boneLength;
        public float stiffness;
        public float damping;
        public float elasticity;
        public float gravityPower;
        public float3 gravityDir;
        public float3 windForce;
        public float radius;
        public float angleLimitRad;
        public int active;
    }

    [AddComponentMenu("Pony Physics/Spring Bone Manager")]
    [DisallowMultipleComponent]
    public class SpringBoneManager
        : RigConstraint<SpringBoneAnimJob, SpringBoneJobData, SpringBoneBinder>
    {
        [Header("Simulation")]
        [Range(1, 4)] public int subSteps = 2;

        [Tooltip("How strongly the character's velocity sweeps hair/tail backwards.\n0 = none  1 = natural  2 = exaggerated")]
        [Range(0f, 2f)] public float inertiaScale = 0.6f;

        [Tooltip("Constant world-space wind added to all chains.")]
        public Vector3 windForce = Vector3.zero;

        [Header("Colliders")]
        [Tooltip("Auto-gathered from children if left empty.")]
        public List<SpringCollider> globalColliders = new List<SpringCollider>();

        [Header("Debug")]
        public bool drawDebugGizmos = false;

        private SpringBoneChain[] _chains;
        private BoneState[] _allBones;
        private int _boneCount;

        private NativeArray<TransformHandle> _transforms;
        private TransformAccessArray _transformAccess;
        private NativeArray<float3> _currentTips;
        private NativeArray<float3> _prevTips;
        private NativeArray<SpringBoneData> _boneData;
        private NativeArray<SpringColliderData> _colliderData;

        private Vector3 _prevRootPos;
        private Vector3 _rootVelocity;
        private bool _ready;

        protected void OnEnable()
        {
            Initialise();
        }

        protected void OnDisable()
        {
            DisposeNative();
        }

        private void OnDestroy() => DisposeNative();

        private void LateUpdate()
        {
            if (!_ready || _boneCount == 0) return;

            UpdateRootVelocity();
            UploadInputsAndColliders();
            RunSimulation(Time.deltaTime);
        }

        public void Initialise()
        {
            DisposeNative();

            _chains = GetComponentsInChildren<SpringBoneChain>(false);
            if (_chains.Length == 0) return;

            if (globalColliders.Count == 0)
                globalColliders.AddRange(GetComponentsInChildren<SpringCollider>());

            var list = new List<BoneState>();
            foreach (var chain in _chains)
            {
                chain.BuildChain();
                foreach (var b in chain.bones) list.Add(b);
            }

            _allBones = list.ToArray();
            _boneCount = _allBones.Length;
            if (_boneCount == 0) return;

            // Using NativeArray<TransformHandle> as requested
            _transforms = new NativeArray<TransformHandle>(_boneCount, Allocator.Persistent);
            for (int i = 0; i < _boneCount; i++)
            {
                _allBones[i].InitBones();
                _transforms[i] = _allBones[i].transformHandle;
            }
            _transformAccess = new TransformAccessArray(_transforms);

            _currentTips = new NativeArray<float3>(_boneCount, Allocator.Persistent);
            _prevTips = new NativeArray<float3>(_boneCount, Allocator.Persistent);
            _boneData = new NativeArray<SpringBoneData>(_boneCount, Allocator.Persistent);

            for (int i = 0; i < _boneCount; i++)
            {
                _currentTips[i] = _allBones[i].currentTip;
                _prevTips[i] = _allBones[i].prevTip;
            }

            _prevRootPos = transform.position;
            _rootVelocity = Vector3.zero;
            _ready = true;
        }

        public void ResetPose()
        {
            if (!_ready) return;
            int gi = 0;
            foreach (var chain in _chains)
            {
                Vector3 chainScale = chain.transform.lossyScale;
                float liveScale = Mathf.Max(chainScale.x, Mathf.Max(chainScale.y, chainScale.z));
                float scaleRatio = liveScale / chain.bakeScale;

                for (int ci = 0; ci < chain.bones.Length; ci++, gi++)
                {
                    BoneState b = chain.bones[ci];
                    Vector3 tip = b.transform.position +
                                  b.transform.TransformDirection(b.restLocalDir) * (b.boneLength * scaleRatio);
                    b.currentTip = tip;
                    b.prevTip = tip;
                    _currentTips[gi] = tip;
                    _prevTips[gi] = tip;
                }
            }
            _rootVelocity = Vector3.zero;
        }

        private void UpdateRootVelocity()
        {
            Vector3 cur = transform.position;
            Vector3 raw = (cur - _prevRootPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            _rootVelocity = Vector3.Lerp(_rootVelocity, raw, 0.35f);
            _prevRootPos = cur;
        }

        private void UploadInputsAndColliders()
        {
            if (!_colliderData.IsCreated || _colliderData.Length != globalColliders.Count)
            {
                if (_colliderData.IsCreated) _colliderData.Dispose();
                _colliderData = new NativeArray<SpringColliderData>(globalColliders.Count, Allocator.Persistent);
            }

            for (int c = 0; c < globalColliders.Count; c++)
            {
                var col = globalColliders[c];
                if (col)
                {
                    col.RefreshWorldData();
                    _colliderData[c] = new SpringColliderData
                    {
                        worldCenter = col.worldCenter,
                        worldTail = col.worldTail,
                        radius = col.worldRadius,
                        type = (int)col.type
                    };
                }
            }

            Vector3 inertia = -_rootVelocity * inertiaScale;
            int gi = 0;
            foreach (var chain in _chains)
            {
                Vector3 chainScale = chain.transform.lossyScale;
                float liveScale = Mathf.Max(chainScale.x, Mathf.Max(chainScale.y, chainScale.z));
                float scaleRatio = liveScale / chain.bakeScale;

                for (int ci = 0; ci < chain.bones.Length; ci++, gi++)
                {
                    BoneState b = chain.bones[ci];
                    chain.GetCurvedValues(ci, out float s, out float d, out float e,
                                              out float gp, out float alRad, out float r);
                    float t01 = chain.t01Cache[ci];

                    _boneData[gi] = new SpringBoneData
                    {
                        restLocalDir = b.restLocalDir,
                        boneLength = b.boneLength * scaleRatio,
                        stiffness = s,
                        damping = d,
                        elasticity = e,
                        gravityPower = gp,
                        gravityDir = chain.gravityDir,
                        windForce = windForce + inertia * Mathf.Lerp(0.4f, 1.2f, t01),
                        radius = r * scaleRatio,
                        angleLimitRad = alRad,
                        active = 1,
                    };
                }
            }
        }

        private void RunSimulation(float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, 0.05f) / subSteps;
            for (int s = 0; s < subSteps; s++)
            {
                var job = new IntegrateAndApplyJob
                {
                    boneData = _boneData,
                    colliders = _colliderData,
                    currentTips = _currentTips,
                    prevTips = _prevTips,
                    dt = dt
                };

                job.Schedule(_transformAccess).Complete();
            }
        }

        private void DisposeNative()
        {
            _ready = false;
            if (_transformAccess.isCreated) _transformAccess.Dispose();
            if (_transforms.IsCreated) _transforms.Dispose();
            if (_currentTips.IsCreated) _currentTips.Dispose();
            if (_prevTips.IsCreated) _prevTips.Dispose();
            if (_boneData.IsCreated) _boneData.Dispose();
            if (_colliderData.IsCreated) _colliderData.Dispose();
            
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || !_ready) return;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.5f);
            for (int i = 0; i < _boneCount; i++)
                if (_allBones[i].initialised)
                    Gizmos.DrawWireSphere(_currentTips[i], _boneData[i].radius);
        }
#endif
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct IntegrateAndApplyJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<SpringBoneData> boneData;
        [ReadOnly] public NativeArray<SpringColliderData> colliders;
        public NativeArray<float3> currentTips;
        public NativeArray<float3> prevTips;
        public float dt;

        public void Execute(int index, TransformAccess transform)
        {
            SpringBoneData d = boneData[index];
            if (d.active == 0) return;

            float4x4 m = transform.localToWorldMatrix;
            float3 bonePos = m.c3.xyz;
            // math.rotate(float4x4, float3) multiplies by the matrix's upper 3x3, which
            // bakes in scale for a TRS matrix — normalize so all length/scale information
            // comes from d.boneLength (already scale-corrected upstream), not from here too.
            float3 restDir = math.normalize(math.rotate(m, d.restLocalDir));

            float3 cur = currentTips[index];
            float3 prev = prevTips[index];

            float3 vel = (cur - prev) * (1f - d.damping);
            float3 spring = (bonePos + restDir * d.boneLength - cur) * d.stiffness;
            float3 grav = d.gravityDir * (d.gravityPower * dt);
            float3 ext = d.windForce * dt;

            float3 newTip = cur + vel + spring * dt + grav + ext;

            float3 delta = newTip - bonePos;
            float dist = math.length(delta);
            if (dist > 1e-6f)
            {
                float3 dir = delta / dist;
                float angle = math.acos(math.clamp(math.dot(dir, restDir), -1f, 1f));
                if (angle > d.angleLimitRad)
                    dir = RotateToward(restDir, dir, d.angleLimitRad);
                newTip = bonePos + dir * d.boneLength;
            }

            for (int c = 0; c < colliders.Length; c++)
            {
                ResolveCollision(colliders[c], ref newTip, d.radius);
            }

            // Collisions can push the tip further than boneLength away from bonePos
            // (especially once collider/bone radius exceeds the segment length itself).
            // Re-project onto the fixed-length sphere so the rod doesn't stretch — an
            // unprojected stretch here reads as a huge fake velocity next frame (vel is
            // an undamped position delta) and the chain explodes.
            float3 postCollisionDelta = newTip - bonePos;
            float postCollisionDist = math.length(postCollisionDelta);
            if (postCollisionDist > 1e-6f)
                newTip = bonePos + (postCollisionDelta / postCollisionDist) * d.boneLength;

            prevTips[index] = cur;
            currentTips[index] = newTip;

            float3 worldRest = math.rotate(transform.rotation, d.restLocalDir);
            float3 toTip = newTip - bonePos;
            float len = math.length(toTip);

            if (len >= 1e-5f)
            {
                toTip /= len;
                quaternion fromTo = FromToRotation(worldRest, toTip);
                //quaternion fromTo = Quaternion.FromToRotation(worldRest, toTip);
                transform.rotation = math.mul(fromTo, transform.rotation);
            }
        }

        private static quaternion FromToRotation(float3 from, float3 to)
        {
            float3 axis = math.cross(from, to);
            float dot = math.dot(from, to);
            if (dot >= 1.0f) return quaternion.identity;
            if (dot <= -1.0f)
            {
                float3 orth = math.abs(from.x) > 0.1f ? new float3(0, 1, 0) : new float3(1, 0, 0);
                axis = math.normalize(math.cross(from, orth));
                return new quaternion(axis.x, axis.y, axis.z, 0);
            }
            float s = math.sqrt((1.0f + dot) * 2.0f);
            axis /= s;
            return new quaternion(axis.x, axis.y, axis.z, s * 0.5f);
        }

        private static void ResolveCollision(SpringColliderData col, ref float3 point, float boneRadius)
        {
            float combined = col.radius + boneRadius;

            if (col.type == 0) // Sphere
            {
                float3 delta = point - col.worldCenter;
                float dist = math.length(delta);
                if (dist < combined)
                {
                    float3 pushDir = dist < 1e-6f ? new float3(0, 1, 0) : delta / dist;
                    point = col.worldCenter + pushDir * combined;
                }
            }
            else // Capsule
            {
                float3 ab = col.worldTail - col.worldCenter;
                float len = math.length(ab);
                if (len < 1e-5f) return;

                float3 dir = ab / len;
                float t2 = math.clamp(math.dot(point - col.worldCenter, dir), 0f, len);
                float3 closest = col.worldCenter + dir * t2;
                float3 delta = point - closest;
                float dist = math.length(delta);

                if (dist < combined)
                {
                    float3 pushDir = dist < 1e-6f ? new float3(0, 1, 0) : delta / dist;
                    point = closest + pushDir * combined;
                }
            }
        }

        private static float3 RotateToward(float3 from, float3 to, float maxAngle)
        {
            float3 axis = math.cross(from, to);
            float al = math.length(axis);
            if (al < 1e-6f) return from;
            axis /= al;
            float cos = math.cos(maxAngle), sin = math.sin(maxAngle);
            return from * cos + math.cross(axis, from) * sin + axis * math.dot(axis, from) * (1f - cos);
        }
    }
}