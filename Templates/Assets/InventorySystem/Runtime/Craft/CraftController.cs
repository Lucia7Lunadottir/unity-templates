using System.Collections.Generic;
using UnityEngine;
using PG.InventoryManagement;
using UnityEngine.UI;
using System.IO;

namespace PG.CraftSystem
{
    public class CraftController : MonoBehaviour, ISaveable
    {
        [Header("Save")]
        [SerializeField] private string _fileName = "CraftRecipes.json";
        private string _filePath;

        [Header("Data")]
        [SerializeField] private Inventory _inventory;
        [SerializeField] private CraftRecipeContainer _recipeContainer;
        [SerializeField] private List<CraftRecipe> _recipes = new List<CraftRecipe>();

        [Header("UI")]
        [SerializeField] private Transform _recipesContainer; // ���� �������� ������
        [SerializeField] private CraftSlotUI _recipeUiPrefab; // ������ ������
        [SerializeField] private Selectable _craftMenuButton;
        public Selectable craftMenuButton => _craftMenuButton;

        private List<CraftSlotUI> _activeSlots = new List<CraftSlotUI>();

        // Для нового меню крафта (CraftMenuView): изученные рецепты и сигнал «пересчитать»
        public IReadOnlyList<CraftRecipe> knownRecipes => _recipes;
        public event System.Action changed;

        private SaveRecipes _saveRecipes = new SaveRecipes();

        [System.Serializable]
        public class SaveRecipes
        {
            public List<int> recipes = new List<int>();
        }

        private void Start()
        {
            _filePath = Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _fileName);
            // ������������� �� ��������� ���������, ����� ��������� ����������� ������
            _inventory.itemAdded += OnInventoryChanged;
            _inventory.itemRemoved += OnInventoryChanged;

            Load();

            CreateRecipeList();
            changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_inventory != null)
            {
                _inventory.itemAdded -= OnInventoryChanged;
                _inventory.itemRemoved -= OnInventoryChanged;
            }
        }

        [ContextMenu("Load")]
        public void Load()
        {
            if (!File.Exists(_filePath))
            {
                return;
            }
            string json = File.ReadAllText(_filePath);
            JsonUtility.FromJsonOverwrite(json, _saveRecipes);

            _recipes.Clear();
            for (int i = 0; i < _saveRecipes.recipes.Count; i++)
            {
                _recipes.Add(_recipeContainer.recipes[_saveRecipes.recipes[i]]);
            }

            
        }

        [ContextMenu("Save")]
        public void Save()
        {
            _saveRecipes.recipes.Clear();
            for (int i = 0; i < _recipes.Count; i++)
            {
                int index = i;
                _saveRecipes.recipes.Add(_recipeContainer.GetRecipeID(_recipes[index]));
            }

            string json = JsonUtility.ToJson(_saveRecipes);
            File.WriteAllText(_filePath, json);
        }

        public void AddRecipe(CraftRecipe craftRecipe)
        {
            if (_recipes.Contains(craftRecipe))
            {
                return;
            }
            _recipes.Add(craftRecipe);
            CreateRecipeList();
            Save();
            changed?.Invoke();
        }

        // ����� ��������� ��������, ��������� ��� ������ (����� ������ ������� ��������?)
        private void OnInventoryChanged(Inventory.RuntimeItem item)
        {
            foreach (var slot in _activeSlots)
            {
                slot.UpdateAvailability();
            }
            changed?.Invoke();
        }

        [ContextMenu("Create Recipes List")]
        private void CreateRecipeList()
        {
            // Старый список не назначен (новое меню CraftMenuView) - строить нечего
            if (_recipesContainer == null || _recipeUiPrefab == null) return;

            // ������� ������� (�� ������ ������)
            foreach (Transform child in _recipesContainer) Destroy(child.gameObject);
            _activeSlots.Clear();

            // ������� ������ ��� ������� �������
            foreach (var recipe in _recipes)
            {
                var newSlot = Instantiate(_recipeUiPrefab, _recipesContainer);
                newSlot.Initialize(recipe, this);
                _activeSlots.Add(newSlot);
            }
        }

        // ��������������� ����� ��� UI
        public int GetItemCount(Item item) => _inventory.GetItemCount(item);

        // ������ ��������
        public bool CanCraft(CraftRecipe recipe)
        {
            foreach (var ing in recipe.ingredients)
            {
                if (_inventory.GetItemCount(ing.item) < ing.count) return false;
            }

            float ingredientsWeight = 0f;
            foreach (var ing in recipe.ingredients)
                ingredientsWeight += ing.item != null ? ing.item.weight * ing.count : 0f;

            float resultWeight = recipe.resultItem != null ? recipe.resultItem.weight * recipe.resultCount : 0f;
            if (_inventory.weight - ingredientsWeight + resultWeight > _inventory.maxWeight) return false;

            return true;
        }

        // Сколько раз подряд можно создать: хватает ингредиентов и не превышен вес
        public int MaxCraftCount(CraftRecipe recipe)
        {
            if (recipe == null || recipe.resultItem == null) return 0;
            int max = int.MaxValue;
            float ingredientsWeight = 0f;
            foreach (var ing in recipe.ingredients)
            {
                if (ing.item == null || ing.count <= 0) continue;
                max = Mathf.Min(max, _inventory.GetItemCount(ing.item) / ing.count);
                ingredientsWeight += ing.item.weight * ing.count;
            }
            if (max == int.MaxValue) max = 99;

            float delta = recipe.resultItem.weight * recipe.resultCount - ingredientsWeight;
            if (delta > 0f) max = Mathf.Min(max, Mathf.FloorToInt((_inventory.maxWeight - _inventory.weight) / delta + 0.0001f));
            return Mathf.Max(0, max);
        }

        // Хватает ли ингредиентов (без учёта веса) - чтобы меню могло сказать «слишком тяжело»
        public bool HasIngredients(CraftRecipe recipe)
        {
            foreach (var ing in recipe.ingredients)
                if (ing.item != null && _inventory.GetItemCount(ing.item) < ing.count) return false;
            return true;
        }

        public float freeWeight => _inventory.maxWeight - _inventory.weight;

        // ��� ������� ������
        public void TryCraft(CraftRecipe recipe)
        {
            if (!CanCraft(recipe)) return;

            // 1. �������� �������
            foreach (var ing in recipe.ingredients)
            {
                _inventory.RemoveItem(ing.item, ing.count);
            }

            // 2. ���� �������
            _inventory.AddItem(recipe.resultItem, recipe.resultCount);

            // ���������� UI ���������� ������������� ����� ������� OnInventoryChanged
            Debug.Log($"���������: {recipe.resultItem.nameItem}");
        }
    }
}