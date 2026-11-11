using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Playables;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

namespace PG.TimelineTween
{
    [TrackColor(0.95f, 0.55f, 0.15f)]
    [TrackBindingType(typeof(Transform))]
    [TrackClipType(typeof(TransformTweenClip))]
    [DisplayName("PG/Transform Tween Track")]
    public class TransformTweenTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<TransformTweenMixer>.Create(graph, inputCount);
        }

        protected override Playable CreatePlayable(PlayableGraph graph, GameObject gameObject, TimelineClip clip)
        {
            var playable = base.CreatePlayable(graph, gameObject, clip);
            if (playable.GetPlayableType() == typeof(TransformTweenBehaviour))
            {
                var behaviour = ((ScriptPlayable<TransformTweenBehaviour>)playable).GetBehaviour();
                behaviour.clipIn = clip.clipIn;
                behaviour.clipDuration = clip.duration;
            }
            return playable;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var target = director.GetGenericBinding(this) as Transform;
            if (target != null)
            {
                var go = target.gameObject;
                driver.AddFromName<Transform>(go, "m_LocalPosition.x");
                driver.AddFromName<Transform>(go, "m_LocalPosition.y");
                driver.AddFromName<Transform>(go, "m_LocalPosition.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.x");
                driver.AddFromName<Transform>(go, "m_LocalRotation.y");
                driver.AddFromName<Transform>(go, "m_LocalRotation.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.w");
                if (go.TryGetComponent<Animator>(out _))
                    driver.AddFromName<Animator>(go, "m_ApplyRootMotion");
            }
            base.GatherProperties(director, driver);
        }
    }

    public class TransformTweenMixer : PlayableBehaviour
    {
        Transform _target;
        Animator _animator;
        bool _syncPhysics;
        Vector3 _initialPosition;
        Quaternion _initialRotation;

        bool _rootMotionOverridden;
        bool _savedRootMotion;

        Playable _lastInput;
        bool _hasPose;
        bool _poseMovesPosition;
        bool _poseMovesRotation;
        Vector3 _posePosition;
        Quaternion _poseRotation;

        // Re-snapshot on every graph start so a moved object is not snapped back to an old pose.
        public override void OnGraphStart(Playable playable) => _target = null;

        public override void OnGraphStop(Playable playable) => Release();

        public override void OnPlayableDestroy(Playable playable) => Release();

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as Transform;
            if (target == null)
                return;
            if (_target != target)
                Bind(target);

            Transform parent = target.parent;
            Vector3 position = Vector3.zero;
            Quaternion rotation = new Quaternion(0f, 0f, 0f, 0f);
            float positionWeight = 0f;
            float rotationWeight = 0f;
            float topWeight = 0f;
            RootMotionMode rootMotion = RootMotionMode.Keep;

            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f)
                    continue;

                var input = (ScriptPlayable<TransformTweenBehaviour>)playable.GetInput(i);
                var clip = input.GetBehaviour().clip;
                if (clip == null)
                    continue;

                Sample(input, parent, out var clipPosition, out var clipRotation);
                if (clip.tweenPosition)
                {
                    position += clipPosition * weight;
                    positionWeight += weight;
                }
                if (clip.tweenRotation)
                {
                    rotation = Accumulate(rotation, clipRotation, weight);
                    rotationWeight += weight;
                }
                if (weight > topWeight)
                {
                    topWeight = weight;
                    rootMotion = clip.rootMotion;
                    _lastInput = input;
                }
            }

            if (topWeight <= 0f)
            {
                RestoreRootMotion();
                FinishLastClip(parent);
                TransformTweenLateApply.ReleasePreview(this);
                return;
            }

            ApplyRootMotion(rootMotion);

            position += _initialPosition * Mathf.Max(0f, 1f - positionWeight);
            if (rotationWeight < 1f)
                rotation = Accumulate(rotation, _initialRotation, 1f - rotationWeight);
            SetPose(position, Quaternion.Normalize(rotation), positionWeight > 0f, rotationWeight > 0f);
        }

        // After the last clip ends, its playable is paused at the clip end; apply that pose once so it doesn't stop a frame short.
        void FinishLastClip(Transform parent)
        {
            if (!_lastInput.IsValid())
                return;
            var input = (ScriptPlayable<TransformTweenBehaviour>)_lastInput;
            _lastInput = Playable.Null;
            var clip = input.GetBehaviour().clip;
            if (clip == null)
                return;

            Sample(input, parent, out var position, out var rotation);
            SetPose(position, rotation, clip.tweenPosition, clip.tweenRotation);
        }

        void Bind(Transform target)
        {
            RestoreRootMotion();
            _target = target;
            _animator = target.GetComponent<Animator>();
            _syncPhysics = target.TryGetComponent<CharacterController>(out _) || target.TryGetComponent<Rigidbody>(out _);
            _initialPosition = target.position;
            _initialRotation = target.rotation;
            _lastInput = Playable.Null;
        }

        // Only the channels some active clip tweens are written, so Animator/gameplay keep the rest.
        void SetPose(Vector3 position, Quaternion rotation, bool movesPosition, bool movesRotation)
        {
            if (!movesPosition && !movesRotation)
                return;
            _posePosition = position;
            _poseRotation = rotation;
            _poseMovesPosition = movesPosition;
            _poseMovesRotation = movesRotation;
            _hasPose = true;
            WritePose();
            if (Application.isPlaying)
                TransformTweenLateApply.Enqueue(this);
            else
                TransformTweenLateApply.HoldPreview(this);
        }

        void Release()
        {
            RestoreRootMotion();
            _hasPose = false;
            TransformTweenLateApply.ReleasePreview(this);
        }

        // Timeline preview evaluates the Animator after script tracks, so the pose is written again right before rendering.
        internal void ApplyPreview()
        {
            if (_hasPose && _target != null)
                WritePose();
        }

        // Re-applied after the Animator so Animation Track offsets / root motion cannot move the object.
        internal bool LateApply()
        {
            if (!_hasPose || _target == null)
                return false;
            _hasPose = false;
            WritePose();
            return _syncPhysics;
        }

        void WritePose()
        {
            if (_poseMovesPosition && _poseMovesRotation)
                _target.SetPositionAndRotation(_posePosition, _poseRotation);
            else if (_poseMovesPosition)
                _target.position = _posePosition;
            else
                _target.rotation = _poseRotation;
        }

        void ApplyRootMotion(RootMotionMode mode)
        {
            if (mode == RootMotionMode.Keep || _animator == null)
            {
                RestoreRootMotion();
                return;
            }
            if (!_rootMotionOverridden)
            {
                _savedRootMotion = _animator.applyRootMotion;
                _rootMotionOverridden = true;
            }
            _animator.applyRootMotion = mode == RootMotionMode.On;
        }

        void RestoreRootMotion()
        {
            if (!_rootMotionOverridden)
                return;
            _rootMotionOverridden = false;
            if (_animator != null)
                _animator.applyRootMotion = _savedRootMotion;
        }

        static void Sample(ScriptPlayable<TransformTweenBehaviour> input, Transform parent, out Vector3 position, out Quaternion rotation)
        {
            var behaviour = input.GetBehaviour();
            float progress = behaviour.clip.Evaluate(input.GetTime(), behaviour.clipIn, behaviour.clipDuration);
            behaviour.clip.GetPose(progress, parent, out position, out rotation);
        }

        // Weighted quaternion sum with hemisphere fix; normalized once at the end.
        static Quaternion Accumulate(Quaternion sum, Quaternion q, float weight)
        {
            if (Quaternion.Dot(sum, q) < 0f)
                weight = -weight;
            return new Quaternion(sum.x + q.x * weight, sum.y + q.y * weight, sum.z + q.z * weight, sum.w + q.w * weight);
        }
    }

    // PlayerLoop step right after the Animator (PreLateUpdate.DirectorUpdateAnimationEnd); Timeline script tracks run before it.
    static class TransformTweenLateApply
    {
        static readonly List<TransformTweenMixer> Pending = new List<TransformTweenMixer>();
        static readonly List<TransformTweenMixer> Preview = new List<TransformTweenMixer>();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallPreview()
        {
            RenderPipelineManager.beginContextRendering += (_, _) => ApplyPreview();
            Camera.onPreCull += _ => ApplyPreview();
        }
#endif

        internal static void HoldPreview(TransformTweenMixer mixer)
        {
            if (!Preview.Contains(mixer))
                Preview.Add(mixer);
        }

        internal static void ReleasePreview(TransformTweenMixer mixer) => Preview.Remove(mixer);

        static void ApplyPreview()
        {
            if (Application.isPlaying)
                return;
            foreach (var mixer in Preview)
                mixer.ApplyPreview();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            Pending.Clear();
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (Insert(ref root))
                PlayerLoop.SetPlayerLoop(root);
        }

        internal static void Enqueue(TransformTweenMixer mixer)
        {
            if (!Pending.Contains(mixer))
                Pending.Add(mixer);
        }

        static void Run()
        {
            bool sync = false;
            foreach (var mixer in Pending)
                sync |= mixer.LateApply();
            Pending.Clear();
            if (sync)
                Physics.SyncTransforms();
        }

        static bool Insert(ref PlayerLoopSystem root)
        {
            var systems = root.subSystemList;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].type != typeof(PreLateUpdate))
                    continue;

                var list = new List<PlayerLoopSystem>(systems[i].subSystemList);
                if (list.Exists(s => s.type == typeof(TransformTweenLateApply)))
                    return false;
                int anchor = list.FindIndex(s => s.type == typeof(PreLateUpdate.DirectorUpdateAnimationEnd));
                list.Insert(anchor + 1, new PlayerLoopSystem { type = typeof(TransformTweenLateApply), updateDelegate = Run });
                systems[i].subSystemList = list.ToArray();
                return true;
            }
            return false;
        }
    }
}
