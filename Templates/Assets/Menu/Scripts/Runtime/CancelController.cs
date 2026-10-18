using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PG.MenuManagement
{
    public class CancelController : MonoBehaviour
    {
        [SerializeField] private InputActionReference _cancelAction;
        [SerializeField] private UnityEvent _cancelEvent;
        [SerializeField] private Selectable _nextSelectable;

        private void OnEnable()
        {
            _cancelAction.action.performed += CancelInput;
        }
        private void OnDisable()
        {
            _cancelAction.action.performed -= CancelInput;
        }

        void CancelInput(InputAction.CallbackContext context) => Cancel();

        public void Cancel()
        {
            if (_cancelEvent != null) _cancelEvent.Invoke();
            if (_nextSelectable != null) _nextSelectable.Select();
        }
        public void SetNextSelectable(Selectable nextSelectable) => _nextSelectable = nextSelectable;
    }
}
