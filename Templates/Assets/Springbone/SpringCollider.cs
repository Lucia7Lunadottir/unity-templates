// SpringCollider.cs
// Add to body parts (chest, neck, legs) to prevent hair/tail clipping.

using UnityEngine;

namespace PonyPhysics
{
    public enum ColliderType { Sphere, Capsule }

    [AddComponentMenu("Pony Physics/Spring Collider")]
    public class SpringCollider : MonoBehaviour
    {
        public ColliderType type      = ColliderType.Sphere;
        public float        radius    = 0.05f;
        [Tooltip("Capsule only: second endpoint in local space.")]
        public Vector3      tailOffset = new Vector3(0f, 0.1f, 0f);

        // Cached world values, updated each frame by SpringBoneManager
        [HideInInspector] public Vector3 worldCenter;
        [HideInInspector] public Vector3 worldTail;
        [HideInInspector] public float   worldRadius;

        public void RefreshWorldData()
        {
            Transform t = transform;
            worldCenter = t.position;
            worldTail   = t.TransformPoint(tailOffset);
            worldRadius = radius * Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
        }

        // Pushes point outside this collider. Returns true if collision occurred.
        public bool Resolve(ref Vector3 point, float boneRadius)
        {
            float combined = worldRadius + boneRadius;

            if (type == ColliderType.Sphere)
            {
                Vector3 delta = point - worldCenter;
                float   dist  = delta.magnitude;
                if (dist < combined)
                {
                    point = worldCenter + (dist < 1e-6f ? Vector3.up : delta / dist) * combined;
                    return true;
                }
            }
            else
            {
                Vector3 ab  = worldTail - worldCenter;
                float   len = ab.magnitude;
                if (len < 1e-5f)
                {
                    type = ColliderType.Sphere;
                    return Resolve(ref point, boneRadius);
                }
                Vector3 dir     = ab / len;
                float   t2      = Mathf.Clamp(Vector3.Dot(point - worldCenter, dir), 0f, len);
                Vector3 closest = worldCenter + dir * t2;
                Vector3 delta   = point - closest;
                float   dist    = delta.magnitude;
                if (dist < combined)
                {
                    point = closest + (dist < 1e-6f ? Vector3.up : delta / dist) * combined;
                    return true;
                }
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.75f);
            if (type == ColliderType.Sphere)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else
            {
                Vector3 a = transform.position;
                Vector3 b = transform.TransformPoint(tailOffset);
                Gizmos.DrawWireSphere(a, radius);
                Gizmos.DrawWireSphere(b, radius);
                Gizmos.DrawLine(a, b);
            }
        }
#endif
    }
}
