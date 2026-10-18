using UnityEngine;
using UnityEngine.InputSystem;

namespace PG.MenuManagement
{
    // Keeps an input action asset enabled while this object is active
    public class InputEnabler : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actionAsset;

        private void OnEnable()
        {
            if (_actionAsset != null) _actionAsset.Enable();
        }

        private void OnDisable()
        {
            if (_actionAsset != null) _actionAsset.Disable();
        }
    }
}
