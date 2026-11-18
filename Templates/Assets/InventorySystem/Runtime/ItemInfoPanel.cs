using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.InventoryManagement
{
    public class ItemInfoPanel : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private GameObject _contentParent; // Объект, который скрываем, если предмета нет

        private void Awake()
        {
            Render(null);
        }

        public void Render(Item item)
        {
            if (item == null)
            {
                _iconImage.enabled = false;
                _nameText.text = "";
                _descriptionText.text = "";
                return;
            }

            if (_contentParent != null) _contentParent.SetActive(true);

            // Иконка
            if (_iconImage != null)
            {
                _iconImage.enabled = true;
                _iconImage.sprite = item.icon;
            }

            // Текст
            _nameText.text = item.GetNameKey();
            _descriptionText.text = item.GetDescriptionKey();
        }
    }
}