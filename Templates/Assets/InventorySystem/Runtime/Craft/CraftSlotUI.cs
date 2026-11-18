using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using PG.Localization;
using UnityEngine.EventSystems;

namespace PG.CraftSystem
{
    public class CraftSlotUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image _resultIconImage; // Иконка результата
        [SerializeField] private TMP_Text _resultNameText; // Имя результата
        [SerializeField] private TMP_Text _resultCountText; // Имя результата

        [Header("Ingredients Setup")]
        [SerializeField] private Transform _ingredientsContainer; // Сюда будем спавнить иконки ресурсов
        [SerializeField] private IngredientSlotUI _ingredientPrefab; // Ссылка на префаб IngredientSlotUI

        [Header("Button")]
        [SerializeField] private Button _craftButton;

        private CraftRecipe _currentRecipe;
        private CraftController _controller;

        // Список созданных слотов, чтобы обновлять их, а не удалять/создавать заново каждый кадр
        private List<IngredientSlotUI> _activeIngredientSlots = new List<IngredientSlotUI>();

        private void Start()
        {
            LocalizationSystem.instance.localizationChanged += OnLocalize;
        }

        private void OnDestroy()
        {
            if (LocalizationSystem.instance != null)
                LocalizationSystem.instance.localizationChanged -= OnLocalize;
        }

        public void Initialize(CraftRecipe recipe, CraftController controller)
        {
            _currentRecipe = recipe;
            _controller = controller;

            // 1. Ставим иконку результата
            if (recipe.resultItem != null)
            {
                _resultIconImage.sprite = recipe.resultItem.icon;
                _resultCountText.text = recipe.resultCount.ToString();
            }

            // 2. Настраиваем кнопку
            _craftButton.onClick.RemoveAllListeners();
            _craftButton.onClick.AddListener(() => _controller.TryCraft(recipe));

            // 3. Создаем слоты для ингредиентов (ОДИН РАЗ)
            RebuildIngredients();

            OnLocalize();
            UpdateAvailability();
        }

        private void RebuildIngredients()
        {
            // Очищаем старые, если были
            foreach (Transform child in _ingredientsContainer) Destroy(child.gameObject);
            _activeIngredientSlots.Clear();

            // Создаем новые слоты под каждый ингредиент рецепта
            foreach (var ingredient in _currentRecipe.ingredients)
            {
                var slot = Instantiate(_ingredientPrefab, _ingredientsContainer);
                _activeIngredientSlots.Add(slot);
            }
        }

        public void OnLocalize(string val = "")
        {
            if (_currentRecipe == null || _currentRecipe.resultItem == null) return;

            string key = _currentRecipe.resultItem.GetNameKey();
            _resultNameText.text = string.IsNullOrEmpty(key)
                ? _currentRecipe.resultItem.nameItem
                : LocalizationSystem.instance.GetLocalizedValue(key);
        }

        // Этот метод вызывается, когда что-то меняется в инвентаре
        public void UpdateAvailability()
        {
            if (_currentRecipe == null) return;

            bool canCraft = true;

            // Проходимся по списку ингредиентов рецепта и обновляем наши слоты
            for (int i = 0; i < _currentRecipe.ingredients.Count; i++)
            {
                var ingredientData = _currentRecipe.ingredients[i];
                var uiSlot = _activeIngredientSlots[i];

                int currentCount = _controller.GetItemCount(ingredientData.item);

                // Передаем данные в UI слот (иконка, сколько есть, сколько надо)
                uiSlot.SetData(ingredientData.item.icon, currentCount, ingredientData.count);

                if (currentCount < ingredientData.count)
                { 
                    canCraft = false;
                }
            }

            if (!canCraft && EventSystem.current.currentSelectedGameObject == _craftButton.gameObject)
            {
                _controller.craftMenuButton.Select();
            }

            // Визуализация доступности кнопки
            _craftButton.interactable = canCraft;
        }
    }
}