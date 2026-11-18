using UnityEngine;

namespace PG.BuffSystem
{
    public class BuffLinker : MonoBehaviour
    {
        [field:SerializeField] public BuffManager buffManager { get; private set; }
    }
}