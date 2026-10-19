using UnityEngine;
using UnityEngine.Playables;

namespace PG.Localization.SubtitlesCutscene
{
    [System.Serializable]
    public class SubtitlesClip : PlayableAsset
    {
        public Color color = Color.white;
        [TextArea(3, 10)] public string subtitlesKey;
        [TextArea(3, 10)] public string defaultText;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SubtitlesBehaviour>.Create(graph);
            SubtitlesBehaviour subtitlesBehaviour = playable.GetBehaviour();

            subtitlesBehaviour.color = color;

            if (Application.isPlaying)
            {
                subtitlesBehaviour.subtitlesKey = subtitlesKey;
            }
            else
            {
                subtitlesBehaviour.defaultText = defaultText;
            }

            return playable;
        }
    }

}