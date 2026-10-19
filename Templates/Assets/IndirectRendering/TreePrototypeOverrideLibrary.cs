using System.Collections.Generic;
using UnityEngine;

namespace PG.Rendering
{
    // Общий на ВЕСЬ проект склад PrototypeOverride - ключ самого префаба прототипа, а не тайл/
    // индекс. Настроил LOD/импостор для конкретного дерева/травы ОДИН раз (на любом тайле, через
    // обычный инспектор TerrainTreeIndirectRenderer/TerrainGrassIndirectRenderer) - это применяется
    // сразу на ВСЕХ тайлах, где встречается тот же префаб, без ручного повтора и без кнопки
    // "разослать". Один ассет на проект, лежит в Resources - находится сам (см. Instance), руками
    // тащить ссылку в каждый компонент не нужно. См. TerrainIndirectRendererBase.GetOverride.
    public class TreePrototypeOverrideLibrary : ScriptableObject
    {
        private const string ResourcePath = "TreePrototypeOverrideLibrary";
        private const string AssetFolder = "Assets/Resources";

        [System.Serializable]
        public struct Entry
        {
            public GameObject Prefab;
            public TerrainIndirectRendererBase.PrototypeOverride Override;
        }

        [SerializeField] private Entry[] _entries = System.Array.Empty<Entry>();

        private Dictionary<GameObject, int> _indexByPrefab;

        private static TreePrototypeOverrideLibrary s_instance;

        public static TreePrototypeOverrideLibrary Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<TreePrototypeOverrideLibrary>(ResourcePath);
#if UNITY_EDITOR
                if (s_instance == null) s_instance = CreateLibraryAsset();
#endif
                return s_instance;
            }
        }

        private void BuildIndex()
        {
            _indexByPrefab = new Dictionary<GameObject, int>();
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Prefab != null) _indexByPrefab[_entries[i].Prefab] = i;
        }

        public bool TryGet(GameObject prefab, out TerrainIndirectRendererBase.PrototypeOverride over)
        {
            if (prefab == null) { over = default; return false; }
            if (_indexByPrefab == null) BuildIndex();
            if (_indexByPrefab.TryGetValue(prefab, out int index))
            {
                over = _entries[index].Override;
                return true;
            }
#if UNITY_EDITOR
            // Ни тут, ни локально у тайла ничего нет - последний шанс: поискать готовый импостор по
            // соглашению об именах проекта, прежде чем сдаваться (см. TryAutoDiscoverImpostor).
            if (TryAutoDiscoverImpostor(prefab, out Mesh mesh, out Material material))
            {
                over = default;
                over.ImpostorMesh = mesh;
                over.ImpostorMaterial = material;
                Set(prefab, over);
                return true;
            }
#endif
            over = default;
            return false;
        }

        // Пишет/обновляет запись для префаба - вызывается из GetOverride, когда у ЭТОГО тайла для
        // данного прототипа заполнен непустой локальный override: этот тайл становится "источником
        // истины" для префаба, остальные (с пустым локальным override) подтянут его через TryGet.
        public void Set(GameObject prefab, TerrainIndirectRendererBase.PrototypeOverride over)
        {
            if (prefab == null) return;
            if (_indexByPrefab == null) BuildIndex();
            if (_indexByPrefab.TryGetValue(prefab, out int index))
            {
                _entries[index].Override = over;
            }
            else
            {
                System.Array.Resize(ref _entries, _entries.Length + 1);
                _entries[^1] = new Entry { Prefab = prefab, Override = over };
                _indexByPrefab[prefab] = _entries.Length - 1;
            }
#if UNITY_EDITOR
            // Dirty только вне Play - чтобы тестовый прогон в Play не плодил "несохранённые
            // изменения" на ассете от каждого OnEnable/RebuildBatches.
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        // Автопоиск готового импостора по соглашению об именах, которое уже используется в проекте
        // (см. Assets/ScriptableData/World/Imposters/.../{BaseName}_Impostor/) - для префаба с именем
        // BaseName ищем AmplifyImpostorAsset с ИМЕНЕМ "{BaseName}_Impostor" (AmplifyImpostorAsset.Mesh -
        // готовый billboard-меш) и рядом лежащий "{BaseName}_Impostor_Indirect.mat" (материал, уже
        // сконвертированный ImpostorMaterialConverter'ом - НЕ сырой AmplifyImpostorAsset.Material,
        // тот без чтения _InstanceBuffer). BaseName - это ИМЯ КОРНЕВОГО префаба: у Prefab Variant'ов
        // ("Dry3333 Variant") папка импостора названа по базовому префабу ("Dry3333"), не по варианту -
        // пробуем сначала имя как есть, потом с отрезанным " Variant" (повторно, встречается "Variant
        // Variant"). Не универсально - у части деревьев (Aspen/Cedar/Maple и т.п.) папка импостора
        // переименована вручную и под эту формулу не попадает, но у них override уже настроен и
        // лежит в библиотеке/локально, сюда это обращение и не доходит.
        private static bool TryAutoDiscoverImpostor(GameObject prefab, out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;
            if (prefab == null) return false;

            string candidate = prefab.name;
            for (int i = 0; i < 4; i++) // максимум пара попыток отрезать " Variant" - дальше явно не то имя
            {
                if (TryFindImpostorByBaseName(candidate, out mesh, out material)) return true;
                if (!candidate.EndsWith(" Variant")) break;
                candidate = candidate[..^" Variant".Length];
            }
            return false;
        }

        private static bool TryFindImpostorByBaseName(string baseName, out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;
            string folderName = baseName + "_Impostor";

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets($"{folderName} t:AmplifyImpostorAsset"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != folderName) continue; // точное имя, не "содержит"

                // Без compile-зависимости от AmplifyImpostors: поле Mesh читаем через SerializedObject
                var impostorAsset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
                if (impostorAsset == null) continue;
                var impostorMesh = new UnityEditor.SerializedObject(impostorAsset).FindProperty("Mesh")?.objectReferenceValue as Mesh;
                if (impostorMesh == null) continue;

                string indirectMatPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path) ?? "", folderName + "_Indirect.mat");
                Material indirectMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(indirectMatPath);
                if (indirectMat == null) continue;

                mesh = impostorMesh;
                material = indirectMat;
                return true;
            }
            return false;
        }

        // Первое обращение к Instance в Editor'е, пока ассета ещё нет на диске - создаёт его сама,
        // пустым. Дальше Resources.Load его уже находит как обычно.
        private static TreePrototypeOverrideLibrary CreateLibraryAsset()
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(AssetFolder))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources"); // ассет проекта, не пакета: папка пакета только для чтения

            var asset = CreateInstance<TreePrototypeOverrideLibrary>();
            string path = AssetFolder + "/" + ResourcePath + ".asset";
            UnityEditor.AssetDatabase.CreateAsset(asset, path);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"TreePrototypeOverrideLibrary: создан общий ассет '{path}' - override'ы деревьев/травы " +
                      "теперь настраиваются один раз и расходятся по всем тайлам сами.");
            return asset;
        }
#endif
    }
}
