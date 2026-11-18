using System.Collections;
using PG.HealthSystem;
using UnityEngine;

public class AreaParticlesDamage : ParticleDamage
{
    [SerializeField] private float _areaRadius = 1f;
    [SerializeField] private bool _useTimer;
    [SerializeField] private float _areaDuration = 2f;

    protected override void Damage(GameObject other, Vector3 hitPoint)
    {
        if (_useTimer)
        {
            StartCoroutine(DamageTimer(other, hitPoint));
        }
        else
        {
            SubDamage(other, hitPoint);
        }
    }
    IEnumerator DamageTimer(GameObject other, Vector3 hitPoint)
    {
        for (float i = 0; i < _areaDuration; i+= Time.deltaTime)
        {
            yield return null;
            SubDamage(other, hitPoint);
        }
    }
    protected void SubDamage(GameObject other, Vector3 hitPoint)
    {
        Collider[] colliders = Physics.OverlapSphere(hitPoint, _areaRadius);
        
        bool damageApplied = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider.TryGetComponent(out IDamagable damagable))
            {
                // Чужой тег (сама героиня рядом с точкой удара) - пропустить, а не обрывать весь урон
                if (!_tagSet.Contains(collider.tag))
                {
                    continue;
                }
                damagable?.OnDamage(_damage, _unblockable, gameObject);
                damageApplied = true;
            }
        }

        if (damageApplied)
        {
            SpawnDamageText(hitPoint);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _areaRadius);
    }
}