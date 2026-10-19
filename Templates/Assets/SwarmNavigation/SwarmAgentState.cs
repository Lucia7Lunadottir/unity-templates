using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace PG.SwarmNavigation
{
    // Near - каждый кадр; Mid/Far - раз в N кадров целиком по тиру (SwarmLodScheduler), не по таймеру
    // на агента. Объявлен здесь, а не в SwarmLodScheduler.cs, потому что SwarmAgentState нужен раньше.
    public enum SwarmLodTier : byte { Near = 0, Mid = 1, Far = 2 }

    // Чисто blittable публичное состояние агента - никакого GameObject/Transform. Его читают те, кто рисует
    // агентов сам (RenderMeshIndirect, VAT): Position лежит на поверхности NavMesh, Velocity - направление
    // последнего шага (длина 0..1, ноль - стоит). Раскладка пригодна для GraphicsBuffer (SwarmNav.AgentsGpu).
    [StructLayout(LayoutKind.Sequential)]
    public struct SwarmAgentState
    {
        public float3 Position;
        public float3 Velocity;
        public float Speed;
        public SwarmLodTier Tier;
        public byte Generation;       // увеличивается при Unregister - защита от использования протухшего SwarmNavHandle

        public static SwarmAgentState Create(float3 position, float speed)
        {
            return new SwarmAgentState
            {
                Position = position,
                Velocity = float3.zero,
                Speed = speed,
                Tier = SwarmLodTier.Near,
                Generation = 0
            };
        }
    }
}
