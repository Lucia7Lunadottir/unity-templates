using TMPro;
using UnityEngine;

namespace PG.MenuManagement
{
    public class FPSVisibleChanger : MonoBehaviour
    {
        [SerializeField] private GameObject _fpsHUD;
        [SerializeField] private TMP_Dropdown _fpsDropdown;
        public const string _SAVE_PARAMETER = "Game FPS Visible";

        private void Awake()
        {
            int index = PlayerPrefs.GetInt(_SAVE_PARAMETER, 0);
            bool visible = index == 1 ? true : false;
            if (_fpsHUD != null) _fpsHUD.SetActive(visible);
            if (_fpsDropdown != null) _fpsDropdown.SetValueWithoutNotify(index);
        }

        private void OnEnable()
        {
            if (_fpsDropdown != null) _fpsDropdown.onValueChanged.AddListener(SetVisibleHUD);
        }

        private void OnDisable()
        {
            if (_fpsDropdown != null) _fpsDropdown.onValueChanged.RemoveListener(SetVisibleHUD);
        }

        public void SetVisibleHUD(int value)
        {
            PlayerPrefs.SetInt(_SAVE_PARAMETER, value);
            if (_fpsHUD != null) _fpsHUD.SetActive(value == 1);
        }

    }
}
