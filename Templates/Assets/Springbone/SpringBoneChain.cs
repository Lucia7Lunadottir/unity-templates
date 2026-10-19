// SpringBoneChain.cs
// Add ONE of these to the ROOT bone of each chain (tail, mane, etc.).
// No components needed on child bones — the chain walks them automatically.

using System.Collections.Generic;
using UnityEngine;

namespace PonyPhysics
{
    [AddComponentMenu("Pony Physics/Spring Bone Chain")]
    public class SpringBoneChain : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Chain Setup")]
        [Tooltip("How many levels deep to walk. -1 = full depth.")]
        public int chainDepth = -1;

        [Tooltip("Transforms to skip (IK targets, twist bones, etc.).")]
        public List<Transform> excludeBones = new List<Transform>();

        [Header("Base Physics")]
        [Range(0f, 1f)] public float stiffness      = 0.15f;
        [Range(0f, 1f)] public float damping        = 0.08f;
        [Range(0f, 1f)] public float elasticity     = 0.10f;
        public float gravityPower                   = 0.02f;
        public Vector3 gravityDir                   = Vector3.down;
        [Range(0f, 180f)] public float angleLimitDeg = 60f;
        public float radius                         = 0.02f;

        [Header("Per-Bone Curves  (X: 0=root  1=tip,  Y: multiplier)")]
        public AnimationCurve stiffnessCurve = AnimationCurve.Linear(0f, 1f,   1f, 0.4f);
        public AnimationCurve dampingCurve   = AnimationCurve.Linear(0f, 0.6f, 1f, 1.2f);
        public AnimationCurve gravityCurve   = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
        public AnimationCurve radiusCurve    = AnimationCurve.Linear(0f, 1.2f, 1f, 0.6f);

        // ── Runtime state (written by SpringBoneManager) ──────────────────────
        [HideInInspector] public BoneState[] bones;
        [HideInInspector] public List<float> t01Cache = new List<float>();

        // Chain-root scale at bake time. boneLength/radius are cached in world units at
        // that scale, so live values are corrected by (current scale / bakeScale) each
        // frame — otherwise resizing the rig at runtime desyncs them from SpringCollider,
        // which recomputes its own radius from live lossyScale every frame.
        [HideInInspector] public float bakeScale = 1f;

        // ── Called by SpringBoneManager.Initialise() ──────────────────────────
        public void BuildChain()
        {
            t01Cache.Clear();
            bakeScale = Mathf.Max(0.0001f, Mathf.Max(transform.lossyScale.x,
                Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));

            var list = new List<Transform>();
            CollectBones(transform, 0, list);

            bones = new BoneState[list.Count];

            for (int i = 0; i < list.Count; i++)
            {
                Transform t     = list[i];
                Transform child = (i + 1 < list.Count) ? list[i + 1] : FindFirstValidChild(t);

                float boneLen = (child != null)
                    ? Vector3.Distance(t.position, child.position)
                    : EstimateTipLength(t);

                Vector3 localDir = (child != null)
                    ? t.InverseTransformDirection((child.position - t.position).normalized)
                    : Vector3.forward;

                Vector3 worldTip = t.position + t.TransformDirection(localDir) * boneLen;

                float t01 = (list.Count > 1) ? (float)i / (list.Count - 1) : 0f;
                t01Cache.Add(t01);

                bones[i] = new BoneState
                {
                    transform    = t,
                    child        = child,
                    restLocalDir = localDir,
                    boneLength   = boneLen,
                    currentTip   = worldTip,
                    prevTip      = worldTip,
                    initialised  = true,
                };
            }
        }

        // Returns curve-multiplied physics values for bone index i
        public void GetCurvedValues(int i,
            out float s, out float d, out float e,
            out float gp, out float alRad, out float r)
        {
            float t01 = t01Cache[i];
            s     = stiffness    * stiffnessCurve.Evaluate(t01);
            d     = damping      * dampingCurve.Evaluate(t01);
            e     = elasticity;
            gp    = gravityPower * gravityCurve.Evaluate(t01);
            alRad = angleLimitDeg * Mathf.Deg2Rad;
            r     = radius       * radiusCurve.Evaluate(t01);
        }

        // ── Hierarchy helpers ─────────────────────────────────────────────────
        private void CollectBones(Transform node, int depth, List<Transform> result)
        {
            if (chainDepth >= 0 && depth > chainDepth) return;
            if (depth > 0 && excludeBones.Contains(node)) return;

            result.Add(node);

            Transform next = FindFirstValidChild(node);
            if (next != null)
                CollectBones(next, depth + 1, result);
        }

        private Transform FindFirstValidChild(Transform node)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform c = node.GetChild(i);
                if (!excludeBones.Contains(c)) return c;
            }
            return null;
        }

        private static float EstimateTipLength(Transform t)
        {
            if (t.parent != null)
                return Mathf.Max(0.01f, Vector3.Distance(t.position, t.parent.position));
            return 0.05f;
        }

        // ── Gizmos ────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Edit-mode preview
            if (bones == null || bones.Length == 0)
            {
                var preview = new List<Transform>();
                CollectBones(transform, 0, preview);
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.7f);
                for (int i = 0; i + 1 < preview.Count; i++)
                    Gizmos.DrawLine(preview[i].position, preview[i + 1].position);
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
                foreach (var b in preview)
                    Gizmos.DrawWireSphere(b.position, radius * 0.5f);
                return;
            }

            // Play-mode: simulated tips
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i].initialised) continue;
                float t01 = t01Cache[i];
                Gizmos.color = Color.Lerp(new Color(0.2f, 1f, 0.4f), new Color(1f, 0.8f, 0.1f), t01);
                Gizmos.DrawLine(bones[i].transform.position, bones[i].currentTip);
                Gizmos.DrawWireSphere(bones[i].currentTip, radius * radiusCurve.Evaluate(t01));
            }
        }
#endif
    }
}
