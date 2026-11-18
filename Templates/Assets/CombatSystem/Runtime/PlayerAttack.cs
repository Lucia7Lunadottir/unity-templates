using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private InputActionProperty _attackActionProperty;
    [SerializeField] private Animator _animator;
    [SerializeField] private string _attackTrigger = "Attack";
    public event System.Action<GameObject> attacked;
    private void OnEnable()
    {
        _attackActionProperty.action.performed += OnAttack;
    }
    private void OnDisable()
    {
        _attackActionProperty.action.performed -= OnAttack;
    }
    void OnAttack(InputAction.CallbackContext callbackContext)
    {
        _animator.SetTrigger(_attackTrigger);
        attacked?.Invoke(_animator.gameObject);
    }
}
