using UnityEngine;
using UnityEngine.Playables;
using PG.Tween;

// Общее для всех катсцен этого PlayableDirector: что спрятать, что показать, блокировка ввода.
// Срабатывает на любой запуск (граф, триггер, сигнал, Play On Awake), после конца всё возвращается как было.
[RequireComponent(typeof(PlayableDirector))]
public class CutsceneDirector : MonoBehaviour
{
    [Tooltip("Выключить на время катсцены (HUD, скрипты игрока)")]
    [SerializeField] private GameObject[] _hideDuring;
    [Tooltip("Включить на время катсцены (кинорамка, субтитры)")]
    [SerializeField] private GameObject[] _showDuring;
    [Tooltip("Блокировать управление игроком на время катсцены (через CutsceneBridge)")]
    [SerializeField] private bool _lockInput = true;
    
    [SerializeField] private CanvasGroup _cinemachineCanvas;
    [SerializeField] private float _transition = 0.25f;

    private PlayableDirector _director;
    private bool[] _hideWasActive;
    private bool[] _showWasActive;
    private bool _running;

    public PlayableDirector director => _director;
    public bool isRunning => _running;

    void Awake()
    {
        _director = GetComponent<PlayableDirector>();
    }

    void OnEnable()
    {
        _director.played += OnPlayed;
        _director.stopped += OnStopped;
        if (_director.state == PlayState.Playing) Begin();
    }

    void OnDisable()
    {
        _director.played -= OnPlayed;
        _director.stopped -= OnStopped;
        End();
    }

    private void OnPlayed(PlayableDirector _) => Begin();
    private void OnStopped(PlayableDirector _) => End();

    private void Begin()
    {
        if (_running) return;
        _running = true;
        _cinemachineCanvas.gameObject.SetActive(true);
        _cinemachineCanvas?.OnAlphaTween(1f, _transition);
        _hideWasActive = SetActive(_hideDuring, false);
        _showWasActive = SetActive(_showDuring, true);
        if (_lockInput) CutsceneBridge.inputLock?.Invoke(this);
    }

    private void End()
    {
        if (!_running) return;
        _running = false;
        _cinemachineCanvas?.OnAlphaTween(0f, _transition, false, null, ()=> 
        _cinemachineCanvas.gameObject.SetActive(false));
        Restore(_hideDuring, _hideWasActive);
        Restore(_showDuring, _showWasActive);
        if (_lockInput) CutsceneBridge.inputUnlock?.Invoke(this);
    }

    // Возвращает, какими объекты были до катсцены
    private static bool[] SetActive(GameObject[] objects, bool active)
    {
        if (objects == null) return null;
        var was = new bool[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null) continue;
            was[i] = objects[i].activeSelf;
            objects[i].SetActive(active);
        }
        return was;
    }

    private static void Restore(GameObject[] objects, bool[] was)
    {
        if (objects == null || was == null) return;
        for (int i = 0; i < objects.Length; i++){
            if (objects[i] != null) objects[i].SetActive(was[i]);
        }
    }
}
