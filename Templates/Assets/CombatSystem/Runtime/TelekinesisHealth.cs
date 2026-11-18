using PG.HealthSystem;
using System;
using UnityEngine;
using UnityEngine.Events;

public class TelekinesisHealth : MonoBehaviour, IDamagable, IDeath
{
    [field:SerializeField] public int health { get; set; } = 30;
    [SerializeField] private UnityEvent _damageEvent;
    public event Action<float, GameObject> damaged;
    public event Action dead;

    [SerializeField] private UnityEvent _deathEvent;
    

    public void OnDamage(float damage, bool unblockable = false, GameObject damageObject = null)
    {
        if (health <= 0) return;

        if (damageObject == null || !damageObject.TryGetComponent(out RigidbodyDamager damager))
        {
            // ���� ������ �� RigidbodyDamager, ���������� ����
            return;
        }
        health -= (int)damage;
        damaged?.Invoke(damage, damageObject);
        _damageEvent?.Invoke();
        if (health <= 0)
        {
            _deathEvent?.Invoke();
            OnDeath();
            dead?.Invoke();
        }
    }

    public void OnDeath()
    {
    }
}
