using UnityEngine;

namespace PG.BuffSystem
{
    [CreateAssetMenu(menuName = "Buff System/Buff")]
    public class BuffData : ScriptableObject
    {
        [field:SerializeField] public Sprite icon { get; private set; }
        [field:SerializeField] public string buffName { get; private set; }
    }
}