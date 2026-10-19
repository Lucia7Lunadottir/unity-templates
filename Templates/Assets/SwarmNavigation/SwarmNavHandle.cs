using System.Runtime.InteropServices;

namespace PG.SwarmNavigation
{
    // Хэндл на агента для внешнего кода - индекс слота + generation, чтобы использование хэндла
    // после UnregisterAgent тихо считалось невалидным (SwarmNav.IsHandleValid), а не читало данные
    // чужого агента, занявшего тот же слот.
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SwarmNavHandle
    {
        public readonly int Slot;
        public readonly byte Generation;

        public SwarmNavHandle(int slot, byte generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public static readonly SwarmNavHandle Invalid = new SwarmNavHandle(-1, 0);
    }

    // Как NavMeshAgent.pathStatus: Pending - путь ещё считается, Partial - цель недостижима (агент дойдёт
    // до ближайшей достижимой точки), Invalid - агент или цель вне NavMesh.
    public enum SwarmPathStatus : byte { None = 0, Pending = 1, Complete = 2, Partial = 3, Invalid = 4 }
}
