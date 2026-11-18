using UnityEngine;

namespace PG.CraftSystem
{
    [CreateAssetMenu(menuName = "PG/Craft/Recipe Container")]
    public class CraftRecipeContainer : ScriptableObject
    {
        public CraftRecipe[] recipes;
        public int GetRecipeID(CraftRecipe recipe)
        {
            for (int i = 0; i < recipes.Length; i++)
            {
                if (recipes[i] == recipe)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}