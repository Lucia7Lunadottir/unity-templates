using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG.TimelineTween
{
    public enum TweenMode { Tween, Place }

    public enum TweenSpace { World, Local }

    public enum TweenTiming { Clip, Fixed }

    public enum RootMotionMode { Keep, Off, On }

    [Serializable]
    [DisplayName("Transform Tween Clip")]
    public class TransformTweenClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Tween: moves from Start to End. Place: holds the object at Start for the whole clip.")]
        public TweenMode mode = TweenMode.Tween;
        [Tooltip("World: stored poses are world coordinates. Local: relative to the parent of the bound object. Switching does not convert the values.")]
        public TweenSpace space = TweenSpace.World;
        [Tooltip("Clip: the tween stretches over the clip length. Fixed: the tween lasts Tween Duration, so trimming or splitting the clip keeps its place on the curve.")]
        public TweenTiming timing = TweenTiming.Clip;
        [Min(0.01f)] public float tweenDuration = 1f;
        [Tooltip("Animator.applyRootMotion while the clip plays; the previous value is restored when no clip is active.")]
        public RootMotionMode rootMotion = RootMotionMode.Off;

        public bool tweenPosition = true;
        public bool tweenRotation = true;

        public Vector3 startPosition;
        public Vector3 startRotation;
        public Vector3 endPosition;
        public Vector3 endRotation;

        [Tooltip("Values outside [0,1] overshoot the end points.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        bool IsFixed => mode == TweenMode.Tween && timing == TweenTiming.Fixed;

        public ClipCaps clipCaps => IsFixed
            ? ClipCaps.Blending | ClipCaps.Extrapolation | ClipCaps.ClipIn
            : ClipCaps.Blending | ClipCaps.Extrapolation;

        public override double duration => IsFixed ? tweenDuration : base.duration;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<TransformTweenBehaviour>.Create(graph);
            playable.GetBehaviour().clip = this;
            return playable;
        }

        // localTime includes clipIn, so a split/trimmed Fixed clip continues from the same point on the curve.
        public float Evaluate(double localTime, double clipIn, double clipDuration)
        {
            if (mode == TweenMode.Place)
                return 0f;
            double t = IsFixed
                ? localTime / tweenDuration
                : clipDuration > 0 ? (localTime - clipIn) / clipDuration : 1d;
            return curve != null ? curve.Evaluate(Mathf.Clamp01((float)t)) : Mathf.Clamp01((float)t);
        }

        public void GetPose(float progress, Transform parent, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.LerpUnclamped(startPosition, endPosition, progress);
            rotation = Quaternion.SlerpUnclamped(Quaternion.Euler(startRotation), Quaternion.Euler(endRotation), progress);
            ToWorld(parent, ref position, ref rotation);
        }

        public void GetPoint(bool start, Transform parent, out Vector3 position, out Quaternion rotation)
        {
            position = start ? startPosition : endPosition;
            rotation = Quaternion.Euler(start ? startRotation : endRotation);
            ToWorld(parent, ref position, ref rotation);
        }

        public void SetPoint(bool start, Transform parent, Vector3 position, Quaternion rotation)
        {
            if (space == TweenSpace.Local && parent != null)
            {
                position = parent.InverseTransformPoint(position);
                rotation = Quaternion.Inverse(parent.rotation) * rotation;
            }

            if (start)
            {
                startPosition = position;
                startRotation = rotation.eulerAngles;
            }
            else
            {
                endPosition = position;
                endRotation = rotation.eulerAngles;
            }
        }

        void ToWorld(Transform parent, ref Vector3 position, ref Quaternion rotation)
        {
            if (space != TweenSpace.Local || parent == null)
                return;
            position = parent.TransformPoint(position);
            rotation = parent.rotation * rotation;
        }
    }

    public class TransformTweenBehaviour : PlayableBehaviour
    {
        public TransformTweenClip clip;
        public double clipIn;
        public double clipDuration;
    }
}
