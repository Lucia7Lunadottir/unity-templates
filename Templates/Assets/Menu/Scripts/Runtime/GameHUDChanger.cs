using TMPro;
using UnityEngine;

namespace PG.MenuManagement
{
    public class GameHUDChanger : MonoBehaviour
    {
        [SerializeField] private GameObject _gameHUD;
        [SerializeField] private GameObject[] _otherGameHUD;
        [SerializeField] private TMP_Dropdown _gameHudDropdown;
        public const string _SAVE_PARAMETER = "Game HUD Visible";

        private void Awake()
        {
            int index = PlayerPrefs.GetInt(_SAVE_PARAMETER, 1);
            bool visible = index == 1 ? true : false;
            if (_gameHUD != null) _gameHUD.SetActive(visible);
            if (_gameHudDropdown != null) _gameHudDropdown.SetValueWithoutNotify(index);

            if (_otherGameHUD.Length > 0)
            {
                foreach (GameObject gameHUD in _otherGameHUD)
                {
                    if (gameHUD == null)
                    {
                        continue;
                    }
                    gameHUD.SetActive(visible);
                }
            }
        }

        private void OnEnable()
        {
            if (_gameHudDropdown != null) _gameHudDropdown.onValueChanged.AddListener(SetVisibleHUD);
        }

        private void OnDisable()
        {
            if (_gameHudDropdown != null) _gameHudDropdown.onValueChanged.RemoveListener(SetVisibleHUD);
        }

        public void SetVisibleHUD(int value)
        {
            PlayerPrefs.SetInt(_SAVE_PARAMETER, value);
            if (_gameHUD != null) _gameHUD.SetActive(value == 1);
            if (_otherGameHUD.Length > 0)
            {
                foreach (GameObject gameHUD in _otherGameHUD)
                {
                    if (gameHUD == null)
                    {
                        continue;
                    }
                    gameHUD.SetActive(value == 1);
                }
            }
        }

    }
}
