using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG
{
    [Serializable]
    public class ParticleSystemControlClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public double clipStart { get; set; }
        public double clipEnd { get; set; }

        [NotKeyable] public bool scrubbing = true;
        [NotKeyable] public uint startSeed;

        public enum ReinitMode
        {
            None,
            OnExitClip,
            OnEnterClip,
            OnEnterOrExitClip
        }
        [NotKeyable] public ReinitMode reinit = ReinitMode.OnEnterOrExitClip;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ParticleSystemControlPlayableBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.clipStart = clipStart;
            behaviour.clipEnd = clipEnd;
            behaviour.scrubbing = scrubbing;
            behaviour.startSeed = startSeed;

            if (scrubbing)
            {
                behaviour.reinitEnter = true;
                behaviour.reinitExit = true;
            }
            else
            {
                switch (reinit)
                {
                    case ReinitMode.None:
                        behaviour.reinitEnter = false;
                        behaviour.reinitExit = false;
                        break;
                    case ReinitMode.OnExitClip:
                        behaviour.reinitEnter = false;
                        behaviour.reinitExit = true;
                        break;
                    case ReinitMode.OnEnterClip:
                        behaviour.reinitEnter = true;
                        behaviour.reinitExit = false;
                        break;
                    case ReinitMode.OnEnterOrExitClip:
                        behaviour.reinitEnter = true;
                        behaviour.reinitExit = true;
                        break;
                }
            }

            return playable;
        }
    }
}