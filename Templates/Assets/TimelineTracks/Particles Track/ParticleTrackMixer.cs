using UnityEngine;
using UnityEngine.Playables;

namespace PG
{
    public class ParticleTrackMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var ps = playerData as ParticleSystem;
            if (ps == null) return;

            int inputCount = playable.GetInputCount();
            bool isAnyClipActive = false;
            float localClipTime = 0f;

            // Find if any clip is currently active and get its local time
            for (int i = 0; i < inputCount; i++)
            {
                if (playable.GetInputWeight(i) > 0f)
                {
                    isAnyClipActive = true;
                    localClipTime = (float)playable.GetInput(i).GetTime();
                    break;
                }
            }

            // ==========================================
            // PLAY MODE (Runtime Gameplay)
            // ==========================================
            if (Application.isPlaying)
            {
                var emission = ps.emission;

                if (isAnyClipActive)
                {
                    // Enable emission when inside the clip boundaries
                    if (!emission.enabled)
                    {
                        emission.enabled = true;
                    }

                    // Ensure the particle system is actually running natively in Unity's loop
                    if (!ps.isPlaying)
                    {
                        ps.Play(true);
                    }
                }
                else
                {
                    // Disable emission outside the clip so no new particles are born,
                    // but already existing particles can finish their lifetime naturally.
                    if (emission.enabled)
                    {
                        emission.enabled = false;
                    }
                }
                return;
            }

            // ==========================================
            // EDITOR MODE (Timeline Scrubbing)
            // ==========================================
            if (isAnyClipActive)
            {
                var emission = ps.emission;
                if (!emission.enabled) 
                {
                    emission.enabled = true;
                }
                
                // Continuous absolute simulation based on local clip time for accurate scrubbing
                ps.Simulate(localClipTime, true, true, false);
            }
            else
            {
                // Clear visual clutter in the editor scene view when outside clips
                ps.Clear(true);
            }
        }
    }
}