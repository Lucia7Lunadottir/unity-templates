using System.IO;
using UnityEngine;
using UnityEngine.Events;

public class DayNightTime : MonoBehaviour, ISaveable
{
    [SerializeField] protected bool _useSave = true;
    [SerializeField] protected string _fileName = "DayNight.json";

    protected string _filePath;
    protected string FilePath => _filePath ??= Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _fileName);

    public enum Config
    {
        DayNight = 0,
        OnlyDay = 1,
        OnlyNight = 2,
        OnlyNightWithoutTemperature = 3,
        OnlyDayWithNightTemperature = 4,
        DayNightWithNightTemperature = 5,
        DayNightWithoutTemperature = 6
    }
    public Config config;

    [Header("Timing (Minutes)")]
    [SerializeField] protected float _dayDurationMinutes = 4;   // full day hold duration (lerp stays at 0)
    [SerializeField] protected float _nightDurationMinutes = 2; // full night hold duration (lerp stays at 1)
    [SerializeField] protected float transitionMinutes = 8;     // day-night transition duration

    [Header("State")]
    [field: SerializeField] public bool setNight { get; set; }
    [field: SerializeField][field:Range(0, 1)] public float nightLerp { get; set; }
    public int currentHour;


    // wait timer for holding at full day or full night
    private float _currentWaitTimer;

    [Header("Events")]
    [SerializeField] public UnityEvent OnNightStarted;
    [SerializeField] public UnityEvent OnNightEnded;
    public event System.Action configChanged;
    public event System.Action<int> dayChanged;
    public event System.Action currentDayChanged;
    
    public int currentDay { get; private set; } = 1;

    protected SaveDayNight _saveDayNight = new SaveDayNight();

    [System.Serializable]
    public class SaveDayNight
    {
        public bool setNight;
        public float nightLerp;
        public int currentDay;
        public int config;
    }


    protected virtual void Start()
    {
        if (_useSave)
        {
            Load();
        }
    }

    protected virtual void Update()
    {
        HandleCycle();
    }

    private void HandleCycle()
    {
        // 1. Transition — drive the lerp step
        float transitionDuration = transitionMinutes > 0 ? transitionMinutes * 60f : 0.001f;
        float step = Time.deltaTime * GameTime.timeMultiplier / transitionDuration;

        if (setNight)
        {
            nightLerp += step;
        }
        else
        {
            nightLerp -= step;
        }

        nightLerp = Mathf.Clamp01(nightLerp);

        // 2. Phase duration
        // Reached full night (1.0)
        if (nightLerp >= 1f && setNight)
        {
            _currentWaitTimer += GameTime.delta;
            // Night duration elapsed — switch to day
            if (_currentWaitTimer >= _nightDurationMinutes * 60f)
            {
                setNight = false; // switch to day
                _currentWaitTimer = 0;
                OnNightEnded?.Invoke();
                currentDay++;
                dayChanged?.Invoke(currentDay);
                currentDayChanged?.Invoke();
            }
        }
        // Reached full day (0.0)
        else if (nightLerp <= 0f && !setNight)
        {
            _currentWaitTimer += GameTime.delta;
            // Day duration elapsed — switch to night
            if (_currentWaitTimer >= _dayDurationMinutes * 60f)
            {
                setNight = true; // switch to night
                _currentWaitTimer = 0;
                OnNightStarted?.Invoke();
            }
        }
        else
        {
            // Still transitioning (neither phase boundary reached).
            // Reset wait timer so it doesn't accumulate during transition.
            _currentWaitTimer = 0;
        }
        CalculateHour();
    }

    private void CalculateHour()
    {
        float daySec = _dayDurationMinutes * 60f;
        float nightSec = _nightDurationMinutes * 60f;
        float transSec = transitionMinutes * 60f;
        float totalSec = daySec + nightSec + (transSec * 2);

        float currentPoint = 0;

        // Determine where in the day cycle we currently are
        if (!setNight && nightLerp <= 0)
        {
            // Waiting in day
            currentPoint = _currentWaitTimer;
        }
        else if (setNight && nightLerp < 1)
        {
            // Transitioning to night (sunset)
            currentPoint = daySec + (nightLerp * transSec);
        }
        else if (setNight && nightLerp >= 1)
        {
            // Waiting in night
            currentPoint = daySec + transSec + _currentWaitTimer;
        }
        else
        {
            // Transitioning to day (sunrise)
            currentPoint = daySec + transSec + nightSec + ((1f - nightLerp) * transSec);
        }

        // Normalise. +12f offsets "start of day" to 12:00
        currentHour = Mathf.FloorToInt((currentPoint / totalSec * 24f + 12f) % 24f);
    }


    // Returns the full day cycle duration in seconds
    public float GetTotalDayCycleSeconds()
    {
        return (_dayDurationMinutes + _nightDurationMinutes + (transitionMinutes * 2)) * 60f;
    }

    public virtual void SetConfig(int value)
    {
        config = (Config)value;
        configChanged?.Invoke();
        Save();
    }

    [ContextMenu("Load")]
    public virtual void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            string json = File.ReadAllText(FilePath);
            JsonUtility.FromJsonOverwrite(json, _saveDayNight);

            nightLerp = _saveDayNight.nightLerp;
            setNight = _saveDayNight.setNight;
            currentDay = _saveDayNight.currentDay;
            config = (Config)_saveDayNight.config;
        }
        catch { }
    }

    [ContextMenu("Save")]
    public virtual void Save()
    {
        _saveDayNight.nightLerp = nightLerp;
        _saveDayNight.setNight = setNight;
        _saveDayNight.currentDay = currentDay;
        _saveDayNight.config = (int)config;

        string json = JsonUtility.ToJson(_saveDayNight);
        File.WriteAllText(FilePath, json);
    }
}