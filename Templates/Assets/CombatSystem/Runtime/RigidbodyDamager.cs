using PG.HealthSystem;
using UnityEngine;

public class RigidbodyDamager : MonoBehaviour
{
    [SerializeField] private float _targetVelocity = 10f;
    [SerializeField] private float _damage = 10f;
    [SerializeField] private Rigidbody _body;
    [SerializeField] private string[] _ignoringTags;
    private void Awake()
    {
        if(_body == null)
        {
            _body = GetComponent<Rigidbody>();
        }
    }
    [ContextMenu("Get Cache")]
    void GetCache()
    {
        _body = GetComponent<Rigidbody>();
    }
    private void OnCollisionEnter(Collision collision)
    {
        for (int i = 0; i < _ignoringTags.Length; i++)
        {
            if (!collision.collider.CompareTag(_ignoringTags[i]))
            {
                if (collision.collider.TryGetComponent(out IDamagable damagable))
                {
                    if (_body.linearVelocity.magnitude >= _targetVelocity)
                    {
                        damagable?.OnDamage(_damage * _body.linearVelocity.magnitude / _targetVelocity, false, gameObject);
                    }
                }
            }
        }
    }
}
