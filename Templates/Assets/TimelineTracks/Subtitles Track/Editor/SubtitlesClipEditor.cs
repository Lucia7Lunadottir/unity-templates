using UnityEngine.Timeline;
using UnityEditor.Timeline;

namespace PG.Localization.SubtitlesCutscene
{
    // --- МАГИЯ НАЧИНАЕТСЯ ЗДЕСЬ ---
    // Этот код работает только в редакторе Unity и заставляет имя обновляться
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