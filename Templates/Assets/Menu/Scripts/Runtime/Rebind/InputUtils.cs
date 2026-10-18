using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace PG.Input.RebindManagement
{
    public static class InputUtils
    {
        public static InputAction GetActionFromPath(this InputActionAsset inputActions, string actionPath)
        {
            if (inputActions == null)
            {
                Debug.LogError("InputActionAsset is not assigned!");
                return null;
            }

            var parts = actionPath.Split('/');

            if (parts.Length != 2)
            {
                Debug.LogError($"Invalid action path: {actionPath}. Expected format Map/Action");
                return null;
            }

            string mapName = parts[0];
            string actionName = parts[1];

            var map = inputActions.FindActionMap(mapName, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"ActionMap '{mapName}' not found in InputActionAsset!");
                return null;
            }

            var action = map.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
            {
                Debug.LogError($"Action '{actionName}' not found in ActionMap '{mapName}'!");
                return null;
            }

            return action;
        }
        public static int FindBindingIndexByGroup(InputAction action, string groupSubstring)
        {
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var groups = bindings[i].groups;

                if (string.IsNullOrEmpty(groups))
                    continue;

                if (groups.IndexOf(groupSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            return -1;
        }
    }
}
