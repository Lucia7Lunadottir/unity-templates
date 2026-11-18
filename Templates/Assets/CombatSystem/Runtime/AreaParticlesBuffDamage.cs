using System.Collections;
using PG.BuffSystem;
using PG.HealthSystem;
using UnityEngine;

public class AreaParticlesBuffDamage : ParticleDamage
{
    [SerializeField] private float _areaRadius = 1f;
    [SerializeField] private bool _useTimer;
    [SerializeField] private float _areaDuration = 2f;
    [SerializeField] private BuffType _targetBuffType;
    [SerializeField] private float _buffDuration = 3f;
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

    void SubDamage(GameObject other, Vector3 hitPoint)
    {
        Collider[] colliders = Physics.OverlapSphere(other.transformHandle.position, _areaRadius);
        Debug.Log($"Damage! | {other.gameObject} = {_damage}");
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            
            if (collider.TryGetComponent(out IDamagable damagable) && collider.TryGetComponent(out BuffLinker linker))
            {
                if (!_tagSet.Contains(collider.tag))
                {
                    return;
                }
                if (!ApplyDamage(damagable)) continue;
                linker.buffManager?.AddBuff(CheckBuffType.GetBuffType(_targetBuffType), _buffDuration);
                //Debug.Log($"Damage! {damagable} | {other.gameObject} = {_damage}");
                SpawnDamageText(collider.transformHandle.position);
            }
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


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _areaRadius);
    }
}