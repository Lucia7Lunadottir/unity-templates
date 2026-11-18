using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Манекен для урона: считает удары, сумму и урон в секунду, по умолчанию не умирает
[RequireComponent(typeof(HealthMob))]
public class TrainingDummy : MonoBehaviour
{
    [Tooltip("Здоровье возвращается к максимуму на каждом ударе - манекен не умирает.")]
    [SerializeField] private bool _immortal = true;
    [Tooltip("Окно подсчёта урона в секунду, секунды.")]
    [SerializeField, Min(0.1f)] private float _dpsWindow = 3f;
    [Tooltip("Столько секунд без ударов - счётчики сбрасываются.")]
    [SerializeField, Min(0.5f)] private float _resetDelay = 5f;

    [Header("Табличка")]
    [Tooltip("Пусто - табличка создаётся сама над манекеном.")]
    [SerializeField] private TMP_Text _readout;
    [SerializeField] private float _readoutHeight = 2.6f;
    [SerializeField] private float _readoutSize = 3f;

    private readonly Queue<(float time, float damage)> _recent = new Queue<(float, float)>();
    private HealthMob _mob;
    private Camera _camera;
    private float _total, _last, _maxHit, _lastHitTime = float.NegativeInfinity;
    private int _hits;
    private bool _dirty = true;

    private void Awake()
    {
        _mob = GetComponent<HealthMob>();
        if (_readout == null) _readout = CreateReadout();
    }

    private void OnEnable() => HealthMob.anyDamaged += OnAnyDamaged;
    private void OnDisable() => HealthMob.anyDamaged -= OnAnyDamaged;

    private void OnAnyDamaged(HealthMob mob, float damage)
    {
        if (mob != _mob) return;
        // anyDamaged приходит до проверки смерти - лечим раньше, чем HealthMob решит умереть
        if (_immortal) _mob.health = _mob.maxHealth;
        _hits++;
        _total += damage;
        _last = damage;
        _maxHit = Mathf.Max(_maxHit, damage);
        _lastHitTime = Time.time;
        _recent.Enqueue((Time.time, damage));
        _dirty = true;
    }

    [ContextMenu("Сбросить счётчики")]
    public void ResetStats()
    {
        _hits = 0;
        _total = _last = _maxHit = 0f;
        _recent.Clear();
        _mob.health = _mob.maxHealth;
        if (!_mob.enabled) _mob.enabled = true;
        _dirty = true;
    }

    private void Update()
    {
        if (_hits > 0 && Time.time - _lastHitTime > _resetDelay) ResetStats();
        while (_recent.Count > 0 && Time.time - _recent.Peek().time > _dpsWindow)
        {
            _recent.Dequeue();
            _dirty = true;
        }
        if (_dirty) Redraw();
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null) _readout.transform.rotation = _camera.transform.rotation;
    }

    private void Redraw()
    {
        _dirty = false;
        float sum = 0f;
        foreach (var hit in _recent) sum += hit.damage;
        _readout.text = $"Удар: {_last:0.#}\nМакс.: {_maxHit:0.#}\nВсего: {_total:0.#} ({_hits})\nУрон/сек: {sum / _dpsWindow:0.#}";
    }

    private TMP_Text CreateReadout()
    {
        var go = new GameObject("Dummy Readout");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * _readoutHeight;
        var text = go.AddComponent<TextMeshPro>();
        text.fontSize = _readoutSize;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(6f, 3f);
        return text;
    }
}
