#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PonyPhysics
{
    [CustomEditor(typeof(SpringBoneManager))]
    public class SpringBoneManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SpringBoneManager mgr = (SpringBoneManager)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(!Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Re-initialise")) mgr.Initialise();
                    if (GUILayout.Button("Reset Pose"))    mgr.ResetPose();
                }
            }

            EditorGUILayout.Space(4);

            var chains = mgr.GetComponentsInChildren<SpringBoneChain>(false);
            if (chains.Length > 0)
            {
                EditorGUILayout.LabelField($"Chains: {chains.Length}", EditorStyles.miniBoldLabel);
                foreach (var c in chains)
                {
                    int n = c.bones != null ? c.bones.Length : 0;
                    EditorGUILayout.LabelField($"  • {c.name}  ({n} bones)", EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No SpringBoneChain found.\nAdd SpringBoneChain to each chain root (tail, mane).",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Animation Rigging setup:\n" +
                "1. RigBuilder on pony root\n" +
                "2. Rig component on child GameObject (e.g. \"SpringRig\")\n" +
                "3. SpringBoneManager on that same GameObject\n" +
                "4. Add the Rig to RigBuilder's rig list",
                MessageType.Info);
        }
    }

    [CustomEditor(typeof(SpringBoneChain))]
    public class SpringBoneChainEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            SpringBoneChain chain = (SpringBoneChain)target;
            EditorGUILayout.Space(4);
            int count = CountChain(chain.transform, 0, chain.chainDepth, chain.excludeBones);
            EditorGUILayout.LabelField($"Chain preview: {count} bone(s)", EditorStyles.miniLabel);
            if (Application.isPlaying && chain.bones != null)
                EditorGUILayout.LabelField($"Simulating: {chain.bones.Length} bone(s)", EditorStyles.miniLabel);
        }

        static int CountChain(Transform node, int depth, int maxDepth,
            System.Collections.Generic.List<Transform> excl)
        {
            if (maxDepth >= 0 && depth > maxDepth) return 0;
            if (depth > 0 && excl.Contains(node)) return 0;
            int n = 1;
            for (int i = 0; i < node.childCount; i++)
            {
                var c = node.GetChild(i);
                if (!excl.Contains(c)) { n += CountChain(c, depth+1, maxDepth, excl); break; }
            }
            return n;
        }
    }
}
#endif
