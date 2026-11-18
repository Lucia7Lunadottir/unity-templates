using PG.InventoryManagement;
using UnityEngine;

/// <summary>
/// Attach to a world prefab spawned via PlaceableItem.
/// Registers with PlaceablePickupManager (NOT TriggerInteractManager), so tap-E still works
/// on other interactables (Fireplace etc.) on the same object without any conflict.
/// Hold-E near this object picks it back into inventory.
/// Requires: Rigidbody, Collider (physics), trigger Collider (for hold-E range detection).
/// </summary>
public class PlaceableWorldObject : MonoBehaviour
{
    [HideInInspector] public PlaceableItem sourceItem;

    [SerializeField] private string _pickupAudioName;

    private const string PLAYER_TAG = "Player";

    public void Pickup()
    {
        if (sourceItem == null) return;

        var inventory = FindAnyObjectByType<PG.InventoryManagement.Inventory>(FindObjectsInactive.Include);
        if (inventory == null) return;

        if (inventory.AddItem(sourceItem))
        {
            DroppedItemManager.OnItemPickedUp(gameObject);
            AudioManager.instance?.Play(_pickupAudioName);
            Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        PlaceablePickupManager.instance?.Unregister(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(PLAYER_TAG))
            PlaceablePickupManager.instance?.Register(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(PLAYER_TAG))
            PlaceablePickupManager.instance?.Unregister(this);
    }
}
