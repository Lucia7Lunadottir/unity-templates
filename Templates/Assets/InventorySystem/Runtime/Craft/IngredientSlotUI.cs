using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PG.CraftSystem
{
    public class IngredientSlotUI : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _countText;

        // Метод, который мы вызовем из основной панели крафта
        public void SetData(Sprite iconSprite, int currentCount, int requiredCount)
        {
            _icon.sprite = iconSprite;

            // Красим текст: Красный, если не хватает. Белый, если хватает.
            string colorHex = (currentCount >= requiredCount) ? "#FFFFFF" : "#FF0000";

            // Формат: "2/5"
            _countText.text = $"<color={colorHex}>{currentCount}/{requiredCount}</color>";
        }
    }
}