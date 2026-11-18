using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PG.MenuManagement
{
    public partial class ActiveObjectStateChanger : MonoBehaviour
    {
        [SerializeField] private GameObject[] _enableObjects;
        [SerializeField] private GameObject[] _disableObjectsAferClose;

        // Shared ref counts so several changers can hold the same object
        [AutoStaticsCleanup]
        private static Dictionary<GameObject, int> _enableCounts = new Dictionary<GameObject, int>();
        [AutoStaticsCleanup]
        private static Dictionary<GameObject, int> _disableCounts = new Dictionary<GameObject, int>();

        private void OnEnable()
        {
            foreach (var obj in _enableObjects)
            {
                if (obj == null) continue;

                if (!_enableCounts.ContainsKey(obj)) _enableCounts[obj] = 0;
                _enableCounts[obj]++;

                obj.SetActive(true);
            }

            foreach (var obj in _disableObjectsAferClose)
            {
                if (obj == null) continue;

                if (!_disableCounts.ContainsKey(obj)) _disableCounts[obj] = 0;
                _disableCounts[obj]++;

                obj.SetActive(false);
            }
        }

        private void OnDisable()
        {
            foreach (var obj in _enableObjects)
            {
                if (obj == null) continue;

                if (_enableCounts.ContainsKey(obj))
                {
                    _enableCounts[obj]--;

                    if (_enableCounts[obj] <= 0)
                    {
                        _enableCounts.Remove(obj);
                        DisableNextFrame(obj);
                    }
                }
            }

            foreach (var obj in _disableObjectsAferClose)
            {
                if (obj == null) continue;

                if (_disableCounts.ContainsKey(obj))
                {
                    _disableCounts[obj]--;

                    if (_disableCounts[obj] <= 0)
                    {
                        obj.SetActive(true);
                        _disableCounts.Remove(obj);
                    }
                }
            }
        }

        // Disable one frame later: when the tab changes TabsController may disable the old one before enabling the new one,
        // and a shared object (the book camera) would blink. If someone took it again meanwhile, leave it alone.
        private static async void DisableNextFrame(GameObject obj)
        {
            try { await Awaitable.NextFrameAsync(); }
            catch (System.OperationCanceledException) { return; }
            // Do not touch the editor scene after leaving Play mode
            if (!Application.isPlaying) return;
            if (obj != null && !_enableCounts.ContainsKey(obj)) obj.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitCleanup()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            _enableCounts.Clear();
            _disableCounts.Clear();
        }
    }
}
