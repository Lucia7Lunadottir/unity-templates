using UnityEditor;
using UnityEngine;

namespace PG.Rendering.Editor
{
    [CustomEditor(typeof(TerrainTreeIndirectRenderer))]
    [CanEditMultipleObjects]
    public class TerrainTreeIndirectRendererEditor : TerrainPrototypeOverrideEditorBase
    {
        protected override string[] GetPrototypeNames(TerrainData terrainData)
        {
            TreePrototype[] prototypes = terrainData.treePrototypes;
            var names = new string[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
                names[i] = prototypes[i].prefab != null ? prototypes[i].prefab.name : "(нет префаба)";
            return names;
        }
    }
}
