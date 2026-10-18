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

        // Номер последнего открытия/закрытия: устаревший fade или callback не должен перебить новое состояние
        private int _stateGen;

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
            int gen = ++_stateGen;
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

            if (_uiShowHide)
                _panel.OnAlphaTween(1f, 0.25f, true);
            else
                FadePanel(1f, 0.25f, gen, null);
        }

        public void ClosePause()
        {
            int gen = ++_stateGen;
            _isCurrentPanelActive = false;
            Time.timeScale = _standardTime;

            if (_uiShowHide)
            {
                _uiShowHide.Hide(() =>
                {
                    if (gen == _stateGen) Menu.OnChangeCursorVisible(false);
                });
            }
            else
            {
                FadePanel(0f, 0.25f, gen, () =>
                {
                    _panel.gameObject.SetActive(false);
                    Menu.OnChangeCursorVisible(false);
                });
            }

            _panel.interactable = false;
            _panel.blocksRaycasts = false;
        }

        // Fade по unscaled времени; обрывается, если после него пауза открыли/закрыли снова
        private async void FadePanel(float to, float time, int gen, System.Action onComplete)
        {
            float from = _panel.alpha;
            float elapsed = 0f;

            while (elapsed < time)
            {
                if (this == null || gen != _stateGen) return;
                _panel.alpha = Mathf.Lerp(from, to, elapsed / time);
                elapsed += Time.unscaledDeltaTime;
                await Awaitable.NextFrameAsync();
            }

            if (this == null || gen != _stateGen) return;
            _panel.alpha = to;
            onComplete?.Invoke();
        }
    }
}
