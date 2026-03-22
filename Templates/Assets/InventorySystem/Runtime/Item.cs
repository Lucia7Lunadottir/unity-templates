using UnityEngine;

namespace PG.InventorySystem
{
    [CreateAssetMenu(menuName = "PG/Inventory/Item")]
    public class Item : ScriptableObject
    {
        public string nameItem;
        public string descriptionItem;
        public Sprite iconItem;
        public Sprite largeIconItem;
        public bool isStackable;
        public bool isDropAfterInteract;
        public virtual void Use(Inventory inventory, int slot)
        {
#if UNITY_EDITOR
            Debug.Log($"{nameItem} used!");
#endif
            // Override in subclasses for custom behavior: heal, add to inventory, etc.
        }

    }
}
