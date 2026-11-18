using PG.InteractSystem;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlaceablePickupManager : MonoBehaviour
{
    public static PlaceablePickupManager instance;

    [SerializeField] private InputActionProperty _interactAction;
    [SerializeField] private float _holdDuration = 0.5f;

    [Header("Progress UI")]
    [Tooltip("The same shared icon Image used for all interactions. Show/hide handled by InteractObjectDisplay as usual.")]
    [SerializeField] private Image _progressImage;
    [Tooltip("A separate border/ring Image behind the icon. Only fillAmount is controlled here.")]
    [SerializeField] private Image _fillImage;

    private readonly List<PlaceableWorldObject> _inRange = new();
    private float _holdTimer;
    private bool _isTracking;

    private void Awake() => instance = this;

    private void OnEnable()
    {
        _interactAction.action.started  += OnInteractStarted;
        _interactAction.action.canceled += OnInteractCanceled;
    }

    private void OnDisable()
    {
        _interactAction.action.started  -= OnInteractStarted;
        _interactAction.action.canceled -= OnInteractCanceled;
        _isTracking = false;
        _holdTimer = 0f;
        SetFill(0f);
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        if (_inRange.Count == 0) return;
        _isTracking = true;
        _holdTimer = 0f;
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        _isTracking = false;
        _holdTimer = 0f;
        SetFill(0f);
    }

    private void Update()
    {
        if (!_isTracking || _inRange.Count == 0) return;

        _holdTimer += Time.deltaTime;
        SetFill(_holdTimer / _holdDuration);

        if (_holdTimer >= _holdDuration)
        {
            _isTracking = false;
            _holdTimer = 0f;
            SetFill(0f);
            PickupNearest();
        }
    }

    private void PickupNearest()
    {
        _inRange[_inRange.Count - 1].Pickup();
    }

    public void Register(PlaceableWorldObject pwo)
    {
        if (_inRange.Contains(pwo)) return;
        _inRange.Add(pwo);
        if (_inRange.Count == 1)
        {
            SetFill(0f);
            IInteractVisible.visibleInteracted?.Invoke(true);
        }
    }

    public void Unregister(PlaceableWorldObject pwo)
    {
        _inRange.Remove(pwo);
        if (_inRange.Count == 0)
        {
            _isTracking = false;
            _holdTimer = 0f;
            SetFill(0f);
            IInteractVisible.visibleInteracted?.Invoke(false);
        }
    }

    private void SetFill(float t)
    {
        if (_fillImage != null)
            _fillImage.fillAmount = t;
    }
}
