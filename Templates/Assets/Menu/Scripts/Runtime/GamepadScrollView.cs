using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace PG.MenuManagement
{
    [RequireComponent(typeof(ScrollRect))]
    public class GamepadScrollView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Scroll Settings")]
        [Tooltip("Stick scroll speed")]
        public float scrollSpeed = 300f;

        [Tooltip("D-Pad / LB / RB scroll speed")]
        public float buttonScrollSpeed = 600f;

        [Tooltip("Invert Y axis")]
        public bool invertY = false;

        [Tooltip("Invert X axis")]
        public bool invertX = false;

        [Header("Axis")]
        [Tooltip("Allow horizontal scroll")]
        public bool allowHorizontal = true;

        [Tooltip("Allow vertical scroll")]
        public bool allowVertical = true;

        [Header("Focus")]
        [Tooltip("Scroll only while the cursor is over the ScrollView (false = always)")]
        public bool requireFocus = true;

        // ──────────────────────────────────────────────
        private ScrollRect _scrollRect;
        private bool _isFocused = false;

        private Gamepad _gamepad;

        private void Awake()
        {
            _scrollRect = GetComponent<ScrollRect>();
        }

        private void Update()
        {
            _gamepad = Gamepad.current;
            if (_gamepad == null) return;

            if (requireFocus && !_isFocused) return;

            Vector2 delta = Vector2.zero;

            Vector2 leftStick  = _gamepad.leftStick.ReadValue();
            Vector2 rightStick = _gamepad.rightStick.ReadValue();

            Vector2 stickInput = leftStick.magnitude > rightStick.magnitude ? leftStick : rightStick;

            delta += stickInput * scrollSpeed * Time.unscaledDeltaTime;

            // ── D-Pad ─────────────────────────────────────
            if (_gamepad.dpad.up.isPressed)    delta.y += buttonScrollSpeed * Time.unscaledDeltaTime;
            if (_gamepad.dpad.down.isPressed)  delta.y -= buttonScrollSpeed * Time.unscaledDeltaTime;
            if (_gamepad.dpad.left.isPressed)  delta.x -= buttonScrollSpeed * Time.unscaledDeltaTime;
            if (_gamepad.dpad.right.isPressed) delta.x += buttonScrollSpeed * Time.unscaledDeltaTime;

            if (_gamepad.leftShoulder.isPressed)  delta.y += buttonScrollSpeed * Time.unscaledDeltaTime;
            if (_gamepad.rightShoulder.isPressed) delta.y -= buttonScrollSpeed * Time.unscaledDeltaTime;

            if (invertY) delta.y = -delta.y;
            if (invertX) delta.x = -delta.x;

            if (!allowHorizontal) delta.x = 0f;
            if (!allowVertical)   delta.y = 0f;

            if (delta == Vector2.zero) return;

            ApplyScroll(delta);
        }

        private void ApplyScroll(Vector2 delta)
        {
            RectTransform content  = _scrollRect.content;
            RectTransform viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : _scrollRect.GetComponent<RectTransform>();

            Vector2 contentSize  = content.rect.size;
            Vector2 viewportSize = viewport.rect.size;

            Vector2 scrollableSize = contentSize - viewportSize;

            float nx = scrollableSize.x > 0.01f ? delta.x / scrollableSize.x : 0f;
            float ny = scrollableSize.y > 0.01f ? delta.y / scrollableSize.y : 0f;

            Vector2 newPos = _scrollRect.normalizedPosition + new Vector2(nx, ny);
            newPos.x = Mathf.Clamp01(newPos.x);
            newPos.y = Mathf.Clamp01(newPos.y);

            _scrollRect.normalizedPosition = newPos;
        }

        public void OnPointerEnter(PointerEventData eventData) => _isFocused = true;
        public void OnPointerExit(PointerEventData eventData)  => _isFocused = false;

        public void SetFocus(bool focused) => _isFocused = focused;

        public void ScrollToTop()    => _scrollRect.normalizedPosition = new Vector2(_scrollRect.normalizedPosition.x, 1f);

        public void ScrollToBottom() => _scrollRect.normalizedPosition = new Vector2(_scrollRect.normalizedPosition.x, 0f);
    }
}
