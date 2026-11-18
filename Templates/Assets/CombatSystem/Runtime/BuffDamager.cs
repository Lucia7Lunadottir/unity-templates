using PG.BuffSystem;
using PG.HealthSystem;
using UnityEngine;

public class BuffDamager : MonoBehaviour
{
    [TagDropdown]
    [SerializeField] private string[] _tags = new[] { "Enemy" };
    [SerializeField] private float _damage = 5f;
    [SerializeField] private BuffType _buffType;
    
    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log($"[Damager] touched: {other.gameObject.name} (Tag: {other.tag})", this);
        for (int i = 0; i < _tags.Length; i++)
        {
            if (other.CompareTag(_tags[i]))
            {
                // Удар ушёл в щит/блок - без дебаффа
                if (other.TryGetComponent(out IDamagable damagable) && !ParticleDamage.ApplyDamage(damagable, _damage, false, gameObject))
                    return;

                if (other.TryGetComponent(out BuffManager buffManager))
                {
                    buffManager?.AddBuff(CheckBuffType.GetBuffType(_buffType));
                }
                if (other.TryGetComponent(out BuffLinker buffLinker))
                {
                    buffLinker.buffManager?.AddBuff(CheckBuffType.GetBuffType(_buffType));
                }
            }
        }
    }
}