using UnityEngine;

namespace PG.InventorySystem
{
    public class Chest : MonoBehaviour
    {
        private const string _PLAYER_TAG = "Player";
        [Header("Inventories")]
        public Inventory chestInventory;    // Chest inventory
        public Inventory playerInventory;   // Player inventory

        [Header("UI")]
        public GameObject chestPanel;       // UI panel of the chest (nested panel)
        public GameObject playerPanel;      // UI panel of the player inventory

        private bool _isOpen = false;

        public void OpenChest()
        {
            if (_isOpen) return;
            _isOpen = true;
            chestPanel.SetActive(true);
            playerPanel.SetActive(true);

            // Can update UI, play open animation, etc.
#if UNITY_EDITOR
            Debug.Log("Chest is open!");
#endif
        }

        public void CloseChest()
        {
            if (!_isOpen) return;
            _isOpen = false;
            chestPanel.SetActive(false);
            playerPanel.SetActive(false);

            // Can play additional audio, etc.
#if UNITY_EDITOR
            Debug.Log("Chest is closed!");
#endif
        }

        // Open/close the chest via trigger/collision:
        private void OnTriggerEnter(Collider other)
        {
            // Relevant if the scene is 3D and the tag is "Player"
            if (other.CompareTag(_PLAYER_TAG))
            {
                OpenChest();
            }
        }
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(_PLAYER_TAG))
            {
                CloseChest();
            }
        }
    }
}
