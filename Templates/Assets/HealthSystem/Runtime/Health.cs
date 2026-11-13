using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PG.HealthSystem
{
    public class Health : MonoBehaviour, IDamagable, IHealable
    {
        [Header("UI")]
        [SerializeField] private Slider _healthSlider;
        [SerializeField] private RectTransform _hitRect;
        private Coroutine _hitCoroutine;
        [SerializeField] private float _targetTransitionDuration = 0.1f;

        [Header("Cooldown")]
        [SerializeField] private bool _cooldownEnable;
        [SerializeField] private float _damageCooldown = 0.2f;
        private Coroutine _damageCooldownCoroutine;

        [Space(10)]
        public bool useDamage;
        // Щит/блок игрока: отдельно от useDamage, который переключает перезарядка урона
        [NonSerialized] public bool shielded;
        [SerializeField] private UnityEvent _damageEvent;
        [SerializeField] private GameObject _deathObject;

        public event Action<float, GameObject> damaged;
        public event Action<float> healed;
        // Удар пришёл в щит (shielded): урон и источник
        public event Action<float, GameObject> blocked;

        public float value => _healthSlider != null ? _healthSlider.value : 0f;
        public float maxValue => _healthSlider != null ? _healthSlider.maxValue : 0f;
        [SerializeField] private float _healTime = 0.25f;
        private Coroutine _healCoroutine;
        private WaitForSeconds _waitDamageCooldown;

        private void Awake()
        {
            _waitDamageCooldown = new WaitForSeconds(_damageCooldown);
        }
        

        private void OnDestroy()
        {
            if (_hitCoroutine != null)
            {
                StopCoroutine(_hitCoroutine);
            }
        }
        public void Setup(float maxValue, float value = 0)
        {
            _healthSlider.value = value <= 0 ? maxValue : value;
            _healthSlider.maxValue = maxValue;
        }

        public void OnDamage(float damage, bool ignoreDamage = false, GameObject damageObject = null)
        {
            if (!ignoreDamage && shielded)
            {
                blocked?.Invoke(damage, damageObject);
                return;
            }
            if (!ignoreDamage && !useDamage)
            {
                return;
            }

            if (_cooldownEnable)
            {
                if (_damageCooldownCoroutine != null)
                {
                    return;
                }
                _damageCooldownCoroutine = StartCoroutine(OnDamageCooldown());
            }

            _healthSlider.value -= damage;
            _damageEvent?.Invoke();
            damaged?.Invoke(damage, damageObject);
            if (_hitCoroutine != null)
            {
                StopCoroutine(_hitCoroutine);
            }
            _hitCoroutine = StartCoroutine(HitAnimationSlider());
            if (_healthSlider.value == _healthSlider.minValue)
            {
                if (_deathObject.TryGetComponent(out IDeath death))
                {
                    death?.OnDeath();
                }
            }
        }
        IEnumerator HitAnimationSlider()
        {
            for (float i = 0; i < _targetTransitionDuration; i+= Time.deltaTime)
            {
                _hitRect.anchorMax = Vector2.Lerp(Vector2.one, Vector2.up, i / _targetTransitionDuration);
                yield return null;
            }
            _hitCoroutine = null;
        }
        
        IEnumerator OnDamageCooldown()
        {
            useDamage = false;
            yield return _waitDamageCooldown;
            _damageCooldownCoroutine = null;
            useDamage = true;
        }
        
        IEnumerator OnHealTransition(float targetValue)
        {
            float baseValue = _healthSlider.value;
            for (float i = 0; i < _healTime; i+= Time.deltaTime)
            {
                _healthSlider.value  = Mathf.Lerp(baseValue, targetValue, i / _healTime);
                yield return null;
            }
            _healCoroutine =  null;
        }

        public void OnHeal(float value)
        {
            if (_healCoroutine != null)
            {
                StopCoroutine(_healCoroutine);
            }
            _healCoroutine = StartCoroutine(OnHealTransition(_healthSlider.value + value));
            healed?.Invoke(value);
        }
    }
}
