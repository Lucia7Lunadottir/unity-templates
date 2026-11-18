using System;
using System.Collections.Generic;
using PG.CraftSystem;
using PG.InventoryManagement;
using UnityEngine;

// Чертежи Build Mode: категории, в каждой - рецепты крафта, чей результат ставится в мир (PlaceableItem с worldPrefab).
// Стоимость - ингредиенты рецепта, модель - worldPrefab предмета. Чертежи открыты сразу, кроме списка «Found In World» (их находят в домах, BlueprintPickup);
// в меню крафта такие рецепты не показываются. Категория с inventoryItems - не чертежи, а вещи из сумки (см. CanLay).
[CreateAssetMenu(fileName = "Build Catalog", menuName = "PG/Build/Catalog")]
public class BuildCatalog : ScriptableObject
{
    [Serializable]
    public class Category
    {
        public LocalText title = new LocalText("Category");
        [Tooltip("Вместо чертежей - вещи из сумки: кладутся на землю их моделью (как выброшенные), Y - обратно в сумку")]
        public bool inventoryItems;
        public List<CraftRecipe> blueprints = new List<CraftRecipe>();
    }

    public List<Category> categories = new List<Category>();
    [Tooltip("Чертежи, которые закрыты, пока игрок не найдёт их в мире")]
    public List<CraftRecipe> foundInWorld = new List<CraftRecipe>();

    public bool IsUnlocked(CraftRecipe recipe) => recipe != null && (!foundInWorld.Contains(recipe) || BlueprintUnlocks.Has(recipe));

    public static PlaceableItem PlaceableOf(CraftRecipe recipe) =>
        recipe != null && recipe.resultItem is PlaceableItem item && item.worldPrefab != null ? item : null;

    // Вещь из сумки, которую можно положить: есть модель на земле; мебель - чертежами, важные (сюжетные) не выкладываются, как и в «Выбросить»
    public static bool CanLay(Item item) =>
        item != null && item.itemType != Item.ItemType.Important && !(item is PlaceableItem p && p.worldPrefab != null)
        && item.dropPrefabs != null && item.dropPrefabs.Length > 0 && item.dropPrefabs[0].prefab != null;

    // Чертёж, по которому построен предмет (для разбора)
    public CraftRecipe Find(Item item)
    {
        foreach (var category in categories)
            foreach (var recipe in category.blueprints)
                if (recipe != null && recipe.resultItem == item) return recipe;
        return null;
    }

    public bool Contains(CraftRecipe recipe)
    {
        foreach (var category in categories)
            if (category.blueprints.Contains(recipe)) return true;
        return false;
    }
}
