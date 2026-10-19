using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace PG
{
    [Overlay(typeof(SceneView), k_OverlayId, k_DisplayName)]
    public class ParticleSystemControlSceneOverlay : IMGUIOverlay, ITransientOverlay
    {
        private const string k_OverlayId = "Scene View/Particle System Timeline Control";
        private const string k_DisplayName = "Particle System Timeline Control";

        public bool visible
        {
            get
            {
                // Overlay is automatically drawn if the opened Timeline graph contains any active Particle tracks
                var director = TimelineEditor.inspectedDirector;
                if (director != null && director.playableAsset is TimelineAsset timeline)
                {
                    return timeline.GetOutputTracks().Any(t => t is ParticleSystemControlTrack);
                }
                return false;
            }
        }

        public override void OnGUI()
        {
            var director = TimelineEditor.inspectedDirector;
            if (director == null) return;

            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) return;

            EditorGUILayout.LabelField("Active Control Tracks:", EditorStyles.boldLabel);

            var particleTracks = timeline.GetOutputTracks().OfType<ParticleSystemControlTrack>();
            foreach (var track in particleTracks)
            {
                var binding = director.GetGenericBinding(track) as ParticleSystem;
                
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField("Target System:", binding, typeof(ParticleSystem), true);
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}