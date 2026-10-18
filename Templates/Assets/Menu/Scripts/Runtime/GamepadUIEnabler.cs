using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PG.MenuManagement
{
    public class GamepadUIEnabler : MonoBehaviour
    {
        [SerializeField] private GameObject[] _gameObjects;

        private bool _isGamepadMode = false;

        private void Start()
        {
            bool useGamepad = Gamepad.current != null && Gamepad.current.IsActuated();
            _isGamepadMode = useGamepad;
            SetUIActive(useGamepad);
        }

        void Update()
        {
            bool gamepadActive = Gamepad.current != null && Gamepad.current.IsActuated();

            bool kbmActive = (Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                             (Mouse.current != null && (Mouse.current.leftButton.isPressed ||
                                                        Mouse.current.rightButton.isPressed ||
                                                        Mouse.current.delta.magnitude > 0.1f));

            if (gamepadActive)
            {
                SetGamepadMode(true);
            }
            else if (kbmActive)
            {
                SetGamepadMode(false);
            }
        }

        private void SetGamepadMode(bool useGamepad)
        {
            if (_isGamepadMode != useGamepad)
            {
                _isGamepadMode = useGamepad;
                SetUIActive(useGamepad);
            }
        }

        private void SetUIActive(bool state)
        {
            foreach (var obj in _gameObjects)
            {
                if (obj != null)
                {
                    if (obj.TryGetComponent(out UIShowHide panel))
                    {
                        if (state) panel.Show();
                        else panel.Hide();
                    }
                    else
                    {
                        obj.SetActive(state);
                    }
                }
            }
        }
    }
}
