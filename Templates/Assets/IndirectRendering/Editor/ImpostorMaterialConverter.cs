using System.IO;
using UnityEditor;
using UnityEngine;

namespace PG.Rendering.Editor
{
    // Правый клик по материалу, запечённому AmplifyImpostors -> Create Indirect Impostor Material.
    // Копирует одноимённые свойства (_Albedo/_Frames/_ImpostorSize/_ClipMask) на новый материал с
    // IndirectImpostor.shader через Material.CopyPropertiesFromMaterial - руками вписывать числа
    // не нужно, имена свойств у IndirectImpostor.shader совпадают с тем, что печёт AmplifyImpostors.
    public static class ImpostorMaterialConverter
    {
        [MenuItem("Assets/Create Indirect Impostor Material", true)]
        private static bool Validate() => Selection.activeObject is Material;

        [MenuItem("Assets/Create Indirect Impostor Material")]
        private static void Convert()
        {
            var source = (Material)Selection.activeObject;
            Shader shader = Shader.Find("Custom/IndirectImpostor");
            if (shader == null)
            {
                Debug.LogError("ImpostorMaterialConverter: шейдер Custom/IndirectImpostor не найден.");
                return;
            }

            var target = new Material(shader);
            target.CopyPropertiesFromMaterial(source);

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(Path.GetDirectoryName(sourcePath), source.name + "_Indirect.mat"));

            AssetDatabase.CreateAsset(target, targetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(target);
        }
    }
}
