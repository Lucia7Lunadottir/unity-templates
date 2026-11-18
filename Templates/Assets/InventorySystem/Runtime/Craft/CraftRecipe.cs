using UnityEngine;
using System.Collections.Generic;
using PG.InventoryManagement;

namespace PG.CraftSystem
{
    [CreateAssetMenu(fileName = "New Recipe", menuName = "PG/Craft/Recipe")]
    public class CraftRecipe : ScriptableObject
    {
        [Header("Result")]
        public Item resultItem;
        [Min(1)] public int resultCount = 1;

        [Header("Ingredients")]
        public List<Ingredient> ingredients = new List<Ingredient>();

        [System.Serializable]
        public class Ingredient
        {
            public Item item;
            [Min(1)] public int count = 1;
        }
    }
}
