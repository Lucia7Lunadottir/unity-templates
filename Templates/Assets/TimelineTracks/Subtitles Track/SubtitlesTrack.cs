using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG.Localization.SubtitlesCutscene
{
    [TrackBindingType(typeof(TMP_Text))]
    [TrackClipType(typeof(SubtitlesClip))]
    public class SubtitlesTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<SubtitlesTrackMixer>.Create(graph, inputCount);
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director.GetGenericBinding(this) is TMP_Text text && text)
            {
                driver.AddFromName(text, "m_text");
                driver.AddFromName(text, "m_fontColor");
            }

            base.GatherProperties(director, driver);
        }
    }
}
