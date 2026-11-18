using PG.HealthSystem;
using UnityEngine;
using UnityEngine.InputSystem;

// Барьер как в BBS: нажатие = короткий щит, нажатия не теряются (буфер), каждый блок продлевает щит.
// Замена PlayerDodge: на Lieris V3 рядом с ним, PlayerDodge выключен.
public class PlayerBarrier : MonoBehaviour
{
    [Header("Cache")]
    [Tooltip("Кнопка барьера (та же, что была у PlayerDodge).")]
    [SerializeField] private InputActionProperty _barrierActionProperty;
    [Tooltip("Здоровье героини (PLAYER/MANAGERS/Manager).")]
    [SerializeField] private Health _health;
    [Tooltip("Animator героини.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Bool в Animator: true, пока барьер активен.")]
    [SerializeField] private string _barrierBool = "Guard";
    [Tooltip("Выносливость героини. Пусто - барьер бесплатный.")]
    [SerializeField] private MonoBehaviour _staminaController;

    [Header("Тайминги")]
    [Tooltip("Сколько длится барьер после одного нажатия, секунды.")]
    [SerializeField, Min(0.05f)] private float _barrierTime = 0.4f;
    [Tooltip("Каждый заблокированный удар продлевает барьер минимум на столько секунд от момента удара.")]
    [SerializeField, Min(0f)] private float _extendOnBlock = 0.3f;
    [Tooltip("Барьер не держится дольше этого с момента нажатия, даже если по нему бьют без остановки.")]
    [SerializeField, Min(0.05f)] private float _maxBarrierTime = 2f;
    [Tooltip("Пауза после барьера, прежде чем можно поставить новый, секунды.")]
    [SerializeField, Min(0f)] private float _recovery = 0.15f;
    [Tooltip("Нажатие во время паузы запоминается на столько секунд и срабатывает, как только барьер доступен.")]
    [SerializeField, Min(0f)] private float _inputBuffer = 0.2f;

    [Header("Выносливость")]
    [Tooltip("Расход выносливости за одно нажатие. 0 - без расхода.")]
    [SerializeField, Min(0f)] private float _staminaCost = 10f;

    [Header("Блок")]
    [Tooltip("Искры при блоке. Пусто - без искр.")]
    [SerializeField] private ParticleSystem _blockParticles;
    [Tooltip("Сколько частиц выпустить при блоке. 0 - запустить систему целиком (Play).")]
    [SerializeField, Min(0)] private int _blockParticlesCount;

    // GameObject героини и включён ли барьер - как dodged у PlayerDodge
    public event System.Action<GameObject, bool> barrierChanged;
    // Урон и источник удара, который принял барьер
    public event System.Action<float, GameObject> blockedHit;
    public bool isActive { get; private set; }

    private float _startedAt;
    private float _activeUntil;
    private float _recoveredAt;
    private float _bufferedUntil = -1f;

    private void OnEnable()
    {
        _barrierActionProperty.action.started += OnPressed;
        if (_health != null) _health.blocked += OnBlocked;
    }

    private void OnDisable()
    {
        _barrierActionProperty.action.started -= OnPressed;
        if (_health != null) _health.blocked -= OnBlocked;
        if (isActive) End();
        _bufferedUntil = -1f;
    }

    private void Update()
    {
        float now = Time.time;
        if (isActive && now >= _activeUntil) End();
        if (!isActive && now <= _bufferedUntil && now >= _recoveredAt) TryStart();
    }

    private void OnPressed(InputAction.CallbackContext context)
    {
        _bufferedUntil = Time.time + _inputBuffer;
        if (!isActive && Time.time >= _recoveredAt) TryStart();
    }

    private void TryStart()
    {
        _bufferedUntil = -1f;
        if (_staminaController is IStaminaSource stamina && _staminaCost > 0f)
        {
            if (stamina.isResting) return;
            stamina.Spend(_staminaCost);
        }

        isActive = true;
        _startedAt = Time.time;
        _activeUntil = _startedAt + _barrierTime;
        SetShield(true);
    }

    private void End()
    {
        isActive = false;
        _recoveredAt = Time.time + _recovery;
        SetShield(false);
    }

    private void SetShield(bool active)
    {
        if (_health != null) _health.shielded = active;
        if (_animator != null) _animator.SetBool(_barrierBool, active);
        barrierChanged?.Invoke(_animator != null ? _animator.gameObject : gameObject, active);
    }

    private void OnBlocked(float damage, GameObject source)
    {
        if (!isActive) return;
        _activeUntil = Mathf.Min(Mathf.Max(_activeUntil, Time.time + _extendOnBlock), _startedAt + _maxBarrierTime);

        if (_blockParticles != null)
        {
            if (_blockParticlesCount > 0) _blockParticles.Emit(_blockParticlesCount);
            else _blockParticles.Play(true);
        }
        blockedHit?.Invoke(damage, source);
    }
}
