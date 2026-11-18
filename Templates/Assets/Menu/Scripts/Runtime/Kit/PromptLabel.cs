using PG.MenuManagement;
using System.Text.RegularExpressions;
using InputSystemActionPrompts;
using PG.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Строка подсказки «значки кнопок + переведённое слово»: [UI/Cancel]  Назад.
// PromptText запоминает текст один раз, поэтому слово подставляем сами и просим его перерисовать значки.
// Значок, которого для устройства нет (MISSING_PROMPT), не показывается - остаётся только слово.
[RequireComponent(typeof(TMP_Text))]
public class PromptLabel : MonoBehaviour
{
    [Tooltip("Значки кнопок, например [UI/PreviousPage] [UI/NextPage]")]
    [SerializeField] private string _prompts;
    [Tooltip("Ключ локализации слова")]
    [SerializeField] private string _key;
    [Tooltip("Слово, если ключа нет")]
    [SerializeField] private string _fallback;
    [Tooltip("Значки только для геймпада: на клавиатуре и мыши остаётся одно слово (кнопки нажимают мышью)")]
    [SerializeField] private bool _gamepadOnly;

    private static readonly Regex Tag = new Regex(@"\[[^\]]+\]");
    private static InputDevice s_device;

    private void OnEnable()
    {
        if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged += OnLanguage;
        InputDevicePromptSystem.OnActiveDeviceChanged += OnDevice;
        Apply();
    }

    private void OnDisable()
    {
        if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged -= OnLanguage;
        InputDevicePromptSystem.OnActiveDeviceChanged -= OnDevice;
    }

    private void OnLanguage(string _) => Apply();

    private void OnDevice(InputDevice device)
    {
        s_device = device;
        Apply();
    }

    public void Setup(string prompts, string key, string fallback, bool gamepadOnly = false)
    {
        _prompts = prompts;
        _key = key;
        _fallback = fallback;
        _gamepadOnly = gamepadOnly;
        Apply();
    }

    private void Apply()
    {
        string word = string.IsNullOrEmpty(_key) || LocalizationSystem.instance == null ? _fallback : LocalizationSystem.instance.GetLocalizedValue(_key, _fallback);
        string prompts = VisiblePrompts();
        var text = GetComponent<TMP_Text>();
        text.text = string.IsNullOrEmpty(prompts) ? word : prompts + "  " + word;
        if (TryGetComponent(out PromptText prompt)) prompt.RefreshTextWithUpdate();
    }

    // Значки, которые есть у текущего устройства (и никаких - на клавиатуре, если строка только для геймпада)
    private string VisiblePrompts()
    {
        if (string.IsNullOrEmpty(_prompts) || (_gamepadOnly && !UsingGamepad())) return "";
        if (!Application.isPlaying) return _prompts;
        return Tag.Replace(_prompts, m => InputDevicePromptSystem.InsertPromptSprites(m.Value).Contains("MISSING_PROMPT") ? "" : m.Value).Trim();
    }

    private static bool UsingGamepad()
    {
        if (s_device != null) return s_device is Gamepad;
        var pad = Gamepad.current;
        var keyboard = Keyboard.current;
        return pad != null && (keyboard == null || pad.lastUpdateTime > keyboard.lastUpdateTime);
    }
}
