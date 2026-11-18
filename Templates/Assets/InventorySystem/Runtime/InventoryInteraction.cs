using PG.MenuManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PG.InventoryManagement
{
    public class InventoryInteraction : MonoBehaviour
    {
        [Header("Main References")]
        [SerializeField] private Inventory _inventory;
        [SerializeField] private QuickItemSlotInteraction _slotInteraction;
        [SerializeField] private ItemInfoPanel _itemInfoPanel;

        [Header("Input Actions")]
        [SerializeField] private InputActionProperty _dropProperty;
        [SerializeField] private InputActionProperty _useProperty;
        [SerializeField] private InputActionProperty _assignFirstItemProperty;
        [SerializeField] private InputActionProperty _assignSecondItemProperty;
        [SerializeField] private InputActionProperty _placeProperty;


        [Header("Placement")]
        [SerializeField] private Pause _pause;


        public int targetIndex;
        private Camera _camera;

        private void Awake()
        {
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            _dropProperty.action.performed += OnDrop;
            _useProperty.action.performed += OnUse;

            _assignFirstItemProperty.action.performed += OnAssignFirstItem;
            _assignSecondItemProperty.action.performed += OnAssignSecondItem;
            
            _placeProperty.action.performed += OnPlace;
        }

        private void OnDisable()
        {
            _dropProperty.action.performed -= OnDrop;
            _useProperty.action.performed -= OnUse;

            _assignFirstItemProperty.action.performed -= OnAssignFirstItem;
            _assignSecondItemProperty.action.performed -= OnAssignSecondItem;
            
            _placeProperty.action.performed -= OnPlace;
        }

        public void RenderInfo(int id = -1)
        {
            if (id == -1)
            {
                id = targetIndex;
            }
            if (targetIndex >= 0 && targetIndex < _inventory.items.Count)
            {
                Item currentItem = _inventory.items[targetIndex].item;
                if (_itemInfoPanel != null) _itemInfoPanel.Render(currentItem);
            }
        }

        public void OnDrop(InputAction.CallbackContext callbackContext) => Drop();
        public void OnUse(InputAction.CallbackContext callbackContext) => Use();
        public void OnAssignFirstItem(InputAction.CallbackContext callbackContext) => AssignFirstItem();
        public void OnAssignSecondItem(InputAction.CallbackContext callbackContext) => AssignSecondItem();
        public void OnPlace(InputAction.CallbackContext callbackContext) => Place();

        public void Use()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy)
            {
                return;
            }
            _inventory.UseItem(targetIndex);
        }
        public void Drop()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy) return;
            if (targetIndex < 0 || targetIndex >= _inventory.items.Count) return;

            Item item = _inventory.items[targetIndex].item;

            Vector3 dropPos = _camera.transform.position + _camera.transform.forward * 1.5f;
            DroppedItemManager.SpawnDrop(item, dropPos);

            _inventory.RemoveItem(targetIndex);
        }
        public void Remove()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy) return;
            if (targetIndex < 0 || targetIndex >= _inventory.items.Count) return;

            Item item = _inventory.items[targetIndex].item;

            _inventory.RemoveItem(targetIndex);
        }
        public void AssignFirstItem()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy)
            {
                return;
            }
            ToggleSlot(_inventory.items[targetIndex].item, false);
        }
        public void AssignSecondItem()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy)
            {
                return;
            }
            ToggleSlot(_inventory.items[targetIndex].item, true);
        }

        // Уже в этом быстром слоте - убрать оттуда, иначе назначить
        private void ToggleSlot(Item item, bool second)
        {
            var slot = second ? _slotInteraction.secondItemSlot : _slotInteraction.firstItemSlot;
            _slotInteraction.SetItem(slot.assignedItem == item ? null : item, second);
        }

        // Положить предмет в мир: меню закрывается, открывается Build Mode на этом предмете (призрак, поворот, RT - положить)
        public void Place()
        {
            if (!_inventory.cellContainer.gameObject.activeInHierarchy) return;
            if (targetIndex < 0 || targetIndex >= _inventory.items.Count) return;
            Item item = _inventory.items[targetIndex].item;
            if (!InventoryBridge.CanPlace(item)) return;

            if (_pause != null && UIManager.RegisterClose(_pause.panel.gameObject)) _pause.ClosePause();
            InventoryBridge.RequestPlace(item);
        }
    }
}