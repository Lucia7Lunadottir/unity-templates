using InputSystemActionPrompts;
using PG.Tween;
using System.Threading.Tasks;
using UnityEngine;
namespace PG.Input.RebindManagement
{
    public class RebindCell : MonoBehaviour
    {
        [SerializeField] private PromptIcon _promptIconKeyboard;
        [SerializeField] private PromptIcon _promptIconGamepad;
        [SerializeField] private InputSystemRebinder _inputSystemRebinder;
        private void Awake()
        {
            if (_inputSystemRebinder == null)
            {
                _inputSystemRebinder = FindAnyObjectByType<InputSystemRebinder>(FindObjectsInactive.Include);
            }
        }

        public void RefreshIcons()
        {
            if (_promptIconGamepad != null) _promptIconGamepad.RefreshIcon();
            if (_promptIconKeyboard != null) _promptIconKeyboard.RefreshIcon();
        }

        public void RebindGamepad()
        {
            if (_inputSystemRebinder != null) _inputSystemRebinder.RebindGamepad(_inputSystemRebinder.inputActions.GetActionFromPath(_promptIconGamepad.action), RebindGamepadComplete);
        }
        public void RebindKeyboard()
        {
            if (_inputSystemRebinder != null) _inputSystemRebinder.RebindKeyboard(_inputSystemRebinder.inputActions.GetActionFromPath(_promptIconKeyboard.action), RebindKeyboardComplete);
        }
        void RebindGamepadComplete()
        {
            InputDevicePromptSystem.RebuildBindingMaps();
            _promptIconGamepad.RefreshIcon();

        }
        void RebindKeyboardComplete()
        {
            InputDevicePromptSystem.RebuildBindingMaps();
            _promptIconKeyboard.RefreshIcon();
        }
    }
}
