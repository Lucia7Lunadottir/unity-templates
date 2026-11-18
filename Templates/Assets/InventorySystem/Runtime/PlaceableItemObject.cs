using PG.InventoryManagement;
using UnityEngine;

public class PlaceableItemObject : MonoBehaviour
{
    [SerializeField] private Item _item;
    public Item item => _item;
}
