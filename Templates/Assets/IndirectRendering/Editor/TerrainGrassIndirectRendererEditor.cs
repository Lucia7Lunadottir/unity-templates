using UnityEditor;
using UnityEngine;

namespace PG.Rendering.Editor
{
    [CustomEditor(typeof(TerrainGrassIndirectRenderer))]
    [CanEditMultipleObjects]
    public class TerrainGrassIndirectRendererEditor : TerrainPrototypeOverrideEditorBase
    {
        protected override string[] GetPrototypeNames(TerrainData terrainData)
        {
            DetailPrototype[] prototypes = terrainData.detailPrototypes;
            var names = new string[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
            {
                if (prototypes[i].usePrototypeMesh && prototypes[i].prototype != null)
                    names[i] = prototypes[i].prototype.name;
                else if (prototypes[i].prototypeTexture != null)
                    names[i] = $"{prototypes[i].prototypeTexture.name} (текстура, нужен override)";
                else
                    names[i] = "(пусто)";
            }
            return names;
        }
    }
}
