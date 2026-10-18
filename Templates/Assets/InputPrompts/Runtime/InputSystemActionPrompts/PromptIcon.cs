using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace InputSystemActionPrompts
{
    [RequireComponent(typeof(Image))]
    public class PromptIcon : MonoBehaviour
    {
        /// <summary>
        /// This should be the full path, including binding map and action, eg "Player/Move"
        /// </summary>
        [SerializeField] private string m_Action = "Player/Move";
        public string action => m_Action;

        [SerializeField] private bool _setNativeSize = true;

        [Header("Device Override")]
        [SerializeField] private PromptDeviceOverride m_DeviceOverride = PromptDeviceOverride.AutoActiveDevice;

        /// <summary>
        /// The image to apply the prompt sprite to
        /// </summary>
        private Image m_Image;

        void Start()
        {
            m_Image = GetComponent<Image>();
            if (m_Image == null)
            {
                Debug.LogError("Image is null", this);
                return;
            }
            // OnEnable already subscribed and called RefreshIcon before Start.
        }

        private void OnEnable()
        {
            m_Image = GetComponent<Image>();
            if (m_Image == null)
            {
                Debug.LogError("Image is null", this);
                return;
            }

            // Subscribe here so it works correctly across disable/enable cycles
            InputDevicePromptSystem.OnActiveDeviceChanged += DeviceChanged;

            // InputDevicePromptSystem.OnDeviceChange now fires OnActiveDeviceChanged
            // when any gamepad connects, so no separate subscription needed here.

            RefreshIcon();
        }

        private void OnDisable()
        {
            InputDevicePromptSystem.OnActiveDeviceChanged -= DeviceChanged;
        }

        private void OnDestroy()
        {
            InputDevicePromptSystem.OnActiveDeviceChanged -= DeviceChanged;
        }

        private void DeviceChanged(InputDevice device)
        {
            RefreshIcon();
        }

        [ContextMenu("Refresh Icon")]
        public void RefreshIcon()
        {
            if (m_Image == null)
            {
                Debug.LogError("Image is null", this);
                return;
            }

            Sprite sourceSprite;

            // ÃÃ¯Ã°Ã¥Ã¤Ã¥Ã«Ã¿Ã¥Ã¬, Ã­Ã Ã¤Ã® Ã«Ã¨ Ã´Ã®Ã°Ã±Ã¨Ã²Ã¼ Ã²Ã¨Ã¯ Ã³Ã±Ã²Ã°Ã®Ã©Ã±Ã²Ã¢Ã 
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
                    break;
                default:
                    forcedType = InputDeviceType.Keyboard;
                    break;
            }

            if (forcedType.HasValue)
                sourceSprite = InputDevicePromptSystem.GetActionPathBindingSprite(m_Action, forcedType);
            else
                sourceSprite = InputDevicePromptSystem.GetActionPathBindingSprite(m_Action);

            if (sourceSprite == null)
            {
                //Debug.LogError("SourceSprite is null", this);
                return;
            }

            m_Image.sprite = sourceSprite;

            if (_setNativeSize)
                m_Image.SetNativeSize();

        }
    }
}
