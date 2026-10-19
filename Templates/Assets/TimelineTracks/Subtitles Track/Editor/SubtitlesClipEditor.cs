using UnityEngine.Timeline;
using UnityEditor.Timeline;

namespace PG.Localization.SubtitlesCutscene
{
    // --- ћј√»я Ќј„»Ќј≈“—я «ƒ≈—№ ---
    // Ётот код работает только в редакторе Unity и заставл€ет им€ обновл€тьс€
    [CustomTimelineEditor(typeof(SubtitlesClip))]
    public class SubtitlesClipEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            var textPlayableasset = clip.asset as SubtitlesClip;
            if (textPlayableasset != null && !string.IsNullOrEmpty(textPlayableasset.defaultText))
                clip.displayName = textPlayableasset.defaultText;
        }
    }
}