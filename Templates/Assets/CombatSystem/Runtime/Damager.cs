using PG.HealthSystem;
using UnityEngine;

public class Damager : MonoBehaviour
{
    [TagDropdown]
    [SerializeField] private string[] _tags = new[] { "Enemy" };
    [SerializeField] private float _damage = 5f;
    public float damage {
        get => _damage;
        set{
            _damage = value;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log($"[Damager] touched: {other.gameObject.name} (Tag: {other.tag})", this);
        for (int i = 0; i < _tags.Length; i++)
        {
            if (other.CompareTag(_tags[i]))
            {
                if (other.TryGetComponent(out IDamagable damagable))
                {
                    damagable.OnDamage(damage, false, gameObject);
                }
            }
        }
    }
}
