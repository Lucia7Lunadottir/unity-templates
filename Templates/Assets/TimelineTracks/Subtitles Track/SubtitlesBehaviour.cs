using UnityEngine;
using UnityEngine.Playables;

namespace PG.Localization.SubtitlesCutscene
{
    public class SubtitlesBehaviour : PlayableBehaviour
    {
        public Color color;
        [TextArea(3, 10)] public string subtitlesKey;
        [TextArea(3, 10)] public string defaultText;
    }
}
