using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PG.InventoryManagement
{
    public class QuickItemSlotInteraction : MonoBehaviour, ISaveable
    {
        [SerializeField] private Inventory _inventory;

        // Поля теперь хранят ссылку на сам предмет, а не просто индекс
        [field: SerializeField] public ItemSlot firstItemSlot { get; private set; } = new ItemSlot();
        [field: SerializeField] public ItemSlot secondItemSlot { get; private set; } = new ItemSlot();

        [SerializeField] private InputActionProperty _firstInteractActionProperty;
        [SerializeField] private InputActionProperty _secondInteractActionProperty;

        [SerializeField] private bool _autoSave = true;
        [SerializeField] private string _filename = "QuickItems.json";
        private string _filePath => Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _filename);

        [System.Serializable]
        public class ItemSlot
        {
            public InventoryCell cell;
            public Item assignedItem; // Храним сам предмет, а не цифру
        }

        // Структура для сохранения (тут храним ID из базы данных)
        [System.Serializable]
        public class SaveQuickItemSlotInteraction
        {
            public int firstItemID = -1;
            public int secondItemID = -1;
        }

        private void Awake()
        {
            // Подписываемся на события инвентаря, чтобы обновлять UI, если предмет закончился
            _inventory.itemRemoved += OnInventoryItemRemoved;
        }

        private void Start()
        {
            Load();
            UpdateSlotUI(firstItemSlot);
            UpdateSlotUI(secondItemSlot);
        }

        private void OnDestroy()
        {
            // Не забываем отписываться
            if (_inventory != null)
                _inventory.itemRemoved -= OnInventoryItemRemoved;
        }

        private void OnEnable()
        {
            _inventory.itemUsed += RenderSlots;
            _inventory.itemDropped += RenderSlots;
            _inventory.itemAdded += RenderSlots;
            _inventory.itemRemoved += RenderSlots;

            _firstInteractActionProperty.action.performed += OnInteractFirstItem;
            _secondInteractActionProperty.action.performed += OnInteractSecondItem;
        }

        private void OnDisable()
        {
            _inventory.itemUsed -= RenderSlots;
            _inventory.itemDropped -= RenderSlots;
            _inventory.itemAdded -= RenderSlots;
            _inventory.itemRemoved -= RenderSlots;

            _firstInteractActionProperty.action.performed -= OnInteractFirstItem;
            _secondInteractActionProperty.action.performed -= OnInteractSecondItem;
        }

        // Если предмет удален/потрачен из инвентаря, обновляем иконку (показываем кол-во 0 или убираем)
        private void OnInventoryItemRemoved(Inventory.RuntimeItem runtimeItem)
        {
            if (firstItemSlot.assignedItem == runtimeItem.item) UpdateSlotUI(firstItemSlot);
            if (secondItemSlot.assignedItem == runtimeItem.item) UpdateSlotUI(secondItemSlot);
        }

        void OnInteractFirstItem(InputAction.CallbackContext context) => TryUseItem(firstItemSlot);
        void OnInteractSecondItem(InputAction.CallbackContext context) => TryUseItem(secondItemSlot);


        private void TryUseItem(ItemSlot slot)
        {
            if (slot.assignedItem == null) return;
            // Открыто меню (пауза, вещи): L3/R3 там назначают слот - не тратим предмет той же кнопкой
            if (PG.MenuManagement.UIManager.IsAnyPanelOpen) return;

            // Самое важное: Спрашиваем у инвентаря, где сейчас лежит этот предмет
            int realIndex = _inventory.GetItemID(slot.assignedItem);

            if (realIndex != -1)
            {
                // Если предмет есть, используем его по реальному индексу
                _inventory.UseItem(realIndex);
                UpdateSlotUI(slot);
            }
            else
            {
                Debug.Log("Предмет назначен в слот, но отсутствует в инвентаре");
                // Можно здесь очистить слот, если хочешь:
                slot.assignedItem = null;
                UpdateSlotUI(slot);
            }
        }

        public void RenderSlots(Inventory.RuntimeItem runtimeItem)
        {
            if (_inventory == null) return;

            if (runtimeItem.item == firstItemSlot.assignedItem)
            {
                UpdateSlotUI(firstItemSlot);
            }

            if (runtimeItem.item == secondItemSlot.assignedItem)
            {
                UpdateSlotUI(secondItemSlot);
            }
        }

        public void RenderSlots(Item item)
        {
            if (_inventory == null) return;

            if (item == firstItemSlot.assignedItem)
            {
                UpdateSlotUI(firstItemSlot);
            }

            if (item == secondItemSlot.assignedItem)
            {
                UpdateSlotUI(secondItemSlot);
            }
        }

        public void SetItem(Item item, bool secondSlot = false)
        {
            ItemSlot targetSlot = secondSlot ? secondItemSlot : firstItemSlot;
            targetSlot.assignedItem = item;

            UpdateSlotUI(targetSlot);
            if (_autoSave)
            {
                Save();
            }
        }

        private void UpdateSlotUI(ItemSlot slot)
        {
            if (slot.assignedItem == null)
            {
                slot.cell.Render(null);
                return;
            }

            // Находим актуальные данные предмета в инвентаре для отображения кол-ва
            int realIndex = _inventory.GetItemID(slot.assignedItem);

            if (realIndex != -1)
            {
                slot.cell.Render(_inventory.items[realIndex]);
            }
            else
            {
                // Предмет назначен, но его нет в инвентаре (0 шт.)
                // Создаем временную структуру просто чтобы показать иконку
                Inventory.RuntimeItem dummyInfo = new Inventory.RuntimeItem(slot.assignedItem, 0);
                slot.cell.Render(dummyInfo);
            }
        }

        public void Save()
        {
            SaveQuickItemSlotInteraction saveData = new SaveQuickItemSlotInteraction();

            // Сохраняем ID из базы данных (ItemContainer), так как он постоянный
            saveData.firstItemID = _inventory.itemContainer.GetItemID(firstItemSlot.assignedItem);
            saveData.secondItemID = _inventory.itemContainer.GetItemID(secondItemSlot.assignedItem);

            string json = JsonUtility.ToJson(saveData);
            File.WriteAllText(_filePath, json);
        }

        public void Load()
        {
            if (!File.Exists(_filePath)) return;

            try
            {
                string json = File.ReadAllText(_filePath);
                SaveQuickItemSlotInteraction saveData = JsonUtility.FromJson<SaveQuickItemSlotInteraction>(json);

                // Восстанавливаем ссылки на предметы по ID из ItemContainer
                if (saveData.firstItemID != -1 && saveData.firstItemID < _inventory.itemContainer.items.Length)
                    firstItemSlot.assignedItem = _inventory.itemContainer.items[saveData.firstItemID];

                if (saveData.secondItemID != -1 && saveData.secondItemID < _inventory.itemContainer.items.Length)
                    secondItemSlot.assignedItem = _inventory.itemContainer.items[saveData.secondItemID];
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Ошибка загрузки QuickSlots: " + e.Message);
            }
        }
    }
}