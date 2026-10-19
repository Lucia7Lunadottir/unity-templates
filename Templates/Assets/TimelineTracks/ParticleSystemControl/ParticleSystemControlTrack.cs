using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG
{
    [TrackColor(0.1f, 0.8f, 0.4f)]
    [TrackClipType(typeof(ParticleSystemControlClip))]
    [TrackBindingType(typeof(ParticleSystem))]
    public class ParticleSystemControlTrack : TrackAsset
    {
        public enum ReinitMode
        {
            None,
            OnBindingEnable,
            OnBindingDisable,
            OnBindingEnableOrDisable
        }

        [SerializeField, NotKeyable]
        public ReinitMode reinit = ReinitMode.OnBindingEnableOrDisable;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            foreach (var clip in GetClips())
            {
                if (clip.asset is ParticleSystemControlClip customClip)
                {
                    customClip.clipStart = clip.start;
                    customClip.clipEnd = clip.end;
                }
            }

            var mixer = ScriptPlayable<ParticleSystemControlTrackMixerBehaviour>.Create(graph, inputCount);
            var behaviour = mixer.GetBehaviour();
            var reinitBinding = reinit == ReinitMode.OnBindingEnable || reinit == ReinitMode.OnBindingEnableOrDisable;
            var reinitUnbinding = reinit == ReinitMode.OnBindingDisable || reinit == ReinitMode.OnBindingEnableOrDisable;
            behaviour.Init(this, reinitBinding, reinitUnbinding);
            return mixer;
        }
    }
}