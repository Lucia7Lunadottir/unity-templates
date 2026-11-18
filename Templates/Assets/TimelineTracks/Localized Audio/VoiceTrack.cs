using System.ComponentModel;
using UnityEngine;
using UnityEngine.Timeline;

namespace PG.TimelineAudio
{
    // Голосовая дорожка: обычный Audio Track (клипы, нарезка, привязка к AudioSource - всё как раньше) + язык записи.
    // В катсцене звучат только дорожки языка игры - см. TimelineVoiceLanguage.
    [DisplayName("Voice Track")]
    public class VoiceTrack : AudioTrack
    {
        [Tooltip("Пусто - запасной язык из Voice Settings")]
        [SerializeField, VoiceLanguage] private string _language;

        public string language => string.IsNullOrEmpty(_language) && VoiceSettings.Instance != null ? VoiceSettings.Instance.fallbackLanguage : _language;
    }
}
