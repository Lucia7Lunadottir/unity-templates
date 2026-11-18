using UnityEngine;
using System.Collections.Generic;

namespace PG.InventoryManagement
{
    [CreateAssetMenu(menuName = "PG/Inventory/ItemContainer")]
    public class ItemContainer : ScriptableObject
    {
        [field: SerializeField] public Item[] items { get; private set; }


        public bool CheckItem(Item targetItem)
        {
            foreach (Item item in items)
            {
                if (item == targetItem)
                {
                    return true;
                }
            }
            return false;
        }
        public int GetItemID(Item item)
        {

            if (item != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == item)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
    }
}