using UnityEngine;
using UnityEngine.Events;

public class EnemyWavesController : MonoBehaviour
{
    [SerializeField] private EnemyWave[] _waves;
    private int _currentWave;
    public UnityEvent startEvent;
    public UnityEvent endEvent;

    public void CreateWaves(EnemyWave[] waves)
    {
        _waves = waves;
    }
    
    public void StartWave()
    {
        _currentWave = 0;
        startEvent?.Invoke();
        StartCurrentWave();
    }

    void StartCurrentWave()
    {
        EnemyWave wave = _waves[_currentWave];
        wave.startedEvent?.Invoke();
        wave.ended += OnNextWave;
        wave.Start();
    }

    void OnNextWave()
    {
        _waves[_currentWave].ended -= OnNextWave;
        
        _currentWave++;
        if (_currentWave >= _waves.Length)
        {
            endEvent?.Invoke();
        }
        else
        {
            StartCurrentWave();
        }
    }
}