using TMPro;
using UnityEngine;
using UnityEngine.Playables;

namespace PG.Localization.SubtitlesCutscene
{
    public class SubtitlesTrackMixer : PlayableBehaviour
    {
        private TMP_Text boundText;
        private PlayableDirector director;

        public override void OnPlayableCreate(Playable playable)
        {
            if (Application.isPlaying && playable.GetGraph().GetResolver() is PlayableDirector playableDirector)
            {
                director = playableDirector;
                director.stopped += OnDirectorStopped;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var textBinding = playerData as TMP_Text;
            if (boundText != textBinding)
            {
                ClearRuntimeText();
                boundText = textBinding;
            }

            string text = "";
            float currentAlpha = 0f;
            Color color = Color.white;

            if (!textBinding)
            {
                return;
            }

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight > 0)
                {
                    ScriptPlayable<SubtitlesBehaviour> scriptPlayable = (ScriptPlayable<SubtitlesBehaviour>)playable.GetInput(i);
                    SubtitlesBehaviour subtitlesBehaviour = scriptPlayable.GetBehaviour();
                    if (Application.isPlaying)
                    {
                        text = LocalizationSystem.instance.GetLocalizedValue(subtitlesBehaviour.subtitlesKey);
                    }
                    else
                    {
                        text = subtitlesBehaviour.defaultText;
                    }
                    color = subtitlesBehaviour.color;
                    currentAlpha = inputWeight;
                }
            }

            textBinding.text = text;
            textBinding.color = new Color(color.r, color.g, color.b, currentAlpha);
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (director)
            {
                director.stopped -= OnDirectorStopped;
            }

            ReleaseText();
        }

        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            ReleaseText();
        }

        private void ReleaseText()
        {
            ClearRuntimeText();
            boundText = null;
        }

        private void ClearRuntimeText()
        {
            if (Application.isPlaying && boundText)
            {
                boundText.text = string.Empty;
                boundText.alpha = 0f;
            }
        }
    }
}
