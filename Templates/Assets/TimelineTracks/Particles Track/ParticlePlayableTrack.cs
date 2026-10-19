using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG
{
    [TrackColor(0.1f, 0.8f, 0.4f)]
    [TrackClipType(typeof(ParticleClip))]
    [TrackBindingType(typeof(ParticleSystem))]
    public class ParticlePlayableTrack : TrackAsset
#if UNITY_EDITOR
        , IPropertyPreview
#endif
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            // Создаём миксер с нужным количеством входов — как SubtitlesTrack
            return ScriptPlayable<ParticleTrackMixer>.Create(graph, inputCount);
        }

#if UNITY_EDITOR
        public void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director == null) return;

            var boundObject = director.GetGenericBinding(this);

            ParticleSystem ps = null;
            if (boundObject is ParticleSystem boundPs)
                ps = boundPs;
            else if (boundObject is GameObject go)
                ps = go.GetComponent<ParticleSystem>();

            if (ps == null) return;

            foreach (var child in ps.GetComponentsInChildren<ParticleSystem>(true))
            {
                driver.AddFromName<ParticleSystem>(child.gameObject, "randomSeed");
                driver.AddFromName<ParticleSystem>(child.gameObject, "autoRandomSeed");
            }
        }
#endif
    }
}
