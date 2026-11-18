using System.Collections;
using PG.HealthSystem;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// «Удар чувствуется»: стоп-кадр, толчок камеры, вибрация геймпада.
// Компонент в PLAYER/MANAGERS/Manager (настройки - в инспекторе);
// попадания по врагам и урон героине ловит через события, боссы зовут Play напрямую.
public class CombatFeedback : MonoBehaviour
{
    public enum Strength { Hit, Kill, PlayerHurt, Heavy, Block }
    public enum HitstopMode { Fixed, Diminishing }

    [System.Serializable]
    private class Preset
    {
        [Tooltip("Стоп-кадр, секунды реального времени. 0 - без стоп-кадра.")]
        [Min(0f)] public float hitstop;
        [Tooltip("Сила толчка камеры, доля от «Shake Amplitude» (1 = полный сдвиг). 0 - камера не трясётся.")]
        [Min(0f)] public float shake;
        [Tooltip("Вибрация геймпада: низкочастотный мотор (0-1).")]
        [Range(0f, 1f)] public float rumbleLow;
        [Tooltip("Вибрация геймпада: высокочастотный мотор (0-1).")]
        [Range(0f, 1f)] public float rumbleHigh;
        [Tooltip("Длительность вибрации, секунды. 0 - без вибрации.")]
        [Min(0f)] public float rumbleTime;

        public Preset(float hitstop, float shake, float low, float high, float time)
        {
            this.hitstop = hitstop; this.shake = shake; rumbleLow = low; rumbleHigh = high; rumbleTime = time;
        }
    }

    private const string ShakeSaveKey = "CameraShake"; // та же настройка, что в CameraShakeEnabler
    private const string PlayerTag = "Player";

    private static CombatFeedback _instance;

    [Header("Пресеты")]
    [Tooltip("Попадание героини по врагу.")]
    [SerializeField] private Preset _hit = new(0.045f, 0f, 0.15f, 0.3f, 0.08f);
    [Tooltip("Враг умер от удара.")]
    [SerializeField] private Preset _kill = new(0.09f, 0f, 0.45f, 0.7f, 0.18f);
    [Tooltip("Обычный удар по героине.")]
    [SerializeField] private Preset _playerHurt = new(0.06f, 0.6f, 0.65f, 0.35f, 0.2f);
    [Tooltip("Тяжёлый удар: медведь, боссы, ульты, смена фаз.")]
    [SerializeField] private Preset _heavy = new(0.12f, 1f, 0.8f, 1f, 0.3f);
    [Tooltip("Барьер героини принял удар.")]
    [SerializeField] private Preset _block = new(0.05f, 0f, 0.3f, 0.6f, 0.1f);

    [Header("Общее")]
    [Tooltip("Удар по героине с уроном не меньше этого - Heavy (медведь 35/65, волк 25).")]
    [SerializeField] private float _heavyDamage = 30f;
    [Tooltip("Скорость времени во время стоп-кадра.")]
    [SerializeField, Range(0f, 0.5f)] private float _hitstopTimeScale = 0.05f;

    [Header("Удары героини")]
    [Tooltip("Fixed - стоп-кадр всегда одинаковый. Diminishing - чем чаще героиня попадает, тем слабее замедление.")]
    [SerializeField] private HitstopMode _playerHitsMode = HitstopMode.Diminishing;
    [Tooltip("Diminishing: насколько каждое попадание ослабляет следующие (1 = сразу без замедления).")]
    [SerializeField, Range(0f, 1f)] private float _fatiguePerHit = 0.25f;
    [Tooltip("Diminishing: столько секунд без попаданий ослабление держится, потом начинает уходить.")]
    [SerializeField, Min(0f)] private float _fatigueHold = 1.5f;
    [Tooltip("Diminishing: сколько ослабления уходит за секунду после паузы.")]
    [SerializeField, Min(0f)] private float _fatigueRecovery = 0.6f;
    [Tooltip("Diminishing: скорость времени при полном ослаблении (1 = замедления нет совсем).")]
    [SerializeField, Range(0f, 1f)] private float _fatiguedTimeScale = 0.6f;
    [Tooltip("Diminishing: доля длительности стоп-кадра при полном ослаблении.")]
    [SerializeField, Range(0f, 1f)] private float _fatiguedDuration = 0.3f;
    [Tooltip("Попадания по врагам не чаще раза в столько секунд (частицы магии бьют много раз в секунду).")]
    [SerializeField, Min(0f)] private float _minInterval = 0.12f;
    [Tooltip("Длительность толчка камеры, секунды (каждый раз ±20%).")]
    [SerializeField, Min(0.01f)] private float _shakeDuration = 0.18f;
    [Tooltip("Сдвиг камеры в метрах при shake = 1. Пресеты задают долю от него.")]
    [SerializeField, Min(0f)] private float _shakeAmplitude = 0.12f;
    [Tooltip("0 - толчок строго от удара, 1 - в случайную сторону.")]
    [SerializeField, Range(0f, 1f)] private float _shakeRandomness = 0.35f;

    private CinemachineImpulseSource _impulse;
    private Health _playerHealth;
    private float _lastFeedback = -1f;
    private float _scaleBeforeHitstop = 1f;
    private float _appliedScale;
    private float _fatigue, _fatigueTime;
    private bool _inHitstop;
    private Coroutine _hitstopRoutine, _rumbleRoutine;

    // Для боссов и особых атак: сильный удар, смена фазы, ульта
    public static void Play(Strength strength, Vector3 direction = default)
    {
        if (_instance != null) _instance.Run(strength, direction, true);
    }

    private void Awake()
    {
        if (!TryGetComponent(out _impulse)) _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
        _impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        // Резкий толчок и плавный возврат, без качания туда-обратно
        _impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
        _impulse.ImpulseDefinition.ImpulseDuration = _shakeDuration;
        // Толчок должен играть и во время стоп-кадра
        CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
    }

    private void OnEnable()
    {
        _instance = this;
        HealthMob.anyDamaged += OnMobDamaged;
        HealthMob.anyDied += OnMobDied;
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindScene();
    }

    private void OnDisable()
    {
        HealthMob.anyDamaged -= OnMobDamaged;
        HealthMob.anyDied -= OnMobDied;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_playerHealth != null)
        {
            _playerHealth.damaged -= OnPlayerDamaged;
            _playerHealth.blocked -= OnPlayerBlocked;
        }
        StopRumble();
        EndHitstop();
        if (_instance == this) _instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene();

    // Слушатель толчков на каждую камеру Cinemachine и подписка на здоровье героини
    private void BindScene()
    {
        foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include))
        {
            if (cam.GetComponent<CinemachineImpulseListener>() != null) continue;
            var listener = cam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.ApplyAfter = CinemachineCore.Stage.Noise;
            listener.ChannelMask = 1;
            listener.Gain = 1f;
            listener.UseCameraSpace = true;
            listener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.Additive;
            listener.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction { AmplitudeGain = 1f, FrequencyGain = 1f, Duration = 1f };
        }

        var player = GameObject.FindGameObjectWithTag(PlayerTag);
        var health = player != null ? player.GetComponentInChildren<Health>(true) : null;
        if (health == null) health = FindAnyObjectByType<Health>(FindObjectsInactive.Include);
        if (health == _playerHealth) return;
        if (_playerHealth != null)
        {
            _playerHealth.damaged -= OnPlayerDamaged;
            _playerHealth.blocked -= OnPlayerBlocked;
        }
        _playerHealth = health;
        if (_playerHealth != null)
        {
            _playerHealth.damaged += OnPlayerDamaged;
            _playerHealth.blocked += OnPlayerBlocked;
        }
    }

    private void OnMobDamaged(HealthMob mob, float damage) => Run(Strength.Hit, Vector3.zero, false);
    private void OnMobDied(HealthMob mob) => Run(Strength.Kill, Vector3.zero, true);
    private void OnPlayerDamaged(float damage, GameObject source)
    {
        var dir = source != null && _playerHealth != null ? _playerHealth.transform.position - source.transform.position : Vector3.zero;
        Run(damage >= _heavyDamage ? Strength.Heavy : Strength.PlayerHurt, dir, true);
    }

    private void OnPlayerBlocked(float damage, GameObject source) => Run(Strength.Block, Vector3.zero, true);

    private void Run(Strength strength, Vector3 direction, bool force)
    {
        // Пауза/меню: время уже стоит - не трогаем
        if (Time.timeScale <= 0f && !_inHitstop) return;
        float now = Time.unscaledTime;
        if (!force && now - _lastFeedback < _minInterval) return;
        _lastFeedback = now;

        var p = strength switch
        {
            Strength.Kill => _kill,
            Strength.PlayerHurt => _playerHurt,
            Strength.Heavy => _heavy,
            Strength.Block => _block,
            _ => _hit
        };

        if (strength == Strength.Hit || strength == Strength.Kill) PlayerStrikeHitstop(p.hitstop, now);
        else Hitstop(p.hitstop, _hitstopTimeScale);
        Shake(p.shake, direction);
        Rumble(p.rumbleLow, p.rumbleHigh, p.rumbleTime);
    }

    // Diminishing: частые удары копят «усталость» - стоп-кадр короче и время замедляется меньше
    private void PlayerStrikeHitstop(float duration, float now)
    {
        if (_playerHitsMode == HitstopMode.Fixed)
        {
            Hitstop(duration, _hitstopTimeScale);
            return;
        }
        float idle = now - _fatigueTime - _fatigueHold;
        if (idle > 0f) _fatigue = Mathf.Max(0f, _fatigue - _fatigueRecovery * idle);
        _fatigueTime = now;
        float scale = Mathf.Lerp(_hitstopTimeScale, Mathf.Max(_hitstopTimeScale, _fatiguedTimeScale), _fatigue);
        Hitstop(duration * Mathf.Lerp(1f, _fatiguedDuration, _fatigue), scale);
        _fatigue = Mathf.Min(1f, _fatigue + _fatiguePerHit);
    }

    private void Hitstop(float duration, float timeScale)
    {
        if (duration <= 0f || timeScale >= 1f) return;
        if (_hitstopRoutine != null) StopCoroutine(_hitstopRoutine);
        // Уже идущий более сильный стоп-кадр не ослабляем
        _appliedScale = _inHitstop ? Mathf.Min(_appliedScale, timeScale) : timeScale;
        if (!_inHitstop) _scaleBeforeHitstop = Time.timeScale;
        _inHitstop = true;
        Time.timeScale = _appliedScale;
        _hitstopRoutine = StartCoroutine(HitstopRoutine(duration));
    }

    private IEnumerator HitstopRoutine(float duration)
    {
        yield return new WaitForSecondsRealtime(duration);
        EndHitstop();
    }

    private void EndHitstop()
    {
        if (!_inHitstop) return;
        _inHitstop = false;
        _hitstopRoutine = null;
        // Если за это время меню поставило свою паузу - не перебиваем её
        if (Mathf.Approximately(Time.timeScale, _appliedScale)) Time.timeScale = _scaleBeforeHitstop;
    }

    private void Shake(float force, Vector3 direction)
    {
        // Слайдер «Тряска камеры» в настройках - множитель
        force *= _shakeAmplitude * Mathf.Clamp01(PlayerPrefs.GetFloat(ShakeSaveKey, 1f));
        if (force <= 0f) return;
        _impulse.ImpulseDefinition.ImpulseDuration = _shakeDuration * Random.Range(0.8f, 1.2f);
        _impulse.GenerateImpulseWithVelocity(ScreenKick(direction) * force);
    }

    // Направление в плоскости экрана (слушатель работает в пространстве камеры): от удара вбок, без наезда камеры
    private Vector3 ScreenKick(Vector3 worldDirection)
    {
        Vector2 kick = Random.insideUnitCircle.normalized;
        var cam = Camera.main;
        if (cam != null && worldDirection.sqrMagnitude > 0.0001f)
        {
            var t = cam.transform;
            var hit = new Vector2(Vector3.Dot(worldDirection, t.right), Vector3.Dot(worldDirection, t.up));
            var mixed = hit.sqrMagnitude > 0.0001f ? Vector2.Lerp(hit.normalized, kick, _shakeRandomness) : kick;
            if (mixed.sqrMagnitude > 0.0001f) kick = mixed.normalized;
        }
        return new Vector3(kick.x, kick.y * 0.5f, 0f);
    }

    private void Rumble(float low, float high, float time)
    {
        var pad = Gamepad.current;
        if (pad == null || time <= 0f) return;
        if (_rumbleRoutine != null) StopCoroutine(_rumbleRoutine);
        pad.SetMotorSpeeds(low, high);
        _rumbleRoutine = StartCoroutine(RumbleRoutine(time));
    }

    private IEnumerator RumbleRoutine(float time)
    {
        yield return new WaitForSecondsRealtime(time);
        StopRumble();
    }

    private void StopRumble()
    {
        _rumbleRoutine = null;
        Gamepad.current?.SetMotorSpeeds(0f, 0f);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) StopRumble();
    }
}
