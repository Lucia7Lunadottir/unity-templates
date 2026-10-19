using UnityEngine;

namespace PG.SwarmNavigation
{
    // "Как NavMeshAgent" компонент: киньте на GameObject, зовите SetDestination. Навигация берётся из NavMesh Unity
    // (запеките NavMeshSurface как обычно), отдельного бейка нет. SwarmNavSettings назначать необязательно.
    //
    // SwarmAgent САМ НИЧЕГО НЕ СЧИТАЕТ, НЕ ДВИГАЕТ И НЕ ИМЕЕТ СВОЕГО Update(): это тонкая обёртка над ОДНИМ общим
    // SwarmCrowd (через SwarmNavRuntime). Весь кадр для ВСЕХ агентов сцены идёт Burst-джобами, которые ставит один общий
    // драйвер. N отдельных Update() на тысячах агентов стоят реальных денег даже с пустым телом, поэтому их нет.
    //
    // Переходы между NavMeshSurface и NavMeshLink обрабатываются самим NavMesh: цель на другом острове или за связью
    // достижима так же, как у NavMeshAgent.
    [DisallowMultipleComponent]
    public class SwarmAgent : MonoBehaviour
    {
        [Header("SwarmNav (необязательно - без ассета берутся значения по умолчанию)")]
        [SerializeField] private SwarmNavSettings settings;

        [Header("Movement")]
        [SerializeField] private float _speed = 3.5f;
        [SerializeField] private float _stoppingDistance = 0.5f;
        [SerializeField] private float _angularSpeed = 360f;
        [Tooltip("Как NavMeshAgent.acceleration: разгон и торможение у цели, м/с². 0 - скорость сразу полная.")]
        [SerializeField] private float _acceleration = 8f;
        [Tooltip("Как NavMeshAgent.radius: агенты расходятся на сумму радиусов. 0 - AvoidanceRadius / 2 из SwarmNavSettings.")]
        [SerializeField] private float _radius;
        [Tooltip("Как NavMeshAgent.baseOffset: смещение Y модели над поверхностью NavMesh. Для куба с пивотом в центре - половина его высоты.")]
        [SerializeField] private float _baseOffset;
        [SerializeField] private bool _isStopped;

        // Как у NavMeshAgent.updatePosition/updateRotation: false - Transform/поворот двигает кто-то другой снаружи
        // (типичный случай - RootMotion из Animator, см. SyncPosition/Move ниже).
        [SerializeField] private bool _updatePosition = true;
        [SerializeField] private bool _updateRotation = true;

        // Свойства форвардятся в общий SwarmCrowd по хэндлу: O(1), без опроса в Update().
        public float speed
        {
            get => _speed;
            set { _speed = value; if (_initialized) _crowd.SetSpeed(_handle, value); }
        }

        public float stoppingDistance
        {
            get => _stoppingDistance;
            set { _stoppingDistance = value; if (_initialized) _crowd.SetStoppingDistance(_handle, value); }
        }

        public float acceleration
        {
            get => _acceleration;
            set { _acceleration = value; if (_initialized) _crowd.SetAcceleration(_handle, value); }
        }

        public float radius
        {
            get => _radius;
            set { _radius = value; if (_initialized) _crowd.SetRadius(_handle, value); }
        }

        public float baseOffset
        {
            get => _baseOffset;
            set { _baseOffset = value; if (_initialized) _crowd.SetBaseOffset(_handle, value); }
        }

        public float angularSpeed
        {
            get => _angularSpeed;
            set { _angularSpeed = value; if (_initialized) _crowd.SetAngularSpeed(_handle, value); }
        }

        public bool isStopped
        {
            get => _isStopped;
            set { _isStopped = value; if (_initialized) _crowd.SetStopped(_handle, value); }
        }

        public bool updatePosition
        {
            get => _updatePosition;
            set { _updatePosition = value; if (_initialized) _crowd.SetUpdatePosition(_handle, value); }
        }

        public bool updateRotation
        {
            get => _updateRotation;
            set { _updateRotation = value; if (_initialized) _crowd.SetUpdateRotation(_handle, value); }
        }

        public float remainingDistance => _initialized ? _crowd.GetRemainingDistance(_handle) : float.PositiveInfinity;
        public bool hasDestination => _initialized && _crowd.HasDestination(_handle);
        public SwarmPathStatus pathStatus => _initialized ? _crowd.GetPathStatus(_handle) : SwarmPathStatus.None;
        public bool pathPending => pathStatus == SwarmPathStatus.Pending;
        public Vector3 velocity => _initialized ? _crowd.GetVelocityDirection(_handle) * _speed : Vector3.zero;

        // Аналог NavMeshAgent.isOnNavMesh - защитная проверка перед SetDestination/ResetPath.
        public bool isOnNavMesh
        {
            get
            {
                EnsureInitialized();
                return _initialized && _crowd.IsOnNavMesh(_handle);
            }
        }

        private SwarmCrowd _crowd;
        private SwarmNavHandle _handle;
        private bool _initialized;

        // Awake(), не Start(): Start() откладывается до первого Update() - спавнер, который сразу вызывает SetDestination,
        // не успел бы. EnsureInitialized() на публичных входах - защита для экзотичных путей вызова.
        private void Awake() => EnsureInitialized();

        // Для кода, который создаёт SwarmAgent рантаймом (AddComponent): поле settings некому проставить через инспектор.
        public void Configure(SwarmNavSettings navSettings) => settings = navSettings;

        private void EnsureInitialized()
        {
            if (_initialized) return;

            _crowd = SwarmNavRuntime.AcquireCrowd(settings);
            _handle = _crowd.AddAgent(gameObject, _speed, _stoppingDistance, _angularSpeed, _updatePosition, _updateRotation, _baseOffset);
            if (_isStopped) _crowd.SetStopped(_handle, true);
            _crowd.SetAcceleration(_handle, _acceleration);
            if (_radius > 0f) _crowd.SetRadius(_handle, _radius);
            _initialized = true;
        }

        // Неподвижная точка. Для движущейся цели (погоня) - перегрузка с TransformHandle.
        public void SetDestination(Vector3 destination)
        {
            EnsureInitialized();
            _crowd.SetDestination(_handle, destination);
        }

        // Движущаяся цель (погоня за игроком/врагом): позиция читается каждый кадр, повторно вызывать не нужно.
        // Путь агента пересчитывается, когда цель заметно сместилась (SwarmNavSettings.ChaseRepath*).
        public void SetDestination(TransformHandle target)
        {
            EnsureInitialized();
            _crowd.SetDestination(_handle, target);
        }

        // То же для обычного Transform (как NavMeshAgent): читается его TransformHandle.
        public void SetDestination(Transform target)
        {
            if (target != null) SetDestination(target.transformHandle);
        }

        public void Stop() => isStopped = true;

        // Аналог NavMeshAgent.ResetPath(): снимает цель совсем (hasDestination = false), в отличие от Stop().
        public void ResetPath()
        {
            EnsureInitialized();
            _crowd.ResetPath(_handle);
        }

        // Телепорт - сразу и Transform, и позиция внутри SwarmNav (с привязкой к NavMesh).
        public void Warp(Vector3 position)
        {
            EnsureInitialized();
            _crowd.Warp(_handle, position);
        }

        // Аналог "agent.nextPosition = selfHandle.position" - сообщить SwarmNav, где агент реально сейчас,
        // перед тем как применить внешнее смещение через Move (нужно вместе с updatePosition = false).
        public void SyncPosition(Vector3 actualPosition)
        {
            EnsureInitialized();
            _crowd.SyncPosition(_handle, actualPosition);
        }

        // Аналог NavMeshAgent.Move(offset) при updatePosition = false: смещение считает вызывающий код (обычно
        // Animator.deltaPosition), SwarmNav прикладывает его с учётом поверхности NavMesh и возвращает итоговую позицию.
        public Vector3 Move(Vector3 offset)
        {
            EnsureInitialized();
            return _crowd.Move(_handle, offset);
        }

        // Как выключенный NavMeshAgent: агент стоит и не трогает Transform (им владеет, например, Rigidbody при отбрасывании).
        private void OnDisable()
        {
            if (!_initialized || _crowd == null || _crowd.IsDisposed) return;
            _crowd.SetStopped(_handle, true);
            _crowd.SetUpdatePosition(_handle, false);
            _crowd.SetUpdateRotation(_handle, false);
        }

        private void OnEnable()
        {
            if (!_initialized || _crowd == null || _crowd.IsDisposed) return;
            _crowd.SetStopped(_handle, _isStopped);
            _crowd.SetUpdatePosition(_handle, _updatePosition);
            _crowd.SetUpdateRotation(_handle, _updateRotation);
        }

        private void OnDestroy()
        {
            if (!_initialized) return;

            _initialized = false;
            if (_crowd == null || _crowd.IsDisposed) return;

            _crowd.RemoveAgent(_handle);
            SwarmNavRuntime.ReleaseCrowd();
        }
    }
}
