using System;
using TMPro;
using UnityEngine;

public class DayNightTimeChanger : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown _dayNightDropdown;
    private DayNightTime _dayNightTime;

    private void Awake()
    {
        _dayNightTime = FindAnyObjectByType<DayNightTime>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _dayNightDropdown.SetValueWithoutNotify((int)_dayNightTime.config);
    }

    private void OnEnable()
    {
        _dayNightDropdown.onValueChanged.AddListener(SetValue);
    }

    private void OnDisable()
    {
        _dayNightDropdown.onValueChanged.RemoveListener(SetValue);
    }

    public void SetValue(int value)
    {
        if (_dayNightTime != null)
        {
            _dayNightTime.SetConfig(value);
        }
    }
}
