using InputSystemActionPrompts;
using UnityEngine;
using UnityEngine.InputSystem;
namespace PG.Input.RebindManagement
{
    public class InputSystemRebinder : MonoBehaviour
    {
        public enum KeyboardLayout { QWERTY, AZERTY, QWERTZ }
        private const string _playerPrefsKey = "Input Rebinds";
        public InputActionAsset inputActions;
        private InputActionRebindingExtensions.RebindingOperation _rebindingOperation;

        private string _baseRebinds;

        private void Awake()
        {
            _baseRebinds = inputActions.SaveBindingOverridesAsJson();

            string rebinds = PlayerPrefs.GetString(_playerPrefsKey);
            if (!string.IsNullOrEmpty(rebinds))
                inputActions.LoadBindingOverridesFromJson(rebinds);

            inputActions.Enable();
        }
        public void ResetSave()
        {
            PlayerPrefs.DeleteKey(_playerPrefsKey);
            inputActions.LoadBindingOverridesFromJson(_baseRebinds);
            InputDevicePromptSystem.RebuildBindingMaps();

            RebindCell[] rebindCells = FindObjectsByType<RebindCell>(FindObjectsInactive.Include);
            foreach (RebindCell cell in rebindCells)
            {
                cell.RefreshIcons();
            }
        }
        public void RebindKeyboard(InputAction inputAction, System.Action ended = null)
        {
            if (inputAction == null) return;

            int bindingIndex = InputUtils.FindBindingIndexByGroup(inputAction, "Keyboard&Mouse");
            if (bindingIndex == -1)
            {
                Debug.LogError($"[InputSystemRebinder] Binding group 'Keyboard&Mouse' not found for action: {inputAction.name}");
                return;
            }

            inputActions.Disable();
            _rebindingOperation = inputAction
                .PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => {
                    _rebindingOperation.Dispose();
                    inputActions.Enable();
                    ended?.Invoke();
                })
                .OnComplete(_ => {
                    RebindCompleted(ended);
                }).Start();
        }

        public void RebindGamepad(InputAction inputAction, System.Action ended = null)
        {
            if (inputAction == null) return;

            int bindingIndex = InputUtils.FindBindingIndexByGroup(inputAction, "Gamepad");
            if (bindingIndex == -1)
            {
                Debug.LogError($"[InputSystemRebinder] Binding group 'Gamepad' not found for action: {inputAction.name}");
                return;
            }

            inputActions.Disable();
            _rebindingOperation = inputAction
                .PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Gamepad>/start")
                .OnCancel(_ => {
                    _rebindingOperation.Dispose();
                    inputActions.Enable();
                    ended?.Invoke();
                })
                .OnComplete(_ => {
                    RebindCompleted(ended);
                }).Start();
        }
        void RebindCompleted(System.Action ended)
        {
            _rebindingOperation.Dispose();

            string rebinds = inputActions.SaveBindingOverridesAsJson();
            inputActions.Enable();
            PlayerPrefs.SetString(_playerPrefsKey, rebinds);

            ended?.Invoke();
        }

        public void SetLayout(KeyboardLayout layout)
        {
            inputActions.RemoveAllBindingOverrides();

            if (layout == KeyboardLayout.QWERTY)
            {
                SaveAndUpdate();
                return;
            }

            foreach (var map in inputActions.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    // Only apply layout switching to movement or specific actions if needed
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        var binding = action.bindings[i];
                        string path = binding.effectivePath;

                        // Ensure we only touch keyboard bindings and ignore gamepad
                        if (string.IsNullOrEmpty(path) || !path.Contains("<Keyboard>"))
                            continue;

                        // Ensure the binding belongs to keyboard group
                        if (!string.IsNullOrEmpty(binding.groups) && !binding.groups.Contains("Keyboard"))
                            continue;

                        string newPath = null;

                        if (layout == KeyboardLayout.AZERTY)
                        {
                            if (path.EndsWith("/w")) newPath = "<Keyboard>/z";
                            else if (path.EndsWith("/z")) newPath = "<Keyboard>/w";
                            else if (path.EndsWith("/a")) newPath = "<Keyboard>/q";
                            else if (path.EndsWith("/q")) newPath = "<Keyboard>/a";
                        }
                        else if (layout == KeyboardLayout.QWERTZ)
                        {
                            if (path.EndsWith("/y")) newPath = "<Keyboard>/z";
                            else if (path.EndsWith("/z")) newPath = "<Keyboard>/y";
                        }

                        if (newPath != null)
                        {
                            action.ApplyBindingOverride(i, newPath);
                        }
                    }
                }
            }

            SaveAndUpdate();
        }
        public void OnClick_SetAZERTY()
        {
            SetLayout(KeyboardLayout.AZERTY);
        }
        public void OnClick_SetQWERTZ()
        {
            SetLayout(KeyboardLayout.QWERTZ);
        }
        public void OnClick_SetQWERTY()
        {
            SetLayout(KeyboardLayout.QWERTY);
        }

        private void SaveAndUpdate()
        {
            string rebinds = inputActions.SaveBindingOverridesAsJson();
            PlayerPrefs.SetString(_playerPrefsKey, rebinds);

            InputDevicePromptSystem.RebuildBindingMaps();

            RebindCell[] rebindCells = FindObjectsByType<RebindCell>(FindObjectsInactive.Include);
            foreach (RebindCell cell in rebindCells)
            {
                cell.RefreshIcons();
            }
        }
    }
}
