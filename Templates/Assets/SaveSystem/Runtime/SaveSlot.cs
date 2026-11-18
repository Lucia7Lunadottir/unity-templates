using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Load();
        _deleteButton.onClick.AddListener(RegisterDeleteButton);
    }

    private void OnDestroy()
    {
        _deleteButton.onClick.RemoveListener(RegisterDeleteButton);
    }

    void RegisterDeleteButton()
    {
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
        if (Directory.Exists(SaveManager.GetTargetSaveFolderPath(_index, false)))
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
        Directory.Delete(SaveManager.GetTargetSaveFolderPath(_index, false), true);
        RefreshUI();
    }
    // Update is called once per frame
    public void RefreshUI()
    {
        _deleteButton.gameObject.SetActive(Directory.Exists(SaveManager.GetTargetSaveFolderPath(_index, false)));
        _dateText.text = GetSaveDate(_index);
    }
    
    public static string GetSaveDate(int index)
    {

        if (Directory.Exists(SaveManager.GetTargetSaveFolderPath(index, false)))
        {
            System.DateTime lastWriteTime = Directory.GetLastWriteTime(SaveManager.GetTargetSaveFolderPath(index, false));
        
            return lastWriteTime.ToShortDateString() + " " + lastWriteTime.ToLongTimeString();
        }

        return ""; 
    }
}
