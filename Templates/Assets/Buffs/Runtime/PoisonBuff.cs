using PG.HealthSystem;
using UnityEngine;

namespace PG.BuffSystem
{
    public class PoisonBuff : Buff
    {
        [SerializeField] private ParticleSystem _poisonParticle;
        [SerializeField] private GameObject _damagableObject;
        private IDamagable _damagable;
        
        [SerializeField] private float _damage = 20f;
        
        protected override void OnEnable()
        {
            if (_damagable == null)
            {
                _damagableObject.TryGetComponent(out _damagable);
            }
            _poisonParticle?.Play();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            _poisonParticle?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            base.OnDisable();
        }

        protected override void ApplyBuff()
        {
            _damagable?.OnDamage(_damage,true);
        }
    }
}