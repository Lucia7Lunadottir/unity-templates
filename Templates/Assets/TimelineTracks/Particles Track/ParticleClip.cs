using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG
{
    [System.Serializable]
    public class ParticleClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<ParticleBehaviour>.Create(graph);
        }
    }
}
