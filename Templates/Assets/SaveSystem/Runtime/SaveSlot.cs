using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PG.SaveSystem
{
    // Menu button for one save slot: selects the slot, then fires New Game or Load
    public class SaveSlot : MonoBehaviour
    {
        [SerializeField] private int _index = 1;
        [SerializeField] private TMP_Text _dateText;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Button _deleteButton;
        [SerializeField] private Button _loadButton;
        [SerializeField] private Button _confirmDeleteButton;

        [SerializeField] private UnityEvent _newGameEvent;
        [SerializeField] private UnityEvent _loadEvent;

        public int index => _index;

        void Start()
        {
            RefreshUI();
            if (_deleteButton != null) _deleteButton.onClick.AddListener(RegisterDeleteButton);
        }

        private void OnDestroy()
        {
            if (_deleteButton != null) _deleteButton.onClick.RemoveListener(RegisterDeleteButton);
        }

        // Delete asks for confirmation; the confirm button is shared between slots
        void RegisterDeleteButton()
        {
            if (_confirmDeleteButton == null)
            {
                Delete();
                return;
            }
            _confirmDeleteButton.onClick.RemoveAllListeners();
            _confirmDeleteButton.onClick.AddListener(Delete);
        }

        public void Load()
        {
            RefreshUI();
        }

        public void SelectIndex()
        {
            SaveManager.saveIndex = _index;
            GuidListSaveUtility.ClearCache();
            if (SaveManager.SlotExists(_index))
            {
                _loadEvent?.Invoke();
            }
            else
            {
                _newGameEvent?.Invoke();
            }
        }

        public void Delete()
        {
            SaveManager.Storage.DeleteDirectory(SaveManager.GetTargetSaveFolderPath(_index, false));
            SaveManager.Storage.DeleteDirectory(SaveManager.GetCheckpointFolderPath(_index));
            RefreshUI();
        }

        public void RefreshUI()
        {
            if (_deleteButton != null) _deleteButton.gameObject.SetActive(SaveManager.SlotExists(_index));
            if (_dateText != null) _dateText.text = GetSaveDate(_index);
        }

        public static string GetSaveDate(int index)
        {
            string path = SaveManager.GetTargetSaveFolderPath(index, false);
            if (!Directory.Exists(path)) return "";

            System.DateTime lastWriteTime = Directory.GetLastWriteTime(path);
            return lastWriteTime.ToShortDateString() + " " + lastWriteTime.ToLongTimeString();
        }
    }
}
