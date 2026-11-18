#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PG.Rendering
{
    // Показывает деревья Terrain через IndirectRenderBatch в Edit Mode (Scene View / Game View без Play)
    // с culling, LOD-цепочкой и импостором - ровно так, как собирает их ваш TerrainTreeIndirectRenderer.
    // Шейдеры не трогает, ваши классы не меняет: настройки читает из рендереров на сцене, а LOD-уровни
    // строит их же методами ResolveLevels/AppendImpostorLevel (через reflection).
    // Существует только в редакторе (#if UNITY_EDITOR) - в билд не попадает.
    //
    // Родные деревья Terrain на время предпросмотра скрываются (treeDistance = 0), как это делает
    // сам рендерер в Play. Исходное значение ВСЕГДА возвращается назад (RestoreTerrains) -
    // перед Play, перед сохранением сцены, перед перекомпиляцией, при выключении и закрытии сцены.
    //
    // Память: все GraphicsBuffer лежат внутри IndirectRenderBatch, Clear() делает Dispose каждому.
    [InitializeOnLoad]
    public static class TerrainTreeEditorPreview
    {
        private const string PrefKey = "PG.TerrainTreeEditorPreview.Enabled";

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Поля/методы рендерера берём через reflection - чтобы не менять ваши классы (protected/private).
        private static readonly FieldInfo TerrainField =
        typeof(TerrainIndirectRendererBase).GetField("_terrain", Flags);
        private static readonly FieldInfo CameraField =
        typeof(TerrainIndirectRendererBase).GetField("_targetCamera", Flags);
        private static readonly FieldInfo OverridesField =
        typeof(TerrainIndirectRendererBase).GetField("_prototypeOverrides", Flags);
        private static readonly FieldInfo CullingShaderField =
        typeof(TerrainIndirectRendererBase).GetField("_cullingShader", Flags);
        private static readonly FieldInfo CullRadiusField =
        typeof(TerrainIndirectRendererBase).GetField("_cullRadius", Flags);
        private static readonly FieldInfo RotationOffsetField =
        typeof(TerrainTreeIndirectRenderer).GetField("_rotationOffsetDegrees", Flags);
        private static readonly FieldInfo InvertRotationField =
        typeof(TerrainTreeIndirectRenderer).GetField("_invertRotation", Flags);
        private static readonly FieldInfo DisableTreesField =
        typeof(TerrainTreeIndirectRenderer).GetField("_disableTerrainTrees", Flags);
        private static readonly MethodInfo ResolveLevelsMethod =
        typeof(TerrainIndirectRendererBase).GetMethod("ResolveLevels", Flags);
        private static readonly MethodInfo AppendImpostorMethod =
        typeof(TerrainIndirectRendererBase).GetMethod("AppendImpostorLevel", Flags);
        private static readonly MethodInfo ResolveIndirectMaterialMethod =
        typeof(TerrainIndirectRendererBase).GetMethod("ResolveIndirectMaterial", Flags);
        private static readonly MethodInfo ApplyIndirectMaterialsMethod =
        typeof(TerrainIndirectRendererBase).GetMethod("ApplyIndirectMaterials", Flags);
        // Статический метод - в Flags нет BindingFlags.Static, добавляем отдельно только здесь.
        private static readonly MethodInfo ResolveRendererScaleMethod =
        typeof(TerrainTreeIndirectRenderer).GetMethod("ResolveRendererScale", Flags | BindingFlags.Static);

        // Общий батч на пару (mesh, material), как в рантайме: инстансы всех тайлов сливаются.
        private sealed class Group
        {
            public readonly List<InstanceData> Data = new();
            public MeshLevel[] Levels; // берутся у первого вклада, как в рантайме
            public float CullRadius;
        }

        private static ComputeShader s_cullingShader; // null -> рисуем без culling (медленно)
        private static readonly List<IndirectRenderBatch> s_batches = new();
        private static float s_cullRadius = 100f;
        // Terrain -> его настоящий treeDistance, пока мы держим его в 0.
        private static readonly Dictionary<Terrain, float> s_hiddenTrees = new();
        private static bool s_rebuildQueued;
        private static readonly Dictionary<Terrain, int> s_treeCounts = new();
        private static double s_nextPoll;
        private static double s_stableSince = -1;
        private static int s_lastTotal;

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false); // по умолчанию ВЫКЛЮЧЕНО
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static TerrainTreeEditorPreview()
        {
            // Отложено: на самом раннем старте домена видеокарта может быть ещё не готова.
            EditorApplication.delayCall += Init;
        }

        private static void Init()
        {
            // -= перед += : подписка не задваивается, сколько бы раз ни вызвали Init.
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;

            EditorApplication.quitting -= Clear;
            EditorApplication.quitting += Clear;

            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;

            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorSceneManager.sceneClosing += OnSceneClosing;

            // Перед записью сцены на диск возвращаем treeDistance, после записи - снова прячем.
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;

            Undo.undoRedoPerformed -= QueueRebuild;
            Undo.undoRedoPerformed += QueueRebuild;

            QueueRebuild();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Перед Play всё освобождаем и возвращаем treeDistance - иначе рантайм-рендерер
            // запомнил бы наш 0 как "оригинальное" значение и потом не смог бы его вернуть.
            if (state == PlayModeStateChange.ExitingEditMode) Clear();
            else if (state == PlayModeStateChange.EnteredEditMode) QueueRebuild();
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode) => QueueRebuild();

        private static void OnSceneClosing(UnityEngine.SceneManagement.Scene scene, bool removing)
        {
            Clear();
            QueueRebuild();
        }

        private static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => RestoreTerrains();

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene) => QueueRebuild();

        // Один пересбор за тик, даже если событий пришло несколько.
        private static void QueueRebuild()
        {
            if (s_rebuildQueued) return;
            s_rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                s_rebuildQueued = false;
                Rebuild();
            };
        }

        private static T GetField<T>(FieldInfo field, object target, T fallback)
        {
            if (field == null) return fallback;
            object value = field.GetValue(target);
            return value is T typed ? typed : fallback;
        }

        private static void Rebuild()
        {
            Clear();
            if (!Enabled || Application.isPlaying || EditorApplication.isCompiling) return;

            var groups = new Dictionary<(Mesh, Material, string), Group>();
            Bounds? worldBounds = null;

            var renderers = Object.FindObjectsByType<TerrainTreeIndirectRenderer>(FindObjectsSortMode.None);
            foreach (TerrainTreeIndirectRenderer renderer in renderers)
            {
                // Тот же отбор, что в рантайме: компонент включён -> он рисует этот тайл.
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                Terrain terrain = GetField<Terrain>(TerrainField, renderer, null);
                if (terrain == null) terrain = renderer.GetComponent<Terrain>();
                if (terrain == null) terrain = Terrain.activeTerrain;
                if (terrain == null || terrain.terrainData == null) continue;

                if (s_cullingShader == null)
                    s_cullingShader = GetField<ComputeShader>(CullingShaderField, renderer, null);
                float cullRadius = GetField(CullRadiusField, renderer, 100f);
                s_cullRadius = cullRadius;

                AddTerrain(renderer, terrain, cullRadius, groups);
                s_treeCounts[terrain] = terrain.terrainData.treeInstanceCount;

                if (GetField(DisableTreesField, renderer, true) && !s_hiddenTrees.ContainsKey(terrain))
                {
                    // Исходное значение - ещё и в SessionState: статика теряется при перезагрузке скриптов,
                    // и тогда уже спрятанный 0 запомнился бы как "исходный"
                    if (terrain.treeDistance > 0f) SessionState.SetFloat(TreeDistanceKey(terrain), terrain.treeDistance);
                    s_hiddenTrees[terrain] = SessionState.GetFloat(TreeDistanceKey(terrain), terrain.treeDistance);
                    terrain.treeDistance = 0f;
                }

                Vector3 size = terrain.terrainData.size;
                var tb = new Bounds(terrain.transform.position + size * 0.5f, size);
                if (worldBounds.HasValue) { var b = worldBounds.Value; b.Encapsulate(tb); worldBounds = b; }
                else worldBounds = tb;
            }

            if (!worldBounds.HasValue) return;

            Bounds bounds = worldBounds.Value;
            bounds.Expand(100f); // запас на размер кроны

            foreach (var kvp in groups)
            {
                Group group = kvp.Value;
                if (group.Data.Count == 0) continue;
                /*Debug.Log($"TerrainTreeEditorPreview: {kvp.Key.Item1.name}, {group.Data.Count} деревьев, culling " +
                (s_cullingShader != null ? "ВКЛ" : "ВЫКЛ (нет _cullingShader у рендерера!)") + ", уровни: " +
                (group.Levels == null ? "нет" : string.Join(" | ", Array.ConvertAll(group.Levels,
                                                                                    l => $"{(l.Mesh != null ? l.Mesh.name : "?")}[{l.MinDistance:0.#}-{l.MaxDistance:0.#}]"))));
                // Без culling-шейдера levels не нужны - батч рисует всё одним мешом (медленно). */
                s_batches.Add(new IndirectRenderBatch(kvp.Key.Item1, kvp.Key.Item2, group.Data.ToArray(),
                                                      bounds, s_cullingShader, s_cullingShader != null ? group.Levels : null));
            }

            SceneView.RepaintAll();
        }

        private static void AddTerrain(TerrainTreeIndirectRenderer renderer, Terrain terrain, float cullRadius,
                                       Dictionary<(Mesh, Material, string), Group> groups)
        {
            var overrides = GetField<TerrainIndirectRendererBase.PrototypeOverride[]>(OverridesField, renderer, null);
            float rotationOffset = GetField(RotationOffsetField, renderer, 0f);
            bool invertRotation = GetField(InvertRotationField, renderer, false);

            TerrainData terrainData = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrainData.size;
            TreePrototype[] prototypes = terrainData.treePrototypes;

            var perProto = new List<InstanceData>[prototypes.Length];
            var scaleSum = new float[prototypes.Length];
            // "Корень x дочерние объекты" (ResolveRendererScale) на прототип - ScaleMultiplier для
            // деревьев отключён и здесь, как и в самом TerrainTreeIndirectRenderer.BuildBatches.
            // <= 0 - ещё не посчитан для этого p (ResolveRendererScale сам никогда не возвращает <= 0).
            var rendererScale = new float[prototypes.Length];

            foreach (TreeInstance t in terrainData.treeInstances)
            {
                int p = t.prototypeIndex;
                if (p < 0 || p >= prototypes.Length) continue;

                GameObject prefab = prototypes[p].prefab;
                if (rendererScale[p] <= 0f)
                    rendererScale[p] = ResolveRendererScaleMethod != null
                        ? (float)ResolveRendererScaleMethod.Invoke(null, new object[] { prefab })
                        : 1f;
                // Тот же масштаб, что в Play (ResolveRendererScale): у запечённых детей - только корень
                float scaleMultiplier = rendererScale[p];

                float angleDeg = t.rotation * Mathf.Rad2Deg * (invertRotation ? -1f : 1f) + rotationOffset;
                float instanceScale = t.widthScale;
                scaleSum[p] += instanceScale;
                (perProto[p] ??= new List<InstanceData>()).Add(new InstanceData
                {
                    Position = origin + Vector3.Scale(t.position, size),
                    Scale = instanceScale * scaleMultiplier,
                    // Только поворот с террейна, как в Play: поворот ребёнка уже запечён в меш (BakeIntoRootSpace)
                    Rotation = Quaternion.Euler(0f, angleDeg, 0f)
                });
            }

            for (int p = 0; p < prototypes.Length; p++)
            {
                List<InstanceData> list = perProto[p];
                if (list == null || list.Count == 0) continue;

                GameObject prefab = prototypes[p].prefab;
                MeshFilter mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
                MeshRenderer mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                TerrainIndirectRendererBase.PrototypeOverride over = GetOverride(overrides, p, prefab);

                (Mesh mesh, Material material) = ResolveBase(over, mf != null ? mf.sharedMesh : null,
                                                             mr != null ? mr.sharedMaterial : null);
                if (mesh == null || material == null) continue;

                // Подмена шейдера на Indirect-аналог (см. ResolveIndirectMaterial в базовом классе) -
                // та же таблица _indirectShaderConversions, что и в Play, через reflection.
                if (ResolveIndirectMaterialMethod != null)
                    material = (Material)ResolveIndirectMaterialMethod.Invoke(renderer, new object[] { material });

                // Ключ - вся LOD-цепочка (меши/материалы), как в рантайме: одинаковый ствол с разной листвой не сливается
                float avgScale = scaleSum[p] / list.Count * rendererScale[p];
                MeshLevel[] levels = BuildLevels(renderer, prefab, mesh, over, cullRadius, avgScale);
                string signature = string.Join("|", Array.ConvertAll(levels, l =>
                    $"{(l.Mesh != null ? l.Mesh.GetHashCode().ToString() : "-")}/{(l.Material != null ? l.Material.GetHashCode().ToString() : "-")}/{l.ForceMeshLod}/{l.DisableShadows}"));
                var key = (mesh, material, signature);
                if (!groups.TryGetValue(key, out Group group))
                {
                    groups[key] = group = new Group();
                    group.Levels = levels;
                }
                group.Data.AddRange(list);
            }
        }

        // Зеркалит TerrainIndirectRendererBase.GetOverride (тот недоступен статически без инстанса) -
        // локальный override этого тайла побеждает и сам пишется в TreePrototypeOverrideLibrary,
        // пустой локальный - читает из неё. Без этого превью в Edit Mode показывало бы "голые"
        // тайлы, у которых override настроен только на каком-то ДРУГОМ тайле, пока не нажмёшь Play.
        private static TerrainIndirectRendererBase.PrototypeOverride GetOverride(
            TerrainIndirectRendererBase.PrototypeOverride[] overrides, int index, GameObject prefab)
        {
            TerrainIndirectRendererBase.PrototypeOverride local =
                overrides != null && index >= 0 && index < overrides.Length ? overrides[index] : default;
            return TerrainIndirectRendererBase.ResolveOverride(local, prefab);
        }

            // То же правило, что у ResolveBaseMeshMaterial в базовом классе: первая часть с мешем из
            // ручных LodLevels, недостающее - из префаба.
            private static (Mesh, Material) ResolveBase(TerrainIndirectRendererBase.PrototypeOverride over,
                                                        Mesh fallbackMesh, Material fallbackMaterial)
            {
                if (over.LodLevels != null)
                {
                    foreach (var level in over.LodLevels)
                    {
                        if (level.Parts == null) continue;
                        foreach (var part in level.Parts)
                        {
                            if (part.Mesh == null) continue;
                            return (part.Mesh, part.Material != null ? part.Material : fallbackMaterial);
                        }
                    }
                }
                return (fallbackMesh, fallbackMaterial);
            }

            // LOD-цепочка + импостор - методами самого рендерера, так результат совпадает с Play.
            private static MeshLevel[] BuildLevels(TerrainTreeIndirectRenderer renderer, GameObject prefab, Mesh mesh,
                                                   TerrainIndirectRendererBase.PrototypeOverride over, float cullRadius, float averageInstanceScale)
            {
                MeshLevel[] single = { new MeshLevel { Mesh = mesh, MinDistance = 0f, MaxDistance = cullRadius, ForceMeshLod = 0 } };
                if (ResolveLevelsMethod == null || AppendImpostorMethod == null)
                {
                    Debug.LogWarning("TerrainTreeEditorPreview: не нашёл ResolveLevels/AppendImpostorLevel " +
                    "(переименованы?) - рисую только один уровень без LOD.");
                    return single;
                }

                // В Edit Mode OnEnable не выполнялся, камера у рендерера может быть пустой, а без неё
                // CullingReady == false и LOD не строятся. Временно подставляем Camera.main и сразу убираем.
                bool needTempCamera = CameraField != null && GetField<Camera>(CameraField, renderer, null) == null;
                if (needTempCamera)
                {
                    Camera cam = Camera.main;
                    if (cam == null && SceneView.lastActiveSceneView != null) cam = SceneView.lastActiveSceneView.camera;
                    CameraField.SetValue(renderer, cam);
                }
                try
                {
                    // over.ScaleMultiplier для деревьев отключён (см. TerrainTreeIndirectRenderer.BuildBatches) -
                    // ResolveLevels сам домножает на него дистанции, если он > 0, так что здесь тоже
                    // принудительно обнуляем копию, чтобы LOD-дистанции совпадали с Play.
                    TerrainIndirectRendererBase.PrototypeOverride overForLevels = over;
                    overForLevels.ScaleMultiplier = 0f;

                    var levels = (MeshLevel[])ResolveLevelsMethod.Invoke(renderer,
                                                                         new object[] { prefab, mesh, overForLevels, cullRadius, averageInstanceScale });
                    if (ApplyIndirectMaterialsMethod != null)
                        levels = (MeshLevel[])ApplyIndirectMaterialsMethod.Invoke(renderer, new object[] { levels });
                    levels = (MeshLevel[])AppendImpostorMethod.Invoke(renderer, new object[] { levels, over, cullRadius });
                    Camera usedCam = GetField<Camera>(CameraField, renderer, null);
                    /* Debug.Log($"TerrainTreeEditorPreview LOD-расчёт [{(prefab != null ? prefab.name : "?")}]: " +
                    $"камера={(usedCam != null ? usedCam.name + " fov=" + usedCam.fieldOfView.ToString("0") : "НЕТ")}, " +
                    $"LODGroup={(prefab != null && prefab.GetComponentInChildren<LODGroup>() != null ? "есть" : "нет")}, " +
                    $"ручных LodLevels={(over.LodLevels != null ? over.LodLevels.Length : 0)}, " +
                    $"ScaleMultiplier={over.ScaleMultiplier}, средний масштаб={averageInstanceScale:0.##}, " +
                    $"cullRadius={cullRadius}"); */
                    return levels != null && levels.Length > 0 ? levels : single;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"TerrainTreeEditorPreview: не удалось построить LOD ({e.GetBaseException().Message}) - " +
                    "рисую один уровень.");
                    return single;
                }
                finally
                {
                    if (needTempCamera) CameraField.SetValue(renderer, null);
                }
            }

            // Вернуть родным деревьям настоящий treeDistance. Батчи не трогает.
            private static void RestoreTerrains()
            {
                foreach (var kvp in s_hiddenTrees)
                    if (kvp.Key != null && kvp.Value > 0f) kvp.Key.treeDistance = kvp.Value;
                s_hiddenTrees.Clear();
                // Спрятанные до перезагрузки скриптов (в словаре их уже нет) - по копии из SessionState
                foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
                {
                    if (terrain.treeDistance > 0f) continue;
                    float saved = SessionState.GetFloat(TreeDistanceKey(terrain), 0f);
                    if (saved > 0f) terrain.treeDistance = saved;
                }
            }

            private static string TreeDistanceKey(Terrain terrain) =>
                "PG.TerrainTreeEditorPreview.TreeDistance." + GlobalObjectId.GetGlobalObjectIdSlow(terrain);

            // Следим за числом деревьев: Terrain-кисть добавляет/удаляет деревья, а наш буфер - снимок.
            // Когда число перестало меняться на 0.4 с (кисть отпущена) - пересобираем.
            private static void OnEditorUpdate()
            {
                if (s_treeCounts.Count == 0 || Application.isPlaying || s_rebuildQueued) return;
                double now = EditorApplication.timeSinceStartup;
                if (now < s_nextPoll) return;
                s_nextPoll = now + 0.25;

                int total = 0;
                bool mismatch = false;
                foreach (var kvp in s_treeCounts)
                {
                    if (kvp.Key == null || kvp.Key.terrainData == null) { mismatch = true; continue; }
                    int c = kvp.Key.terrainData.treeInstanceCount;
                    total += c;
                    if (c != kvp.Value) mismatch = true;
                }

                if (!mismatch) { s_stableSince = -1; return; }

                // Идёт рисование: освобождаем наши буферы и возвращаем родные деревья - тогда кисть
                // работает как на обычном Terrain (родной рендер, без пересборок на каждый мазок).
                if (s_batches.Count > 0 || s_hiddenTrees.Count > 0)
                {
                    foreach (IndirectRenderBatch batch in s_batches) batch.Dispose();
                    s_batches.Clear();
                    RestoreTerrains();
                }

                if (s_stableSince < 0 || total != s_lastTotal) { s_lastTotal = total; s_stableSince = now; return; }
                if (now - s_stableSince < 1.0) return; // кисть отпущена на секунду - один раз пересобираем
                s_stableSince = -1;
                QueueRebuild();
            }

            private static void Clear()
            {
                s_treeCounts.Clear();
                RestoreTerrains();
                foreach (IndirectRenderBatch batch in s_batches) batch.Dispose();
                s_batches.Clear();
            }

            private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (s_batches.Count == 0 || Application.isPlaying) return;
                // Только Scene View. Результат culling лежит в общих GPU-буферах: если рисовать ещё и Game View,
                // каждая камера затирает отсечение другой (и вдвое больше работы на каждый кадр).
                if (camera.cameraType != CameraType.SceneView) return;

                var settings = new CullingSettings
                {
                    EnableDistance = true,
                    EnableFrustum = true,
                    EnableOcclusion = false,
                    CullRadius = s_cullRadius,
                    DebugInstanceIndex = -1
                };
                foreach (IndirectRenderBatch batch in s_batches)
                    batch.Render(camera, true, settings, IndirectLightingSettings.Default);
            }

            // Публичный API - им пользуются меню и главная панель Unity (TerrainTreeEditorToolbar).
            public static event Action StateChanged;
            public static bool IsEnabled => Enabled;

            public static void SetEnabled(bool value)
            {
                if (Enabled == value) return;
                Enabled = value;
                if (value) QueueRebuild();
                else { Clear(); SceneView.RepaintAll(); }
                StateChanged?.Invoke();
            }

            public static void RequestRebuild() => QueueRebuild();

    }
}
#endif
