using UnityEngine;
using PG.HealthSystem;

public class DeathAnimatorReaction : MonoBehaviour
{
    [SerializeField] private Animator _animator;
    [Tooltip("Необязательно: компонент с IAnimatorBoolTarget (например, VAT-аниматор)")]
    [SerializeField] private MonoBehaviour _vatAnimator;
    [SerializeField] private string _deathParameter = "Death";

    [SerializeField] private GameObject _deathObject;
    public IDeath death;

    private void OnValidate()
    {
        if (_deathObject.GetComponent<IDamagable>() == null)
        {
            _deathObject = null;
            Debug.LogError("DamagableObject haven't interface IDamagable!");
        }
    }
    private void Awake()
    {
        _deathObject.TryGetComponent(out death);
    }
    private void OnEnable()
    {
        death.dead += OnDead;
    }
    private void OnDisable()
    {
        death.dead -= OnDead;
    }
    void OnDead()
    {
        if (_animator != null) _animator.SetBool(_deathParameter, true);
        if (_vatAnimator is IAnimatorBoolTarget target) target.SetBool(_deathParameter, true);
    }
}
