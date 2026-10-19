using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Pool;

namespace PG.VFXPool
{
    public partial class VFXPool : MonoBehaviour
    {
        public static class TransformSetter
        {
            [BurstCompile]
            public static void SetTransform(TransformHandle handle, Vector3 position, Quaternion rotation,
                Vector3 scale)
            {
                handle.SetPositionAndRotation(position, rotation);
                handle.localScale = scale;
            }
        }

        // Эффект, который едет за пустышкой (лапа, голова): поза копируется каждый LateUpdate, пока он играет.
        private struct FollowEntry
        {
            public ParticleSystem Vfx;
            public Transform Anchor;
            public bool UseLossyScale;
        }

        [SerializeField] private VFXPrefabsContainer vfxContainer;

        [SerializeField] private int defaultPoolSize = 100;

        private Dictionary<string, ObjectPool<ParticleSystem>> vfxPools =
            new Dictionary<string, ObjectPool<ParticleSystem>>();

        private Dictionary<ParticleSystem, Coroutine> activeVFXCoroutines = new Dictionary<ParticleSystem, Coroutine>();

        // Map to quickly resolve which pool an active ParticleSystem belongs to
        private Dictionary<ParticleSystem, string> vfxIdMap = new Dictionary<ParticleSystem, string>();

        // Номер запуска экземпляра: растёт при каждом Get, поэтому старая ссылка не остановит эффект, уже выданный другому.
        private readonly Dictionary<ParticleSystem, int> playIds = new Dictionary<ParticleSystem, int>();
        private readonly List<FollowEntry> followers = new List<FollowEntry>();
        private static readonly List<ParticleSystem> shapeScratch = new List<ParticleSystem>();
        private int nextPlayId;

        [AutoStaticsCleanup] public static VFXPool instance { get; private set; }

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        private void InitializePools()
        {
            foreach (VFXData data in vfxContainer.vfxPrefabs)
            {
                CreatePool(data.id);
                Prewarm(data.id, data.prewarm);
            }
        }

        // Экземпляры создаются заранее: первый удар не платит за Instantiate.
        private void Prewarm(string vfxID, int count)
        {
            if (count <= 0 || !vfxPools.TryGetValue(vfxID, out var pool)) return;

            var created = new List<ParticleSystem>(count);
            for (int i = 0; i < count; i++) created.Add(pool.Get());
            foreach (var vfx in created) pool.Release(vfx);
        }

        private void CreatePool(string vfxID)
        {
            var vfx = vfxContainer.GetVFXPrefab(vfxID);
            if (vfx == null)
            {
                Debug.LogError($"Failed to create pool for {vfxID}: Prefab not found!");
                return;
            }

            var pool = new ObjectPool<ParticleSystem>(
                createFunc: () => CreateVFX(vfx),
                actionOnGet: (vfx) => OnGetVFX(vfx),
                actionOnRelease: (vfx) => OnReleaseVFX(vfx),
                actionOnDestroy: (vfx) => Destroy(vfx.gameObject),
                collectionCheck: false,
                defaultCapacity: Mathf.Min(defaultPoolSize, 16),
                maxSize: Mathf.Max(defaultPoolSize, 1)
            );

            vfxPools[vfxID] = pool;
        }

        private ParticleSystem CreateVFX(ParticleSystem prefab)
        {
            ParticleSystem vfx = Instantiate(prefab);
            vfx.transformHandle.SetParent(transformHandle);
            vfx.gameObject.SetActive(false);
            return vfx;
        }

        private void OnGetVFX(ParticleSystem vfx)
        {
            vfx.gameObject.SetActive(true);
        }

        private void OnReleaseVFX(ParticleSystem vfx)
        {
            // Clear remaining particles before returning to pool to prevent artifacts on reuse
            vfx.Clear(true);
            vfx.gameObject.SetActive(false);
        }

        public ParticleSystem PlayVFX(string vfxID, TransformHandle targetTransformHandle)
        {
            return PlayVFX(vfxID, targetTransformHandle.position, targetTransformHandle.rotation,
                targetTransformHandle.localScale);
        }

        // Поза и размер берутся с пустышки. follow - эффект едет за ней, пока играет (частицы с Local Simulation Space на лапе).
        public ParticleSystem PlayVFX(string vfxID, Transform anchor, bool follow, Renderer shapeRenderer = null)
        {
            ParticleSystem vfx = Get(vfxID);
            if (vfx == null) return null;

            if (shapeRenderer != null) ApplyShapeRenderer(vfx, shapeRenderer);

            bool useLossyScale = vfx.main.scalingMode != ParticleSystemScalingMode.Local;
            anchor.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            TransformSetter.SetTransform(vfx.transformHandle, position, rotation,
                useLossyScale ? anchor.lossyScale : anchor.localScale);

            if (follow) followers.Add(new FollowEntry { Vfx = vfx, Anchor = anchor, UseLossyScale = useLossyScale });

            Launch(vfx, vfxID);
            return vfx;
        }

        public ParticleSystem PlayVFX(string vfxID, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            ParticleSystem vfx = Get(vfxID);
            if (vfx == null) return null;

            TransformSetter.SetTransform(vfx.transformHandle, position, rotation, scale);
            Launch(vfx, vfxID);
            return vfx;
        }

        // Частицы на поверхности меша зверя (кровь, глаза): префаб пула не хранит ссылку на объект сцены,
        // поэтому рендерер приходит с пустышки. MeshRenderer у VAT-двойника вместо SkinnedMeshRenderer - тип Shape меняется сам.
        private static void ApplyShapeRenderer(ParticleSystem vfx, Renderer renderer)
        {
            vfx.GetComponentsInChildren(true, shapeScratch);
            foreach (var system in shapeScratch)
            {
                var shape = system.shape;
                if (!shape.enabled) continue;
                if (shape.shapeType != ParticleSystemShapeType.MeshRenderer && shape.shapeType != ParticleSystemShapeType.SkinnedMeshRenderer) continue;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = skinned;
                }
                else if (renderer is MeshRenderer meshRenderer)
                {
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshRenderer = meshRenderer;
                }
            }

            shapeScratch.Clear();
        }

        private ParticleSystem Get(string vfxID)
        {
            if (!vfxPools.ContainsKey(vfxID))
            {
                CreatePool(vfxID);
            }

            if (!vfxPools.TryGetValue(vfxID, out var pool)) return null;

            ParticleSystem vfx = pool.Get();
            vfxIdMap[vfx] = vfxID;
            playIds[vfx] = ++nextPlayId;
            return vfx;
        }

        private void Launch(ParticleSystem vfx, string vfxID)
        {
            vfx.Play(true);

            // Schedule automatic release only for non-looping VFX
            bool isLooping = vfxContainer.IsVFXLooping(vfxID);
            if (!isLooping)
            {
                float duration = vfxContainer.GetVFXDuration(vfxID);
                SetReleaseCoroutine(vfx, StartCoroutine(ReturnToPoolAfterDelay(vfx, duration)));
            }
        }

        /// <summary>Номер текущего запуска экземпляра (0 - он в пуле). Сравнивайте перед StopVFX по сохранённой ссылке.</summary>
        public int GetPlayId(ParticleSystem vfx)
        {
            return vfx != null && playIds.TryGetValue(vfx, out int id) ? id : 0;
        }

        private void LateUpdate()
        {
            for (int i = followers.Count - 1; i >= 0; i--)
            {
                FollowEntry entry = followers[i];
                if (entry.Vfx == null || entry.Anchor == null || !vfxIdMap.ContainsKey(entry.Vfx))
                {
                    followers.RemoveAt(i);
                    continue;
                }

                entry.Anchor.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                TransformSetter.SetTransform(entry.Vfx.transformHandle, position, rotation,
                    entry.UseLossyScale ? entry.Anchor.lossyScale : entry.Anchor.localScale);
            }
        }

        private IEnumerator ReturnToPoolAfterDelay(ParticleSystem vfx, float delay)
        {
            yield return new WaitForSeconds(delay);
            activeVFXCoroutines.Remove(vfx);
            ReleaseVFX(vfx);
        }

        // Stops emitting particles and returns the VFX to its pool
        public void StopVFX(ParticleSystem vfx, bool allowParticlesToFinish = true)
        {
            if (vfx == null || !vfxIdMap.ContainsKey(vfx)) return;

            vfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (allowParticlesToFinish)
            {
                SetReleaseCoroutine(vfx, StartCoroutine(ReleaseAfterParticlesDie(vfx)));
            }
            else
            {
                ReleaseVFX(vfx);
            }
        }

        // Один таймер возврата на экземпляр: новый отменяет старый, иначе экземпляр вернулся бы в пул дважды.
        private void SetReleaseCoroutine(ParticleSystem vfx, Coroutine coroutine)
        {
            if (activeVFXCoroutines.TryGetValue(vfx, out Coroutine previous) && previous != null)
            {
                StopCoroutine(previous);
            }

            activeVFXCoroutines[vfx] = coroutine;
        }

        private IEnumerator ReleaseAfterParticlesDie(ParticleSystem vfx)
        {
            // Wait until all alive particles finish their lifetime
            while (vfx != null && vfx.IsAlive(true))
            {
                yield return null;
            }

            activeVFXCoroutines.Remove(vfx);
            if (vfx != null)
            {
                ReleaseVFX(vfx);
            }
        }

        private void ReleaseVFX(ParticleSystem vfx)
        {
            if (activeVFXCoroutines.TryGetValue(vfx, out Coroutine coroutine))
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }

                activeVFXCoroutines.Remove(vfx);
            }

            playIds.Remove(vfx);
            for (int i = followers.Count - 1; i >= 0; i--)
            {
                if (followers[i].Vfx == vfx) followers.RemoveAt(i);
            }

            if (vfxIdMap.TryGetValue(vfx, out string vfxID))
            {
                vfxIdMap.Remove(vfx);
                if (vfxPools.TryGetValue(vfxID, out var pool))
                {
                    pool.Release(vfx);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var pool in vfxPools.Values)
            {
                pool.Clear();
            }

            vfxPools.Clear();
            activeVFXCoroutines.Clear();
            vfxIdMap.Clear();
            playIds.Clear();
            followers.Clear();
        }
    }
}
