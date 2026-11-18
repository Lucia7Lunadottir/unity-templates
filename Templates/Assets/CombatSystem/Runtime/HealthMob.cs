using UnityEngine;
using PG.HealthSystem;
using UnityEngine.Events;
public class HealthMob : MonoBehaviour, IDamagable, IHealable, IDeath
{
    public float health 
    {
        get
        {
            _health = Mathf.Clamp(_health, 0, _maxHealth);
            return _health;
        }
        set
        {
            _health = value;
            onValueChanged?.Invoke(_health);
        }
    }
    [SerializeField] private float _health = 25;
    [SerializeField] private float _maxHealth = 25;
    public bool useDamage = true;
    // Множитель входящего урона (босс под защитой получает меньше)
    [HideInInspector] public float damageMultiplier = 1f;
    public float maxHealth => _maxHealth;
    [SerializeField] private UnityEvent _damageEvent;
    public event System.Action<float, GameObject> damaged;
    public event System.Action<float> healed;
    public event System.Action<float> onValueChanged;
    public event System.Action dead;
    // Удар пришёл, но урон выключен (useDamage = false) - например, босс держит блок
    public event System.Action<float, GameObject> blocked;
    public UnityEvent _deathEvent;
    // Любой моб получил урон / умер - для общих эффектов боя (CombatFeedback)
    public static event System.Action<HealthMob, float> anyDamaged;
    public static event System.Action<HealthMob> anyDied;

    public void OnDamage(float damage, bool unblockable = false, GameObject damageObject = null)
    {
        if (!enabled)
        {
            return;
        }
        // Ультра-удар проходит сквозь блок
        if (!useDamage && !unblockable)
        {
            blocked?.Invoke(damage, damageObject);
            return;
        }

        health -= damage * damageMultiplier;
        _damageEvent?.Invoke();
        damaged?.Invoke(health, damageObject);
        anyDamaged?.Invoke(this, damage * damageMultiplier);
        if (_health <= 0)
        {
            OnDeath();
        }
    }


    public void OnDeath()
    {
        dead?.Invoke();
        _deathEvent?.Invoke();
        anyDied?.Invoke(this);
        enabled = false;
    }

    public void OnHeal(float value)
    {
        health += value;
        healed?.Invoke(health);
    }
}
