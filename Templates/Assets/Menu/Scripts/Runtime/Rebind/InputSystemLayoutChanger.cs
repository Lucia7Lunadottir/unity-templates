using UnityEngine;
using static PG.Input.RebindManagement.InputSystemRebinder;
namespace PG.Input.RebindManagement
{
    public class InputSystemLayoutChanger : MonoBehaviour
    {
        [SerializeField] private InputSystemRebinder _inputSystemRebinder;
        private void Awake()
        {
            if (_inputSystemRebinder == null)
            {
                _inputSystemRebinder = FindAnyObjectByType<InputSystemRebinder>(FindObjectsInactive.Include);
            }
        }
        public void OnDropdownValueChanged(int value)
        {
            switch (value)
            {
                default:
                    OnClick_SetQWERTY();
                    break;
                case 1:
                    OnClick_SetQWERTZ();
                    break;
                case 2:
                    OnClick_SetAZERTY();
                    break;
            }
        }
        public void OnClick_SetAZERTY()
        {
            _inputSystemRebinder.SetLayout(KeyboardLayout.AZERTY);
        }
        public void OnClick_SetQWERTZ()
        {
            _inputSystemRebinder.SetLayout(KeyboardLayout.QWERTZ);
        }
        public void OnClick_SetQWERTY()
        {
            _inputSystemRebinder.SetLayout(KeyboardLayout.QWERTY);
        }
    }
}
