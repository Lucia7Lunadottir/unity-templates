using System.Runtime.InteropServices;
using UnityEngine;

namespace PG.Rendering
{
    // Layout must match InstanceData in IndirectCube.shader / IndirectCulling.compute byte-for-byte.
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData
    {
        public Vector3 Position;
        public float Scale;
        public Quaternion Rotation;

        public const int Stride = sizeof(float) * 8; // 3 position + 1 scale + 4 rotation
    }
}
