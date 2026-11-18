using System;
using UnityEngine;
using UnityEngine.Events;

namespace PG.HealthSystem
{
    public class DamagableLinker : MonoBehaviour, IDamagable
    {
        [SerializeField] private GameObject _damagableObject;
        public IDamagable damagable;

        [SerializeField] private UnityEvent _damageEvent;
        public event Action<float, GameObject> damaged;

        private void Awake()
        {
            _damagableObject.TryGetComponent(out damagable);
        }

        private bool _damageApplied;

        public void OnDamage(float damage, bool unblockable = false, GameObject damageObject = null)
        {
            if (damagable != null)
            {
                // Щит/кулдаун цели не пропустили удар - без крови и событий
                _damageApplied = false;
                damagable.damaged += OnTargetDamaged;
                damagable.OnDamage(damage, unblockable, damageObject);
                damagable.damaged -= OnTargetDamaged;
                if (!_damageApplied) return;
            }
            _damageEvent?.Invoke();
            damaged?.Invoke(damage, damageObject);
        }

        private void OnTargetDamaged(float damage, GameObject damageObject) => _damageApplied = true;
    }
}
