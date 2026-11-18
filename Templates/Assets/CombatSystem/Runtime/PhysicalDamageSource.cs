using UnityEngine;

// Метка: урон от этого объекта и всех его детей - физический (копьё, удары), а не магия.
// Кто различает урон (например, BarrierAI с «только физический»), ищет её у источника урона и его родителей.
public class PhysicalDamageSource : MonoBehaviour
{
    public static bool IsPhysical(GameObject source) => source != null && source.GetComponentInParent<PhysicalDamageSource>(true) != null;
}
