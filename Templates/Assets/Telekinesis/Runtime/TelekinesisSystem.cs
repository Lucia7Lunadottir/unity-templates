using System;
using PG.Localization;
using PG.MenuManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;
using TMPro;
using Unity.Cinemachine;

// Телекинез: E (нажать) - взять / отпустить, E (удержать) - в сумку, ЛКМ - бросить, колесо - к себе/от себя или поворот (CTRL переключает), средняя кнопка - ось поворота.
// Предмет висит перед Лиэрис (Hold Point) по направлению камеры на расстоянии по своему размеру.
// Двигается и крутится только скоростями Rigidbody, поэтому не проходит сквозь стены и пол.
public class TelekinesisSystem : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string _activeKey = "active";
    [SerializeField] private string _inactiveKey = "inactive";
    [SerializeField] private string _rotateKey = "rotate-object";
    [SerializeField] private string _moveDepthKey = "move-depth-object";
    [Tooltip("Предмет застрял дальше этого от нужной точки (плюс его размер) - отпускается")]
    [SerializeField] private float _dropDistance = 10f;
    [SerializeField] private float _grabRange = 10f;

    [SerializeField] private float _throwForce = 20f;
    [SerializeField] private float _attractSpeed = 10f;
    [Tooltip("Предел скорости переноса, м/с")]
    [SerializeField] private float _maxMoveSpeed = 25f;
    [Tooltip("Градусов за одно деление колеса")]
    [SerializeField] private float _rotationStep = 15f;
    [Tooltip("Как быстро предмет догоняет нужный поворот")]
    [SerializeField] private float _rotationFollow = 12f;
    [SerializeField] private LayerMask _telekinesisMask;
    [SerializeField] private LayerMask _changeSelectLayer;

    private int _baseLayerIndex;
    private int _outlineLayerIndex;

    [Header("Player Avoidance")]
    [SerializeField] private CharacterController _playerController;
    [SerializeField] private float _playerAvoidMargin = 0.05f;

    [Header("Hold Position")]
    [Tooltip("Точка перед Лиэрис, от которой отсчитывается расстояние до предмета")]
    [SerializeField] private Transform _holdPoint;
    [SerializeField] private VisualEffect _visualEffect;
    [Tooltip("Зазор между Hold Point и краем предмета, м")]
    [SerializeField] private float _holdGap = 0.6f;
    [Tooltip("Мелкий предмет не ближе этого к Hold Point - иначе его закрывает голова Лиэрис")]
    [SerializeField] private float _minHoldDistance = 1.8f;

    [Header("Effect Size")]
    [Tooltip("Предмет такого радиуса - эффект как настроен в VFX; меньше/больше - эффект пропорционально")]
    [SerializeField] private float _vfxReferenceRadius = 1f;
    [SerializeField] private Vector2 _vfxScaleRange = new Vector2(0.15f, 2.5f);

    [Header("Camera FOV")]
    [Tooltip("Насколько шире FOV, пока предмет в руках")]
    [SerializeField] private float _holdFovBonus = 5f;
    [Tooltip("Ещё градусов за каждый метр размера предмета")]
    [SerializeField] private float _fovPerMeter = 1.5f;
    [SerializeField] private float _maxHoldFov = 72f;
    [Tooltip("Время плавного перехода FOV, сек")]
    [SerializeField] private float _fovSmoothTime = 0.6f;

    [Header("Depth Offset Settings")]
    [SerializeField] private float _minDepthOffset = -2f; // Минимальный подтяг к себе
    [SerializeField] private float _maxDepthOffset = 10f; // Максимальное удаление
    [SerializeField] private float _depthSensitivity = 0.1f; // Sensitivity для колесика

    [Header("Input")]
    [SerializeField] private InputActionProperty _takeItemAction;   // E (Hold)
    [SerializeField] private InputActionProperty _interactAction;   // E (Grab/Drop)
    [SerializeField] private InputActionProperty _attackAction;     // LMB (Throw)
    [SerializeField] private InputActionProperty _rotateAction;     // MouseScroll (Rotate / Depth)
    [SerializeField] private InputActionProperty _rotateTypeAction; // MiddleClick (Change rotate axis)
    [SerializeField] private InputActionProperty _depthChangerAction; // CTRL

    private Rigidbody _heldObject;
    private bool _lastRigidbodyUsedGravity;
    private RigidbodyInterpolation _lastInterpolation;
    private CollisionDetectionMode _lastCollisionMode;
    private Collider _heldObjectCollider;
    private bool _useDepthChanger = true;
    private bool _tookByHold;
    private float _depthOffset;
    private float _holdRadius;
    private Quaternion _targetRotation;
    private bool _vfxBaseRead;
    private float _vfxBaseRadius, _vfxBaseRate;
    private Vector2 _vfxBaseSize;
    private static readonly int VfxRadius = Shader.PropertyToID("Arc Sphere_sphere_radius");
    private static readonly int VfxSize = Shader.PropertyToID("Size");
    private static readonly int VfxRate = Shader.PropertyToID("Rate");

    public enum RotateType { X, Y, Z }
    [SerializeField] private RotateType _rotateType;
    [SerializeField] private TMP_Text _rotateAxisText;
    [SerializeField] private TMP_Text _rotateObjectText;
    [SerializeField] private TMP_Text _depthModeText;
    [SerializeField] private UIShowHide _telekinesisPanel;

    public Rigidbody heldObject => _heldObject;
    public Transform holdPoint => _holdPoint;
    public System.Action<Rigidbody> grabbed;
    public System.Action<Rigidbody> dropped;
    public event System.Action rotationTypeChanged;

    private Camera _camera;
    private CinemachineCamera _fovCamera;
    private float _baseFov;
    private float _fovVelocity;

    private void Awake()
    {
        _camera = Camera.main;
        _outlineLayerIndex = (int)Mathf.Log(_changeSelectLayer.value, 2);
    }

    private void OnEnable()
    {
        _rotateTypeAction.action.started += OnRotateTypeChange;
        _interactAction.action.canceled  += OnInteract;
        _attackAction.action.performed   += OnThrow;
        _takeItemAction.action.performed += OnTake;
        _depthChangerAction.action.performed += ChangeDepthMode;
    }

    private void OnDisable()
    {
        _rotateTypeAction.action.started -= OnRotateTypeChange;
        _interactAction.action.canceled  -= OnInteract;
        _attackAction.action.performed   -= OnThrow;
        _takeItemAction.action.performed -= OnTake;
        _depthChangerAction.action.performed -= ChangeDepthMode;
        Drop();
        _telekinesisPanel?.Hide();
        // Скрипт выключен (вышли из магии) - FOV возвращает мозг камеры, плавно, без скачка
        if (_fovCamera != null && _camera != null && _camera.TryGetComponent(out CinemachineBrain brain) && brain.isActiveAndEnabled)
            brain.StartCoroutine(ReturnFov(_fovCamera, _baseFov, _fovSmoothTime));
        _fovCamera = null;
        _fovVelocity = 0f;
    }

    void ChangeDepthMode(InputAction.CallbackContext context)
    {
        _useDepthChanger = !_useDepthChanger;
        LocalizeText();
    }

    private void Update()
    {
        UpdateFov();
        if (_heldObject == null) return;

        float input = _rotateAction.action.ReadValue<float>();
        if (!Mathf.Approximately(input, 0f))
        {
            if (_useDepthChanger) ChangeDepthOffset(input);
            else Rotate(input);
        }
        _visualEffect.transform.position = _heldObject.position;
    }

    private void FixedUpdate()
    {
        if (_heldObject != null) MoveObjectToPoint();
    }

    // --- Camera ---

    // Пока держим - шире на бонус (больше предмет - шире), отпустили - обратно к прежнему
    private void UpdateFov()
    {
        if (_fovCamera == null) return;
        float target = _heldObject != null ? Mathf.Min(_baseFov + _holdFovBonus + _fovPerMeter * _holdRadius, Mathf.Max(_maxHoldFov, _baseFov)) : _baseFov;
        _fovCamera.Lens.FieldOfView = Mathf.SmoothDamp(_fovCamera.Lens.FieldOfView, target, ref _fovVelocity, _fovSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (_heldObject == null && Mathf.Abs(_fovCamera.Lens.FieldOfView - _baseFov) < 0.05f)
        {
            _fovCamera.Lens.FieldOfView = _baseFov;
            _fovCamera = null;
        }
    }

    private static System.Collections.IEnumerator ReturnFov(CinemachineCamera camera, float baseFov, float smoothTime)
    {
        float velocity = 0f;
        while (camera != null && Mathf.Abs(camera.Lens.FieldOfView - baseFov) > 0.05f)
        {
            camera.Lens.FieldOfView = Mathf.SmoothDamp(camera.Lens.FieldOfView, baseFov, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            yield return null;
        }
        if (camera != null) camera.Lens.FieldOfView = baseFov;
    }

    // --- Logic ---

    void ChangeDepthOffset(float input)
    {
        _depthOffset = Mathf.Clamp(_depthOffset + Mathf.Sign(input) * _depthSensitivity, _minDepthOffset, _maxDepthOffset);
    }

    void OnRotateTypeChange(InputAction.CallbackContext context)
    {
        switch(_rotateType){
            case RotateType.X:
            _rotateType = RotateType.Y;
            _rotateAxisText.text = "(Y)";
            break;
            case RotateType.Y:
            _rotateType = RotateType.Z;
            _rotateAxisText.text = "(Z)";
            break;
            case RotateType.Z:
            _rotateType = RotateType.X;
            _rotateAxisText.text = "(X)";
            break;
        }
        rotationTypeChanged?.Invoke();
    }

    // Оси от камеры: X - вокруг вертикали, Y - наклон к себе/от себя, Z - наклон вбок
    void Rotate(float input)
    {
        Vector3 axis = _rotateType switch
        {
            RotateType.X => Vector3.up,
            RotateType.Y => _camera.transform.right,
            _ => _camera.transform.forward,
        };
        _targetRotation = Quaternion.AngleAxis(Mathf.Sign(input) * _rotationStep, axis) * _targetRotation;
    }

    // E (отпустили кнопку): держим - отпустить, не держим - взять под прицелом; удержание E уже забрало в сумку - ничего
    private void OnInteract(InputAction.CallbackContext context)
    {
        if (_tookByHold) { _tookByHold = false; return; }
        if (_heldObject != null) Drop();
        else TryGrab();
    }

    private void OnTake(InputAction.CallbackContext context)
    {
        if (_heldObject == null)
        {
            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, _grabRange, _telekinesisMask))
            {
                // Положить в сумку умеет игра (мост TelekinesisBridge), сама система про сумку не знает
                if (TelekinesisBridge.TryTake(hit.collider)) _tookByHold = true;
            }
        }
    }

    private void OnThrow(InputAction.CallbackContext context)
    {
        if (_heldObject != null)
            Throw();
    }

    private void TryGrab()
    {
        Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _grabRange, _telekinesisMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                Grab(hit.rigidbody);
            if (hit.rigidbody == null)
            {
                // У предмета нет физики - её может выдать игра (мост TelekinesisBridge)
                Rigidbody rb = TelekinesisBridge.CreateRigidbody(hit.collider);
                if (rb != null) Grab(rb);
            }
        }
    }

    public void Grab(Rigidbody targetRb)
    {
        _depthOffset = 0f;
        _heldObject = targetRb;
        _lastRigidbodyUsedGravity = targetRb.useGravity;
        _lastInterpolation = targetRb.interpolation;
        _lastCollisionMode = targetRb.collisionDetectionMode;
        _heldObjectCollider = _heldObject.GetComponent<Collider>();
        _holdRadius = GetObjectRadius();
        _targetRotation = targetRb.rotation;
        // Активная камера Cinemachine (в магии - прицельная): её FOV и расширяем
        if (_fovCamera == null && _camera.TryGetComponent(out CinemachineBrain brain) && brain.ActiveVirtualCamera is CinemachineCamera live)
        {
            _fovCamera = live;
            _baseFov = live.Lens.FieldOfView;
        }
        _baseLayerIndex = _heldObject.gameObject.layer;
        _heldObject.gameObject.layer = _outlineLayerIndex;

        _heldObject.useGravity = false;
        _heldObject.linearDamping = 10f;
        _heldObject.angularDamping = 5f;
        // Быстрый перенос без пролёта сквозь тонкие стены
        _heldObject.interpolation = RigidbodyInterpolation.Interpolate;
        _heldObject.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        grabbed?.Invoke(_heldObject);
        FitEffectToObject();
        _visualEffect.Play();

        LocalizeText();
        _telekinesisPanel?.Show();
    }

    private void Drop()
    {
        if (_heldObject == null) return;

        Rigidbody droppedObj = Release();
        dropped?.Invoke(droppedObj);
        TelekinesisBridge.RaiseDropped(droppedObj.gameObject);
    }

    private void Throw()
    {
        Rigidbody rb = _heldObject;
        Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
        const float maxDistance = 100f;
        Vector3 targetPoint = ray.GetPoint(maxDistance);
        float closestDistance = maxDistance;

        foreach (RaycastHit hit in Physics.RaycastAll(ray, maxDistance, -1, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody == rb || hit.distance > closestDistance)
                continue;

            closestDistance = hit.distance;
            targetPoint = hit.point;
        }

        Vector3 throwDirection = (targetPoint - rb.position).normalized;

        Drop();
        rb.AddForce(throwDirection * _throwForce, ForceMode.VelocityChange);
    }

    // Сфера, частицы и их количество - по размеру предмета (тарелка не тонет в искрах, шкаф не в точке)
    private void FitEffectToObject()
    {
        if (!_vfxBaseRead)
        {
            _vfxBaseRead = true;
            _vfxBaseRadius = _visualEffect.HasFloat(VfxRadius) ? _visualEffect.GetFloat(VfxRadius) : 0f;
            _vfxBaseRate = _visualEffect.HasFloat(VfxRate) ? _visualEffect.GetFloat(VfxRate) : 0f;
            _vfxBaseSize = _visualEffect.HasVector2(VfxSize) ? _visualEffect.GetVector2(VfxSize) : Vector2.zero;
        }
        float k = Mathf.Clamp(_holdRadius / Mathf.Max(0.01f, _vfxReferenceRadius), _vfxScaleRange.x, _vfxScaleRange.y);
        if (_visualEffect.HasFloat(VfxRadius)) _visualEffect.SetFloat(VfxRadius, _vfxBaseRadius * k);
        // Частицы мельче не линейно, иначе у мелочи пропадают совсем
        if (_visualEffect.HasVector2(VfxSize)) _visualEffect.SetVector2(VfxSize, _vfxBaseSize * Mathf.Sqrt(k));
        if (_visualEffect.HasFloat(VfxRate)) _visualEffect.SetFloat(VfxRate, _vfxBaseRate * k);
    }

    // Возвращает Rigidbody его настройки и прячет эффект
    private Rigidbody Release()
    {
        Rigidbody rb = _heldObject;
        rb.gameObject.layer = _baseLayerIndex;
        rb.useGravity = _lastRigidbodyUsedGravity;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.interpolation = _lastInterpolation;
        rb.collisionDetectionMode = _lastCollisionMode;

        _heldObject = null;
        _heldObjectCollider = null;
        _depthOffset = 0f;
        _visualEffect.Stop();
        _telekinesisPanel?.Hide();
        return rb;
    }

    void LocalizeText()
    {
        _depthModeText.text = "("+ LocalizationSystem.instance.GetLocalizedValue(_useDepthChanger ? _activeKey : _inactiveKey, _depthModeText.text) +")";
        _rotateObjectText.text = LocalizationSystem.instance.GetLocalizedValue(_useDepthChanger ? _moveDepthKey : _rotateKey, _rotateObjectText.text);
    }

    // Только скорости: в стену или пол предмет упирается, а не проходит сквозь
    private void MoveObjectToPoint()
    {
        float distance = Mathf.Max(Mathf.Max(_minHoldDistance, _holdRadius + _holdGap) + _depthOffset, _holdRadius + 0.3f);
        Vector3 targetPosition = _holdPoint.position + _camera.transform.forward * distance;

        if (_playerController != null)
            targetPosition = ResolveAgainstPlayer(targetPosition, _holdRadius);

        Vector3 directionToPoint = targetPosition - _heldObject.position;
        if (directionToPoint.magnitude > _dropDistance + _holdRadius)
        {
            Drop();
            return;
        }

        _heldObject.linearVelocity = Vector3.ClampMagnitude(directionToPoint * _attractSpeed, _maxMoveSpeed);

        Quaternion delta = _targetRotation * Quaternion.Inverse(_heldObject.rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        _heldObject.angularVelocity = Mathf.Abs(angle) < 0.01f || float.IsInfinity(axis.x)
            ? Vector3.zero
            : axis * (angle * Mathf.Deg2Rad * _rotationFollow);
    }

    // Двигает точку наружу от капсулы игрока, если она оказалась внутри требуемого зазора
    private Vector3 ResolveAgainstPlayer(Vector3 point, float objectRadius)
    {
        Vector3 closest = GetClosestPointOnCapsule(point);
        Vector3 offset = point - closest;
        float dist = offset.magnitude;
        float requiredDist = _playerController.radius + objectRadius + _playerAvoidMargin;

        if (dist >= requiredDist)
            return point; // и так снаружи, трогать не нужно

        Vector3 pushDir = dist > 0.0001f
            ? offset / dist
            : Vector3.up; // если точка ровно в центре капсулы — просто выталкиваем вверх как fallback

        return closest + pushDir * requiredDist;
    }

    // Ближайшая точка на оси капсулы CharacterController, затем проекция наружу на requiredDist делается в ResolveAgainstPlayer
    private Vector3 GetClosestPointOnCapsule(Vector3 point)
    {
        Transform t = _playerController.transform;
        Vector3 center = t.position + _playerController.center;

        float halfSegment = Mathf.Max(0f, (_playerController.height * 0.5f) - _playerController.radius);
        Vector3 up = t.up;

        Vector3 top = center + up * halfSegment;
        Vector3 bottom = center - up * halfSegment;

        // ближайшая точка на отрезке (оси капсулы) к point
        Vector3 segment = top - bottom;
        float segLenSqr = segment.sqrMagnitude;
        float tParam = segLenSqr > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(point - bottom, segment) / segLenSqr)
            : 0f;

        return bottom + segment * tParam;
    }

    // Радиус по всем коллайдерам предмета (у мебели их может быть несколько)
    private float GetObjectRadius()
    {
        var colliders = _heldObject.GetComponentsInChildren<Collider>();
        Bounds? b = null;
        foreach (var c in colliders)
        {
            if (c.isTrigger) continue;
            if (b == null) b = c.bounds;
            else { var bb = b.Value; bb.Encapsulate(c.bounds); b = bb; }
        }
        return b?.extents.magnitude ?? 0.3f;
    }
}
