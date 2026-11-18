using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

// Супер атака: зажать атаку, подержать, отпустить. Обычный удар на нажатие остаётся за PlayerAttack.
// Что играть на земле и в воздухе, решают состояния в Animator по триггеру.
public class PlayerChargeAttack : MonoBehaviour
{
    [Tooltip("Кнопка атаки (та же, что у PlayerAttack).")]
    [SerializeField] private InputActionProperty _attackActionProperty;
    [Tooltip("Animator героини.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Триггер в Animator, который запускает супер атаку при отпускании.")]
    [SerializeField] private string _superAttackTrigger = "Super Attack";
    [Tooltip("Bool в Animator: true, пока атака заряжена и кнопка зажата (поза/эффект заряда). Пусто - не ставить.")]
    [SerializeField] private string _chargedBool;
    [Tooltip("Сколько держать кнопку, чтобы зарядить супер атаку, секунды реального времени.")]
    [SerializeField, Min(0.05f)] private float _holdTime = 0.5f;
    [Tooltip("Триггер живёт столько секунд и сбрасывается, если его не подхватило ни одно состояние (иначе сработает позже).")]
    [SerializeField, Min(0.02f)] private float _triggerBufferTime = 0.15f;
    [SerializeField] private VisualEffect _readyParticles;

    // Атака зарядилась (кнопка ещё зажата) - для эффектов и звука
    public event System.Action charged;
    // Супер атака запущена - GameObject героини, как attacked у PlayerAttack
    public event System.Action<GameObject> superAttacked;
    public bool isCharged { get; private set; }

    private bool _isHeld;
    private float _pressedAt;
    private int _triggerHash, _chargedHash;
    private float _resetTriggerAt = -1f;

    private void Awake()
    {
        _triggerHash = Animator.StringToHash(_superAttackTrigger);
        _chargedHash = string.IsNullOrEmpty(_chargedBool) ? 0 : Animator.StringToHash(_chargedBool);
    }

    private void OnEnable()
    {
        _attackActionProperty.action.performed += OnPressed;
        _attackActionProperty.action.canceled += OnReleased;
    }

    private void OnDisable()
    {
        _attackActionProperty.action.performed -= OnPressed;
        _attackActionProperty.action.canceled -= OnReleased;
        ResetCharge();
    }

    private void Update()
    {
        if (_resetTriggerAt > 0f && Time.unscaledTime >= _resetTriggerAt)
        {
            _resetTriggerAt = -1f;
            if (_animator != null) _animator.ResetTrigger(_triggerHash);
        }
        if (!_isHeld || isCharged || Time.unscaledTime - _pressedAt < _holdTime) return;
        isCharged = true;
        SetChargedBool(true);
        charged?.Invoke();
    }

    private void OnPressed(InputAction.CallbackContext context)
    {
        _isHeld = true;
        _pressedAt = Time.unscaledTime;
    }

    private void OnReleased(InputAction.CallbackContext context)
    {
        bool fire = isCharged;
        ResetCharge();
        if (!fire || _animator == null) return;

        _animator.SetTrigger(_triggerHash);
        _resetTriggerAt = Time.unscaledTime + _triggerBufferTime;
        superAttacked?.Invoke(_animator.gameObject);
    }

    private void ResetCharge()
    {
        _isHeld = false;
        if (isCharged) SetChargedBool(false);
        isCharged = false;
    }

    private void SetChargedBool(bool value)
    {
        if (_chargedHash != 0 && _animator != null) 
        {
            _animator.SetBool(_chargedHash, value);
        
        }
        if (value)
        {
            _readyParticles?.Play();
        }
        else
        {
            _readyParticles?.Stop();
        }
    }
}
