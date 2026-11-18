using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HealthAISlider : MonoBehaviour
{
    [SerializeField] private HealthMob _healthMob;
    [SerializeField] private Slider _slider;
    [SerializeField] private Image _hitImage;
    [SerializeField] private float _delayBeforeShrink = 0.5f; // Пауза перед тем, как полоска урона начнет уменьшаться
    [SerializeField] private float _targetTransitionDuration = 0.1f;

    private Coroutine _hitCoroutine;

    private void Awake()
    {
        if (_healthMob == null) _healthMob = GetComponentInParent<HealthMob>(true);
        if (_healthMob == null)
        {
            Debug.LogWarning($"[HealthAISlider] Не найден HealthMob для '{name}'.", this);
            enabled = false;
            return;
        }

        _slider.maxValue = _healthMob.maxHealth;
        _slider.value = _healthMob.health;
    }

    private void OnEnable()
    {
        if (_healthMob != null) _healthMob.onValueChanged += ChangeValue;
    }

    private void OnDisable()
    {
        if (_healthMob != null) _healthMob.onValueChanged -= ChangeValue;
    }

    void ChangeValue(float value)
    {
        _slider.value = value;

        // Если корутина уже идет, останавливаем, чтобы начать заново (эффект суммирования урона)
        if (_hitCoroutine != null) StopCoroutine(_hitCoroutine);
        _hitCoroutine = StartCoroutine(HitAnimationSlider());
    }

    IEnumerator HitAnimationSlider()
    {
        yield return new WaitForSeconds(_delayBeforeShrink);
        for (float i = 0; i < _targetTransitionDuration; i += Time.deltaTime)
        {
            _hitImage.rectTransform.anchorMax = Vector2.Lerp(Vector2.one, Vector2.up, i / _targetTransitionDuration);
            yield return null;
        }
        _hitCoroutine = null;
    }
}