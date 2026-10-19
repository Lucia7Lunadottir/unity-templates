// BoneState.cs
// Runtime state for one bone in a SpringBoneChain.
// Plain C# class — no MonoBehaviour, no component on individual bones needed.

using UnityEngine;

namespace PonyPhysics
{
    public struct BoneState
    {
        public Transform transform;
        public TransformHandle transformHandle;
        public Transform child;       // next bone toward tip (null = last bone)
        public TransformHandle childHandle;
        
        public Vector3 restLocalDir;  // rest-pose direction toward child, in local space
        public float   boneLength;    // distance to child in rest pose

        public Vector3 currentTip;    // world-space simulated tip this frame
        public Vector3 prevTip;       // world-space simulated tip last frame (Verlet)

        public bool initialised;

        public void InitBones()
        {
            if (transform != null && transform.transformHandle.IsValid())
            {
                transformHandle = transform.transformHandle;
            }
            if (child != null && child.transformHandle.IsValid())
            {
                childHandle = child.transformHandle;
            }
        }
    }
}
