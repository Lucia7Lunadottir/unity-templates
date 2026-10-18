using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PG.MenuManagement
{
    public class LanguageSelectorPanel : MonoBehaviour
    {
        [SerializeField] private string _fileName = "LanguageFirstAsTime.json";
        [SerializeField] private UIShowHide _panel;
        [SerializeField] private Selectable _targetSelectable;
        private string _filePath;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Awake()
        {
            _filePath = Path.Combine(Application.persistentDataPath, _fileName);
        }
        private void Start()
        {
            if (!File.Exists(_filePath))
            {
                _panel.Show();
                if (_targetSelectable != null) _targetSelectable.Select();
            }
        }
        public void Close()
        {
            File.WriteAllText(_filePath, " ");
            _panel.Hide();
        }
    }
}
