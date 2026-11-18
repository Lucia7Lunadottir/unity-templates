using System.Collections.Generic;
using PG.HealthSystem;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public struct EnemyWave
{
    public event System.Action started;
    public UnityEvent startedEvent;
    public event System.Action ended;
    public UnityEvent endedEvent;
    public GameObject[] enemies;
    private List<IDeath> _deaths;

    public void Start()
    {
        started?.Invoke();
        startedEvent?.Invoke();
        _deaths = new List<IDeath>();
        foreach (var enemy in enemies)
        {
            enemy.SetActive(true);
            if (enemy.TryGetComponent(out IDeath death))
            {
                _deaths.Add(death);
                var wave = this;
                death.dead += () => wave.OnDeath(death);
            }
        }
    }

    void OnDeath(IDeath death)
    {
        _deaths.Remove(death);
        var wave = this;
        death.dead -= () => wave.OnDeath(death);
        OnEndWave();
    }
        
    void OnEndWave()
    {
        if (_deaths.Count == 0)
        {
            ended?.Invoke();
            endedEvent?.Invoke();
        }
    }
}