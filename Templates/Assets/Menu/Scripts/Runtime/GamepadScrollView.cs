using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Управление UI ScrollRect с помощью геймпада.
/// Прикрепи этот компонент к объекту с ScrollRect.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class GamepadScrollView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scroll Settings")]
    [Tooltip("Скорость прокрутки стиком")]
    public float scrollSpeed = 300f;

    [Tooltip("Скорость прокрутки кнопками D-Pad / LB / RB")]
    public float buttonScrollSpeed = 600f;

    [Tooltip("Инвертировать ось Y")]
    public bool invertY = false;

    [Tooltip("Инвертировать ось X")]
    public bool invertX = false;

    [Header("Axis")]
    [Tooltip("Разрешить горизонтальную прокрутку")]
    public bool allowHorizontal = true;

    [Tooltip("Разрешить вертикальную прокрутку")]
    public bool allowVertical = true;

    [Header("Focus")]
    [Tooltip("Прокручивать только когда курсор над ScrollView (false = всегда)")]
    public bool requireFocus = true;

    // ──────────────────────────────────────────────
    private ScrollRect _scrollRect;
    private bool _isFocused = false;

    // Кешируем Input Actions
    private Gamepad _gamepad;

    private void Awake()
    {
        _scrollRect = GetComponent<ScrollRect>();
    }

    private void Update()
    {
        _gamepad = Gamepad.current;
        if (_gamepad == null) return;

        // Если нужен фокус, но его нет — пропускаем
        if (requireFocus && !_isFocused) return;

        Vector2 delta = Vector2.zero;

        // ── Левый стик / правый стик ──────────────────
        Vector2 leftStick  = _gamepad.leftStick.ReadValue();
        Vector2 rightStick = _gamepad.rightStick.ReadValue();

        // Стики объединяем (можно убрать один, если не нужен)
        Vector2 stickInput = leftStick.magnitude > rightStick.magnitude ? leftStick : rightStick;

        delta += stickInput * scrollSpeed * Time.unscaledDeltaTime;

        // ── D-Pad ─────────────────────────────────────
        if (_gamepad.dpad.up.isPressed)    delta.y += buttonScrollSpeed * Time.unscaledDeltaTime;
        if (_gamepad.dpad.down.isPressed)  delta.y -= buttonScrollSpeed * Time.unscaledDeltaTime;
        if (_gamepad.dpad.left.isPressed)  delta.x -= buttonScrollSpeed * Time.unscaledDeltaTime;
        if (_gamepad.dpad.right.isPressed) delta.x += buttonScrollSpeed * Time.unscaledDeltaTime;

        // ── Бамперы (LB / RB) — быстрый скролл вверх/вниз ──
        if (_gamepad.leftShoulder.isPressed)  delta.y += buttonScrollSpeed * Time.unscaledDeltaTime;
        if (_gamepad.rightShoulder.isPressed) delta.y -= buttonScrollSpeed * Time.unscaledDeltaTime;

        // ── Применяем инверсию ────────────────────────
        if (invertY) delta.y = -delta.y;
        if (invertX) delta.x = -delta.x;

        // ── Ограничиваем оси ──────────────────────────
        if (!allowHorizontal) delta.x = 0f;
        if (!allowVertical)   delta.y = 0f;

        if (delta == Vector2.zero) return;

        // ── Пересчёт в нормализованные координаты ─────
        ApplyScroll(delta);
    }

    private void ApplyScroll(Vector2 delta)
    {
        RectTransform content  = _scrollRect.content;
        RectTransform viewport = _scrollRect.viewport != null
            ? _scrollRect.viewport
            : _scrollRect.GetComponent<RectTransform>();

        // Размер области прокрутки
        Vector2 contentSize  = content.rect.size;
        Vector2 viewportSize = viewport.rect.size;

        Vector2 scrollableSize = contentSize - viewportSize;

        // Избегаем деления на ноль
        float nx = scrollableSize.x > 0.01f ? delta.x / scrollableSize.x : 0f;
        float ny = scrollableSize.y > 0.01f ? delta.y / scrollableSize.y : 0f;

        Vector2 newPos = _scrollRect.normalizedPosition + new Vector2(nx, ny);
        newPos.x = Mathf.Clamp01(newPos.x);
        newPos.y = Mathf.Clamp01(newPos.y);

        _scrollRect.normalizedPosition = newPos;
    }

    // ── Фокус через наведение курсора / навигацию ─
    public void OnPointerEnter(PointerEventData eventData) => _isFocused = true;
    public void OnPointerExit(PointerEventData eventData)  => _isFocused = false;

    /// <summary>
    /// Вызови этот метод вручную, чтобы задать фокус программно
    /// (например, при навигации кнопками геймпада между панелями).
    /// </summary>
    public void SetFocus(bool focused) => _isFocused = focused;

    /// <summary>
    /// Прокрутить в начало списка.
    /// </summary>
    public void ScrollToTop()    => _scrollRect.normalizedPosition = new Vector2(_scrollRect.normalizedPosition.x, 1f);

    /// <summary>
    /// Прокрутить в конец списка.
    /// </summary>
    public void ScrollToBottom() => _scrollRect.normalizedPosition = new Vector2(_scrollRect.normalizedPosition.x, 0f);
}
