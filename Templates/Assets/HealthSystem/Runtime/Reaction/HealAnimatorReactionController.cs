using UnityEngine;

namespace PG.HealthSystem
{
    public class HealAnimatorReactionController : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private string _healTrigger = "Heal";

        [SerializeField] private GameObject _healableObject;
        public IHealable healable;
        private void Awake()
        {
            _healableObject.TryGetComponent(out healable);
        }
        private void OnEnable()
        {
            healable.healed += OnHeal;
        }
        private void OnDisable()
        {
            healable.healed -= OnHeal;
        }
        void OnHeal(float value)
        {
            _animator?.SetTrigger(_healTrigger);
        }
    }
}
