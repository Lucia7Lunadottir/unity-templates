using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace PG
{
    [CustomEditor(typeof(ParticleSystemControlTrack))]
    public class ParticleSystemControlClipInspector : Editor
    {
        private SerializedProperty m_ReinitProperty;

        private void OnEnable()
        {
            m_ReinitProperty = serializedObject.FindProperty("reinit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ReinitProperty, new GUIContent("Track Reinit Mode"));
            
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();

                // Force instantly rebuild the graph layout to synchronize changes
                var allDirectors = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude);
                foreach (var director in allDirectors)
                {
                    director.RebuildGraph();
                }
            }
        }
    }

    [CustomEditor(typeof(ParticleSystemControlClip))]
    public class ParticleSystemControlClipAssetInspector : Editor
    {
        private SerializedProperty m_ScrubbingProperty;
        private SerializedProperty m_StartSeedProperty;
        private SerializedProperty m_ReinitProperty;

        private void OnEnable()
        {
            m_ScrubbingProperty = serializedObject.FindProperty("scrubbing");
            m_StartSeedProperty = serializedObject.FindProperty("startSeed");
            m_ReinitProperty = serializedObject.FindProperty("reinit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            
            // Layout fields for scrubbing validation
            EditorGUILayout.PropertyField(m_ScrubbingProperty, new GUIContent("Enable Scrubbing", "Allows previewing simulation frame-by-frame via playhead drag."));

            var currentReinit = (ParticleSystemControlClip.ReinitMode)m_ReinitProperty.enumValueIndex;

            // Enforce proper state handling for scrubbing workflows
            if (m_ScrubbingProperty.boolValue)
            {
                currentReinit = ParticleSystemControlClip.ReinitMode.OnEnterOrExitClip;
            }

            using (new EditorGUI.DisabledScope(m_ScrubbingProperty.boolValue))
            {
                EditorGUI.BeginChangeCheck();
                var newReinit = (ParticleSystemControlClip.ReinitMode)EditorGUILayout.EnumPopup("Clip Reinit Mode", currentReinit);
                if (EditorGUI.EndChangeCheck())
                {
                    m_ReinitProperty.enumValueIndex = (int)newReinit;
                }
            }

            // Display seed configuration field only when initialization settings support it
            bool requiresSeed = currentReinit == ParticleSystemControlClip.ReinitMode.OnEnterOrExitClip || 
                                 currentReinit == ParticleSystemControlClip.ReinitMode.OnEnterClip;

            using (new EditorGUI.DisabledScope(!requiresSeed))
            {
                EditorGUILayout.PropertyField(m_StartSeedProperty, new GUIContent("Particle Random Seed", "Determinism seed override applied when entering the playback chunk boundary."));
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}