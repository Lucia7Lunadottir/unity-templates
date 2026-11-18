using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.InventoryManagement
{
    public class InventoryCell : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _countText;
        [SerializeField] private TMP_Text _weightText;
        [SerializeField] private TMP_Text _nameText;

        public void Render(Inventory.RuntimeItem runtimeItem)
        {
            if (runtimeItem == null)
            {
                _icon.enabled = false;
                _countText?.gameObject.SetActive(false);
                _weightText?.gameObject.SetActive(false);
                if (_nameText != null)
                {
                    _nameText.text = "";
                }
                return;
            }
            _icon.enabled = runtimeItem.item.icon != null;
            _icon.sprite = runtimeItem.item.icon;

            if (_nameText != null)
            {
                _nameText.text = runtimeItem.item.GetNameKey();
            }

            if (_weightText != null)
            {
                _weightText.text = runtimeItem.item.weight.ToString();
            }

            // Если предметов больше 1, показываем число. Если 1 — скрываем (как в майнкрафте)
            if (runtimeItem.count > 0)
            {
                _countText.text = runtimeItem.count.ToString();
                _countText.gameObject.SetActive(true);
            }
            else
            {
                _countText.gameObject.SetActive(false);
            }
        }
    }
}