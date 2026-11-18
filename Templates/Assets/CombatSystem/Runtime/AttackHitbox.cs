using System;
using PG.HealthSystem;
using UnityEngine;

[Serializable]
public class AttackHitbox
{
    [SerializeField] private float _damage = 20f;
    [SerializeField] private Collider[] _colliders;

    public bool hasHit { get; private set; }
    public bool isConfigured => _colliders != null && _colliders.Length > 0;
    public Collider[] colliders => _colliders;

    public void Setup(Collider[] colliders)
    {
        _colliders = colliders;
    }

    public void Begin()
    {
        hasHit = false;
    }

    // Урон наносится один раз за атаку
    public bool TryHit(CharacterController target, IDamagable damagable, GameObject source)
    {
        if (hasHit || !IsTouching(target)) return false;

        hasHit = true;
        damagable.OnDamage(_damage, false, source);
        return true;
    }

    private bool IsTouching(CharacterController target)
    {
        float radiusSqr = target.radius * target.radius;
        float halfSegment = Mathf.Max(0f, target.height * 0.5f - target.radius);
        Vector3 center = target.bounds.center;

        // Проверяем низ, середину и верх капсулы игрока
        for (int s = -1; s <= 1; s++)
        {
            Vector3 point = center + Vector3.up * (halfSegment * s);
            foreach (Collider collider in _colliders)
            {
                if (collider == null || !collider.enabled) continue;
                if ((collider.ClosestPoint(point) - point).sqrMagnitude <= radiusSqr) return true;
            }
        }
        return false;
    }
}
