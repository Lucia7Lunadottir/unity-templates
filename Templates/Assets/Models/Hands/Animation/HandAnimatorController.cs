using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
    /// <summary>
    /// Reads values on current controller Select and Activate actions and uses them to drive hand animations.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HandAnimatorController : MonoBehaviour
    {
        [Header("Animator Parameters")]
        [SerializeField]
        [Tooltip("Float parameter corresponding to select value.")]        
        private string selectFloat = "Select";

        [SerializeField]
        [Tooltip("Float parameter corresponding to activate value.")]
        private string activateFloat = "Activate";

        [SerializeField] private Animator _animator;
    [SerializeField] private InputActionProperty _grab;
    [SerializeField] private InputActionProperty _activate;

        private void Start()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            //if (controllerManager != null)
            //{
            //    if (controllerManager.UIState.Enabled)
            //    {
            //        _animator.SetBool(UIStateBool, true);
            //    }
            //    else
            //    {
            //        _animator.SetBool(UIStateBool, false);
            //    }

            //    if (controllerManager.TeleportState.Enabled)
            //    {
            //        _animator.SetBool(teleportStateBool, true);
            //    }
            //    else
            //    {
            //        _animator.SetBool(teleportStateBool, false);
            //    }
            //}

            _animator.SetFloat(selectFloat, _grab.action.ReadValue<float>());
            _animator.SetFloat(activateFloat, _activate.action.ReadValue<float>());
        }
    }