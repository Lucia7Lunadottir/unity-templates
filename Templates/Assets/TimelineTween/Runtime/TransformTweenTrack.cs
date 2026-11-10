using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
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
                driver.AddFromName<Transform>(target.gameObject, "m_LocalPosition.x");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalPosition.y");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalPosition.z");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalRotation.x");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalRotation.y");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalRotation.z");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalRotation.w");
            }
            base.GatherProperties(director, driver);
        }
    }

    public class TransformTweenMixer : PlayableBehaviour
    {
        Transform _target;
        Vector3 _initialPosition;
        Quaternion _initialRotation;

        // Re-snapshot on every graph start so a moved object is not snapped back to an old pose.
        public override void OnGraphStart(Playable playable) => _target = null;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as Transform;
            if (target == null)
                return;

            if (_target != target)
            {
                _target = target;
                _initialPosition = target.position;
                _initialRotation = target.rotation;
            }

            Transform parent = target.parent;
            Vector3 position = Vector3.zero;
            Quaternion rotation = new Quaternion(0f, 0f, 0f, 0f);
            float positionWeight = 0f;
            float rotationWeight = 0f;

            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f)
                    continue;

                var input = (ScriptPlayable<TransformTweenBehaviour>)playable.GetInput(i);
                var behaviour = input.GetBehaviour();
                var clip = behaviour.clip;
                if (clip == null)
                    continue;

                float progress = clip.Evaluate(input.GetTime(), behaviour.clipIn, behaviour.clipDuration);
                clip.GetPose(progress, parent, out var clipPosition, out var clipRotation);

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
            }

            target.position = position + _initialPosition * Mathf.Max(0f, 1f - positionWeight);

            if (rotationWeight < 1f)
                rotation = Accumulate(rotation, _initialRotation, 1f - rotationWeight);
            target.rotation = Quaternion.Normalize(rotation);
        }

        // Weighted quaternion sum with hemisphere fix; normalized once at the end.
        static Quaternion Accumulate(Quaternion sum, Quaternion q, float weight)
        {
            if (Quaternion.Dot(sum, q) < 0f)
                weight = -weight;
            return new Quaternion(sum.x + q.x * weight, sum.y + q.y * weight, sum.z + q.z * weight, sum.w + q.w * weight);
        }
    }
}
