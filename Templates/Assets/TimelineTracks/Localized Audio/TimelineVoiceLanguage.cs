using System.Collections.Generic;
using PG.Localization;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace PG.TimelineAudio
{
    // Озвучка на разных языках в одном таймлайне: голос лежит на Voice Track, у каждой дорожки свой язык,
    // каждый язык режется в Timeline как обычно. При запуске любой катсцены звучат только дорожки одного языка:
    // языка игры, если на нём есть запись, иначе VoiceSettings.fallbackLanguage.
    // Ставить ничего не нужно: все PlayableDirector сцен подхватываются сами; созданные позже - через Attach.
    // Ассет таймлайна не меняется: mute ставится только на время сборки графа и сразу возвращается.
    public static class TimelineVoiceLanguage
    {
        private static readonly HashSet<PlayableDirector> s_attached = new HashSet<PlayableDirector>();
        private static readonly List<VoiceTrack> s_tracks = new List<VoiceTrack>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            s_attached.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) OnSceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var director in root.GetComponentsInChildren<PlayableDirector>(true)) Attach(director);
        }

        public static void Attach(PlayableDirector director)
        {
            if (director == null || !s_attached.Add(director)) return;
            director.played += Apply;
            // Play On Awake мог сработать раньше подписки
            if (director.state == PlayState.Playing) Apply(director);
        }

        public static string GameLanguage => LocalizationSystem.instance != null ? LocalizationSystem.instance.currentLanguage : null;

        // Пересобрать граф так, чтобы звучал только один язык (если в таймлайне есть Voice Track)
        public static void Apply(PlayableDirector director)
        {
            if (director == null || director.playableAsset is not TimelineAsset timeline) return;
            CollectVoiceTracks(timeline, s_tracks);
            if (s_tracks.Count == 0) return;

            string language = ChooseLanguage(s_tracks);
            var restore = new List<(TrackAsset track, bool muted)>();
            foreach (var track in s_tracks)
            {
                bool mute = track.language != language;
                if (track.muted == mute) continue;
                restore.Add((track, track.muted));
                track.muted = mute;
            }
            if (restore.Count == 0) return;
            director.RebuildGraph();
            foreach (var (track, muted) in restore) track.muted = muted;
        }

        // Язык игры -> запасной из VoiceSettings -> первый, на котором в катсцене вообще есть запись
        public static string ChooseLanguage(List<VoiceTrack> tracks)
        {
            string game = GameLanguage;
            if (tracks.Exists(t => t.language == game)) return game;
            var settings = VoiceSettings.Instance;
            if (settings != null && tracks.Exists(t => t.language == settings.fallbackLanguage)) return settings.fallbackLanguage;
            return tracks[0].language;
        }

        public static void CollectVoiceTracks(TimelineAsset timeline, List<VoiceTrack> result)
        {
            result.Clear();
            foreach (var track in timeline.GetRootTracks()) Collect(track, result);
        }

        private static void Collect(TrackAsset track, List<VoiceTrack> result)
        {
            if (track is VoiceTrack voice) result.Add(voice);
            foreach (var child in track.GetChildTracks()) Collect(child, result);
        }
    }
}
