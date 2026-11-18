using PG.HealthSystem;
using UnityEngine;

namespace PG.BuffSystem
{
    public class RegenerationBuff : Buff
    {
        [SerializeField] private ParticleSystem _healParticle;
        [SerializeField] private GameObject _healableObject;
        private IHealable _healable;
        
        [SerializeField] private float _heal = 20f;
        
        protected override void OnEnable()
        {
            if (_healable == null)
            {
                _healableObject.TryGetComponent(out _healable);
            }
            _healParticle?.Play();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            _healParticle?.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            base.OnDisable();
        }

        protected override void ApplyBuff()
        {
            _healable?.OnHeal(_heal);
        }
    }
}