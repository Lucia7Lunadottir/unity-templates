using PG.CraftSystem;
using PG.Localization;
using UnityEngine;

namespace PG.InventoryManagement
{
    [CreateAssetMenu(menuName = "PG/Inventory/Item")]
    public class Item : ScriptableObject
    {
        [Header("Localization Keys")]
        [SerializeField] private string _nameKey;        // ��������: "item_apple_name"
        [SerializeField] private string _descriptionKey; // ��������: "item_apple_desc"
        [field: SerializeField] public string nameItem { get; private set; }
        [field: SerializeField, TextArea(3, 10)] public string descriptionItem { get; private set; }
        [field: SerializeField] public Sprite icon { get; private set; }
        [field: SerializeField] public float weight { get; private set; }
        public enum ItemType { Supplies, Materials, Artifacts, Important }
        [field: SerializeField] public ItemType itemType { get; private set; }

        [field: SerializeField] public bool isStackable { get; private set; } = true;
        [field: SerializeField] public bool isUsable { get; private set; } = true;
        [field: SerializeField] public bool isGiftable { get; private set; }
        [field: SerializeField] public bool isGoodGift { get; private set; }

        [field: SerializeField] public bool isBurnable { get; private set; }
        [field: SerializeField] public float heatValue { get; private set; }

        [Header("Drop Prefabs")]
        [Tooltip("Prefabs spawned when dropped from inventory. One is chosen at random.")]
        [field: SerializeField] public DropEntry[] dropPrefabs { get; private set; }
        public DropEntry firstDropEntry => dropPrefabs.Length > 0 ? dropPrefabs[0] : null;
        
        [System.Serializable]
        public class DropEntry
        {
            public GameObject prefab;
            [Min(0f), Tooltip("Half-height of the prefab so it spawns above the floor")]
            public float halfHeight = 0.25f;

            public bool isRandomScale;
            public float scale = 4f;
            public float maxScale = 6f;
        }

        // Имя и описание на текущем языке; нет ключа или перевода - текст из ассета
        public string localizedName => Localize(_nameKey, nameItem);
        public string localizedDescription => Localize(_descriptionKey, descriptionItem);

        private static string Localize(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key) || LocalizationSystem.instance == null) return fallback;
            return LocalizationSystem.instance.GetLocalizedValue(key, fallback);
        }

        // Сколько сытости даёт предмет (показывается в меню предметов; 0 - ничего)
        public virtual float satiety => 0f;

        public virtual void Interact(Inventory inventory)
        {
        }

        // ��������� ������ ��� ��������� ������
        public string GetNameKey()
        {
            if (LocalizationSystem.instance != null)
            {
                return LocalizationSystem.instance.GetLocalizedValue(_nameKey, nameItem);
            }
            return _nameKey;
        }
        public string GetDescriptionKey()
        {
            if (LocalizationSystem.instance != null)
            {
                return LocalizationSystem.instance.GetLocalizedValue(_descriptionKey, descriptionItem);
            }
            return _descriptionKey;
        }
    }
}
