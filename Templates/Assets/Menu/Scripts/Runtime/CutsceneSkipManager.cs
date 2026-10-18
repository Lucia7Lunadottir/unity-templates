using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace PG.MenuManagement
{
    public class CutsceneSkipManager : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private PlayableDirector _playableDirector;
        [SerializeField] private UIShowHide _interactPanel;
        [SerializeField] private Image _skipSlider;

        [Header("Input Settings")]
        [Tooltip("Hold this action to skip (Space, Gamepad South/X)")]
        [SerializeField] private InputActionProperty _skipActionProperty;

        [Header("Settings")]
        [SerializeField] private float _skipHoldDuration = 1.5f;
        [SerializeField] private float _panelHideDelay = 3f;

        private float _currentHoldTime;
        private float _inactivityTimer;
        private bool _isSkipped;
        private bool _isSkipStart;

        private IDisposable _anyButtonListener;

        private void OnEnable()
        {
            _currentHoldTime = 0;
            _skipSlider.fillAmount = 0;
            _inactivityTimer = 0;
            _isSkipped = false;

            _skipActionProperty.action?.Enable();

            _anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyInput);
        }

        private void OnDisable()
        {
            if (_anyButtonListener != null) _anyButtonListener.Dispose();
            _skipActionProperty.action?.Disable();
        }

        private void Update()
        {
            if (_isSkipped) return;

            HandlePanelVisibility();
            HandleSkipLogic();
        }

        private void OnAnyInput(InputControl inputControl)
        {
            if (_isSkipped) return;

            _inactivityTimer = 0;
            _isSkipStart = false;
            if (_interactPanel != null) _interactPanel.Show();
        }

        private void HandlePanelVisibility()
        {
            _inactivityTimer += Time.deltaTime;

            if (_inactivityTimer >= _panelHideDelay)
            {
                if (_interactPanel != null) _interactPanel.Hide();
                _isSkipStart = false;
            }
        }

        private void HandleSkipLogic()
        {
            if (_skipActionProperty.action.IsPressed())
            {
                if (!_isSkipStart)
                {
                    if (_interactPanel != null) _interactPanel.Show();
                    _isSkipStart = true;
                }
                _inactivityTimer = 0;

                _currentHoldTime += Time.deltaTime;
                _skipSlider.fillAmount = _currentHoldTime / _skipHoldDuration;

                if (_currentHoldTime >= _skipHoldDuration)
                {
                    PerformSkip();
                }
            }
            else
            {
                _currentHoldTime -= Time.deltaTime * 2;
                _currentHoldTime = Mathf.Max(0, _currentHoldTime);
                _skipSlider.fillAmount = _currentHoldTime / _skipHoldDuration;
            }
        }

        private void PerformSkip()
        {
            _isSkipped = true;
            if (_interactPanel != null) _interactPanel.Hide();
            _isSkipStart = false;
            if (_anyButtonListener != null) _anyButtonListener.Dispose();

            if (_playableDirector != null)
            {
                _playableDirector.time = _playableDirector.duration;
                _playableDirector.Evaluate();
            }
        }
    }
}
