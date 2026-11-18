using PG.BuffSystem;
using PG.HealthSystem;
using UnityEngine;

public class ParticleBuffDamage : ParticleDamage
{
    [SerializeField] private BuffType _targetBuffType;
    [SerializeField] private float _buffDuration = 4f;
    

    protected override void Damage(GameObject other, Vector3 hitPoint)
    {
        if (!_tagSet.Contains(other.tag))
        {
            return;
        }
        if (!other.TryGetComponent(out IDamagable damagable) || !ApplyDamage(damagable)) return;
        SpawnDamageText(hitPoint);
        if (other.TryGetComponent(out BuffLinker linker))
        {
            linker.buffManager?.AddBuff(CheckBuffType.GetBuffType(_targetBuffType), _buffDuration);
        }
    }
}