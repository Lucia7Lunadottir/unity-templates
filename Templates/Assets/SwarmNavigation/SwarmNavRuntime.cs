using UnityEngine;

namespace PG.SwarmNavigation
{
    // Внутренняя обслуга для SwarmAgent: ОДИН общий SwarmCrowd (а через него - ОДИН SwarmNav) на сцену, сколько бы
    // SwarmAgent на ней ни было, и ОДИН драйвер с Update()/LateUpdate() вместо Update() на каждом агенте.
    // Для контроля над ёмкостью/моментом создания берите SwarmCrowd/SwarmNav напрямую.
    internal static class SwarmNavRuntime
    {
        // Минимальная ёмкость пула агентов (поднимается SwarmNavSettings.MaxAgents). Поднимите ДО старта первого SwarmAgent.
        public static int MaxAgentsHint = 0;

        // Статики переживают Stop/Play (Domain Reload отключён) - сбрасываем при старте каждого Play.
        private static SwarmCrowd _crowd;
        private static int _refs;
        private static GameObject _driverHost;
        private static TransformHandle _lodFocus = TransformHandle.None;
        private static string _lodFocusTag;
        private static bool _showLodGizmos;
        private static int _focusId = -1;
        private static int _nextFocusSearchFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_crowd != null && !_crowd.IsDisposed) _crowd.Dispose();
            _crowd = null;
            _refs = 0;
            _driverHost = null;
            _lodFocus = TransformHandle.None;
            _focusId = -1;
            _nextFocusSearchFrame = 0;
        }

        // Единственный MonoBehaviour на сцену: Update ставит джобы кадра, LateUpdate их дожидается - между ними
        // воркеры считают параллельно с остальными Update() игры.
        private sealed class SwarmCrowdDriver : MonoBehaviour
        {
            private void Update()
            {
                if (_crowd == null || _crowd.IsDisposed) return;

                UpdateLodFocus();
                _crowd.SetSmoothingCamera(Camera.main);
                _crowd.Schedule(Time.deltaTime);
            }

            private void LateUpdate()
            {
                if (_crowd != null && !_crowd.IsDisposed) _crowd.Complete();
            }

            // Радиусы LOD вокруг фокуса: внутри зелёного - Near, до жёлтого - Mid, дальше - Far.
            private void OnDrawGizmos()
            {
                if (!_showLodGizmos || !_lodFocus.IsValid() || _crowd == null || _crowd.IsDisposed) return;

                SwarmLodSettings lod = _crowd.Nav.LodSettings;
                Vector3 center = _lodFocus.position;
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(center, lod.NearRadius);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(center, lod.MidRadius);
            }
        }

        public static SwarmCrowd AcquireCrowd(SwarmNavSettings settings)
        {
            if (_crowd == null || _crowd.IsDisposed)
            {
                if (settings == null) settings = SwarmNavSettings.CreateDefault();
                int capacity = Mathf.Max(64, Mathf.Max(MaxAgentsHint, settings.MaxAgents));
                _crowd = new SwarmCrowd(settings, capacity);
                _refs = 0;
                _lodFocus = TransformHandle.None;
                _focusId = -1;
                _nextFocusSearchFrame = 0;
                _lodFocusTag = settings.LodFocusTag;
                _showLodGizmos = settings.ShowLodGizmos;
            }

            if (_driverHost == null)
            {
                _driverHost = new GameObject("SwarmCrowd Driver (auto)");
                _driverHost.hideFlags = HideFlags.DontSave;
                _driverHost.AddComponent<SwarmCrowdDriver>();
            }

            _refs++;
            return _crowd;
        }

        public static void ReleaseCrowd()
        {
            if (_crowd == null || _crowd.IsDisposed) return;
            if (--_refs > 0) return;

            _crowd.Dispose();
            _crowd = null;
            if (_driverHost != null) Object.Destroy(_driverHost);
            _driverHost = null;
            _lodFocus = TransformHandle.None;
        }

        private static void UpdateLodFocus()
        {
            if (string.IsNullOrEmpty(_lodFocusTag)) return;

            if (!_lodFocus.IsValid())
            {
                // Поиск раз в 60 кадров, не каждый - пока объекта с тегом нет.
                if (Time.frameCount < _nextFocusSearchFrame) return;
                _nextFocusSearchFrame = Time.frameCount + 60;

                GameObject focus;
                try { focus = GameObject.FindWithTag(_lodFocusTag); }
                catch (UnityException) { _lodFocusTag = null; return; } // тег не объявлен в проекте

                if (focus == null) return;
                _lodFocus = focus.transformHandle;
                _focusId = -1;
            }

            Vector3 position = _lodFocus.position;
            if (_focusId < 0) _focusId = _crowd.Nav.RegisterFocusPoint(position);
            else _crowd.Nav.UpdateFocusPoint(_focusId, position);
        }

        // Автоматический groupKey для движущейся цели (погоня за Transform): привязан к идентичности объекта, а не к
        // позиции, поэтому не меняется, пока агент гонится за тем же Transform. EntityId.GetHashCode() даёт стабильный int
        // (GetInstanceID()/EntityId -> int в этой версии Unity помечены Obsolete(error)).
        public static int GroupKeyForTarget(TransformHandle target) => target.GetHashCode();
    }
}
