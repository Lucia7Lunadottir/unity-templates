using System;
using UnityEngine;
using UnityEngine.Events;

public class DayNightSystem : DayNightTime, ISaveable
{
    [SerializeField] private Light _lightSun;
    [SerializeField] private string _skyboxExposureParameter = "_Exposure";

    [Tooltip("Компоненты с IResilienceTarget (например, TemperatureSystem игры); пусто - температура не меняется")]
    [SerializeField] private MonoBehaviour[] _temperatureSystems;

    
    [Header("Night")]
    [SerializeField] private float _nightLightIntensity = 0.1f;
    [SerializeField] private Color _nightFogColor = Color.gray;
    [SerializeField] private float _nightSkyboxExposure = 0.2f;
    [SerializeField] private float _nightSkyboxEnviromentLighting = 0.5f;
    [SerializeField] private float _nightTemperatureResilienceTime = 175;
    [SerializeField] private UnityEvent _visualNightStarted;

    [Header("Day")]
    [SerializeField] private float _dayLightIntensity = 1f;
    [SerializeField] private Color _dayFogColor = Color.gray;
    [SerializeField] private float _daySkyboxExposure = 1f;
    [SerializeField] private float _daySkyboxEnviromentLighting = 1f;
    [SerializeField] private float _dayTemperatureResilienceTime = 250;
    [SerializeField] private UnityEvent _visualDayStarted;

    [SerializeField] private bool _changeInEditor;

    private void OnEnable()
    {
        configChanged += UpdateVisuals;
        OnNightStarted.RemoveListener(_visualNightStarted.Invoke);
        OnNightEnded.RemoveListener(_visualDayStarted.Invoke);
        OnNightStarted.AddListener(_visualNightStarted.Invoke);
        OnNightEnded.AddListener(_visualDayStarted.Invoke);
    }
    private void OnDisable()
    {
        configChanged -= UpdateVisuals;
        UnRegisterTemperature();
        OnNightStarted.RemoveListener(_visualNightStarted.Invoke);
        OnNightEnded.RemoveListener(_visualDayStarted.Invoke);
    }

    protected override void Start()
    {
        base.Start();
        UpdateVisuals();
    }

    protected override void Update()
    {
        base.Update();
        
        switch (config)
        {
            case Config.DayNight:
                SetDayNight();
                break;
            case Config.DayNightWithoutTemperature:
                SetDayNight();
                break;
            case Config.DayNightWithNightTemperature:
                SetDayNight();
                break;
        }
    }



    private void UpdateVisuals()
    {
        // 1. Управляем подпиской на события дня/ночи в зависимости от конфига
        if (config == Config.DayNight)
        {
            RegisterTemperature();
        }
        else
        {
            UnRegisterTemperature();
        }

        // 2. Выставляем начальные значения
        switch (config)
        {
            case Config.DayNight:
                SetDayNight();
                SetResilienceTimeTemperature(setNight ? _nightTemperatureResilienceTime : _dayTemperatureResilienceTime);
                break;
            case Config.OnlyDay:
                SetDay();
                SetResilienceTimeTemperature(_dayTemperatureResilienceTime);
                break;
            case Config.OnlyDayWithNightTemperature:
                SetDay();
                SetResilienceTimeTemperature(_nightTemperatureResilienceTime);
                break;
            case Config.OnlyNight:
                SetNight();
                SetResilienceTimeTemperature(_nightTemperatureResilienceTime);
                break;
            case Config.OnlyNightWithoutTemperature:
                SetNight();
                SetResilienceTimeTemperature(_dayTemperatureResilienceTime);
                break;
            case Config.DayNightWithoutTemperature:
                SetDayNight();
                SetResilienceTimeTemperature(_dayTemperatureResilienceTime);
                break;
            case Config.DayNightWithNightTemperature:
                SetDayNight();
                SetResilienceTimeTemperature(_nightTemperatureResilienceTime);
                break;
        }
    }


    public void SetDayResilienceTimeTemperature() => SetResilienceTimeTemperature(_dayTemperatureResilienceTime);
    public void SetNightResilienceTimeTemperature() => SetResilienceTimeTemperature(_nightTemperatureResilienceTime);
    public void SetResilienceTimeTemperature(float time)
    {
        if (_temperatureSystems.Length > 0)
        {
            foreach (var temperatureSystem in _temperatureSystems)
            {
                if (temperatureSystem is IResilienceTarget target)
                {
                    target.resilienceTime = time;
                }
            }
        }
    }

    void RegisterTemperature()
    {
        UnRegisterTemperature();
        OnNightStarted.AddListener(SetNightResilienceTimeTemperature);
        OnNightEnded.AddListener(SetDayResilienceTimeTemperature);
    }
    void UnRegisterTemperature()
    {
        OnNightStarted.RemoveListener(SetNightResilienceTimeTemperature);
        OnNightEnded.RemoveListener(SetDayResilienceTimeTemperature);
    }
    public override void Load()
    {
        base.Load();
        UpdateVisuals();
    }

    public override void SetConfig(int value)
    {
        base.SetConfig(value);
        UpdateVisuals();
    }

    public void SetNight()
    {
        _visualNightStarted?.Invoke();
        
        if (_lightSun != null)
        {
            _lightSun.intensity = _nightLightIntensity;
        }

        RenderSettings.fogColor = _nightFogColor;
        RenderSettings.ambientIntensity = _nightSkyboxEnviromentLighting;
        

        if (RenderSettings.skybox != null)
        {
            RenderSettings.skybox.SetFloat(_skyboxExposureParameter, _nightSkyboxExposure);
        }
    }
    public void SetDay()
    {
        _visualDayStarted?.Invoke();
        
        if (_lightSun != null)
        {
            _lightSun.intensity = _dayLightIntensity;
        }

        RenderSettings.fogColor = _dayFogColor;
        RenderSettings.ambientIntensity = _daySkyboxEnviromentLighting;
        

        if (RenderSettings.skybox != null)
        {
            RenderSettings.skybox.SetFloat(_skyboxExposureParameter, _daySkyboxExposure);
        }
    }

    public void SetDayNight()
    {
        if (_lightSun != null)
        {
            _lightSun.intensity = Mathf.Lerp(_dayLightIntensity, _nightLightIntensity, nightLerp);
        }

        RenderSettings.fogColor = Color.Lerp(_dayFogColor, _nightFogColor, nightLerp);
        RenderSettings.ambientIntensity = Mathf.Lerp(_daySkyboxEnviromentLighting, _nightSkyboxEnviromentLighting, nightLerp);
        

        if (RenderSettings.skybox != null)
        {
            RenderSettings.skybox.SetFloat(_skyboxExposureParameter, Mathf.Lerp(_daySkyboxExposure, _nightSkyboxExposure, nightLerp));
        }
    }

    private void OnValidate()
    {
        if (!_changeInEditor) return;
        UpdateVisuals();
    }

}
