using PG.Localization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PG.TimelineAudio
{
    public class LocalizedAudioPlayableAsset : AudioPlayableAsset
    {
        public bool useLocalization = true;
        public List<LanguageClip> clips;

        [System.Serializable]
        public struct LanguageClip
        {
            public string language;
            public AudioClip clip;
        }

        public AudioClip fallbackClip;
        [Min(0)] public int indexEditor;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            // ВАЖНО: тут мы сами выбираем нужный AudioClip
            clip = GetClipForCurrentLanguage();
            return base.CreatePlayable(graph, owner);
        }

        private AudioClip GetClipForCurrentLanguage()
        {
            string lang = "";
            if (Application.isPlaying && useLocalization)
            {
                lang = LocalizationSystem.instance.currentLanguage;
            }
            else
            {
                if (clips.Count > 0)
                {
                    if (indexEditor >= clips.Count)
                    {
                        indexEditor = clips.Count - 1;
                    }
                    else if (indexEditor < 0)
                    {
                        indexEditor = 0;
                    }
                    lang = clips[indexEditor].language;
                }
            }
            foreach (var lc in clips)
            {
                if (lc.clip != null && lc.language == lang)
                    return lc.clip;
            }

            return fallbackClip;
        }
    }
}
