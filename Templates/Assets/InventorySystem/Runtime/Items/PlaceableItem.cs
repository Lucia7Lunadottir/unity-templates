using UnityEngine;

namespace PG.InventoryManagement
{
    [CreateAssetMenu(menuName = "PG/Inventory/PlaceableItem")]
    public class PlaceableItem : Item
    {
        [Tooltip("Prefab with Rigidbody + Collider + PlaceableWorldObject that spawns in the world")]
        [field: SerializeField] public GameObject worldPrefab { get; private set; }
    }
}
