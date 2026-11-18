using PG.HealthSystem;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDodge : MonoBehaviour
{
    [Header("Cache")]
    [SerializeField] private InputActionProperty _dodgeActionProperty;
    [SerializeField] private Health _health;
    [SerializeField] private Animator _animator;
    [SerializeField] private string _attackTrigger = "Dodge";

    [Header("Data")]
    [SerializeField] private float _cooldown = 0.25f;

    [Header("Stamina")]
    [Tooltip("Any component that implements IStaminaSource (stamina of the player); empty - dodging costs nothing")]
    [SerializeField] private MonoBehaviour _staminaController;
    [SerializeField] private float _decreaseStaminaValue = 10f;
    [SerializeField] private float _staminaTickRate = 1f;          // How many seconds does stamina take to burn?

    private IStaminaSource stamina => _staminaController as IStaminaSource;

    private bool _isDodgingCooldown;
    private bool _isDodging;
    private Coroutine _barrierRoutine;
    public event System.Action<GameObject, bool> dodged;

    private void OnEnable()
    {
        _dodgeActionProperty.action.started += OnDodge;
        _dodgeActionProperty.action.canceled += OnDodgeEnd;
    }
    private void OnDisable()
    {
        _dodgeActionProperty.action.started -= OnDodge;
        _dodgeActionProperty.action.canceled -= OnDodgeEnd;
        BarrierEnd();
    }
    void OnDodge(InputAction.CallbackContext callbackContext)
    {
        if (_isDodgingCooldown || _isDodging || (stamina != null && stamina.isResting))
        {
            return;
        }
        _health.shielded = true;
        _animator.SetBool(_attackTrigger, true);
        dodged?.Invoke(_animator.gameObject, true);
        // Один цикл расхода выносливости на щит, даже при быстром повторном нажатии
        if (_barrierRoutine != null) StopCoroutine(_barrierRoutine);
        _barrierRoutine = StartCoroutine(BarrierUpdate());
    }
    IEnumerator BarrierUpdate()
    {
        _isDodging = true;
        while (_isDodging && !(stamina != null && stamina.isResting))
        {
            yield return new WaitForSeconds(_staminaTickRate);
            stamina?.Spend(_decreaseStaminaValue);
        }
        if (stamina != null && stamina.isResting)
        {
            BarrierEnd();
        }
        _isDodging = false;
        _barrierRoutine = null;
    }
    void OnDodgeEnd(InputAction.CallbackContext callbackContext)
    {
        BarrierEnd();
        StartCoroutine(OnCooldown());
    }
    void BarrierEnd()
    {
        _animator.SetBool(_attackTrigger, false);
        _health.shielded = false;
        dodged?.Invoke(_animator.gameObject, false);
    }
    IEnumerator OnCooldown()
    {
        _isDodging = true;
        yield return new WaitForSeconds(_cooldown);
        _isDodging = false;
    }
}
