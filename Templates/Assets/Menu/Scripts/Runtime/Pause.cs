using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PG.Tween;

namespace PG.MenuManagement
{
    public class Pause : MonoBehaviour
    {
        [SerializeField] private InputActionProperty _key;
        [SerializeField] private Button _selectButton;
        [SerializeField] private CanvasGroup _panel;
        public CanvasGroup panel => _panel;

        private UIShowHide _uiShowHide;

        // ������ static: ������ � ������ ������ ���� ���������� ���������
        private bool _isCurrentPanelActive;

        public static bool isPauseEnable { set; get; } = true;
        private float _standardTime = 1f;

        private void Awake()
        {
            isPauseEnable = true;
            _uiShowHide = GetComponent<UIShowHide>();
        }

        private void OnEnable() => _key.action.performed += InputChangePause;
        private void OnDisable() => _key.action.performed -= InputChangePause;

        private void OnDestroy()
        {
            // ���� ������ ������, ������������� ������� ����������� � ���������
            if (_isCurrentPanelActive)
            {
                UIManager.RegisterClose(_panel.gameObject);
            }
        }

        void InputChangePause(InputAction.CallbackContext context) => ChangePause();

        public void ChangePause()
        {
            if (!isPauseEnable) return;

            // ������ ��������
            if (!_isCurrentPanelActive)
            {
                // ���� �����-�� ������ ������ ��� ������� � ���������� ����
                if (UIManager.IsAnyPanelOpen) return;

                // �������� ������ ����� � UIManager
                if (UIManager.RequestOpen(_panel.gameObject))
                {
                    OpenPause();
                }
            }
            // ������ ��������
            else
            {
                // ���������, ������ ���� �� ���� �������
                if (UIManager.RegisterClose(_panel.gameObject))
                {
                    ClosePause();
                }
            }
        }

        private void OpenPause()
        {
            _isCurrentPanelActive = true;
            //_standardTime = Time.timeScale;
            Time.timeScale = 0f;

            Menu.OnChangeCursorVisible(true);

            if (_uiShowHide)
                _uiShowHide.Show();
            else
                _panel.gameObject.SetActive(true);

            _selectButton.Select();

            _panel.interactable = true;
            _panel.blocksRaycasts = true;
            _panel.OnAlphaTween(1f, 0.25f, true);
        }

        public void ClosePause()
        {
            _isCurrentPanelActive = false;
            Time.timeScale = _standardTime;

            if (_uiShowHide)
            {
                _uiShowHide.Hide(() => Menu.OnChangeCursorVisible(false));
            }
            else
            {
                _panel.OnAlphaTween(0f, 0.25f, true, null, () =>
                {
                    _panel.gameObject.SetActive(false);
                    Menu.OnChangeCursorVisible(false);
                });
            }

            _panel.interactable = false;
            _panel.blocksRaycasts = false;
        }
    }
}
