using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InputSystemActionPrompts
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class PromptText : MonoBehaviour
    {
        [SerializeField] protected InputActionAsset m_InputActionAsset;

        [Header("Device Override")]
        [SerializeField] private PromptDeviceOverride m_DeviceOverride = PromptDeviceOverride.AutoActiveDevice;

        protected TextMeshProUGUI m_TextField;
        protected string m_OriginalText;

        public void SetOriginalText(string text) => m_OriginalText = text;
        public void SetOriginalText()
        {
            if (m_TextField == null)
            {
                if (!TryGetComponent(out m_TextField))
                {
                    return;
                }
            }
            m_OriginalText = m_TextField.text;
        }
        protected virtual void Start()
        {
            if (!TryGetComponent(out m_TextField)) return;

            // Важно: первичный захват текста
            if (string.IsNullOrEmpty(m_OriginalText))
                m_OriginalText = m_TextField.text;

            SetOriginalText();
            RefreshText();
            InputDevicePromptSystem.OnActiveDeviceChanged += DeviceChanged;
        }

        protected virtual void OnDestroy()
        {
            InputDevicePromptSystem.OnActiveDeviceChanged -= DeviceChanged;
        }
        public void RefreshTextWithUpdate()
        {
            SetOriginalText();
            RefreshText();
        }
        protected void DeviceChanged(InputDevice device)
        {
            RefreshText();
        }


        public void RefreshText()
        {
            if (m_TextField == null) return;

            InputDeviceType? forcedType = null;
            switch (m_DeviceOverride)
            {
                case PromptDeviceOverride.Keyboard:
                    forcedType = InputDeviceType.Keyboard;
                    break;
                case PromptDeviceOverride.Gamepad:
                    forcedType = InputDeviceType.GamePad;
                    break;
                case PromptDeviceOverride.AutoActiveDevice:
                default:
                    forcedType = null;
                    break;
            }

            if (forcedType.HasValue)
                m_TextField.text = InputDevicePromptSystem.InsertPromptSprites(m_OriginalText, forcedType.Value);
            else
                m_TextField.text = InputDevicePromptSystem.InsertPromptSprites(m_OriginalText);
        }
    }
}
