using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class MenuSoundSettings : MonoBehaviour, ISaveable
{
    [SerializeField] private AudioMixer _audioMixer;
    [SerializeField] private SoundCell[] _soundCells;
    [SerializeField] private string _fileName = "SoundSettings.json";
    private string _path;

    private SaveSoundSettings _saveSoundSettings = new SaveSoundSettings();

    [System.Serializable]
    public struct SoundCell
    {
        public string parameter;
        public Slider slider;
    }
    private void Awake()
    {
        _path = Path.Combine(Application.persistentDataPath, _fileName);
        Load();
    }
    private void OnEnable()
    {
        for (int i = 0; i < _soundCells.Length; i++)
        {
            int index = i; // Local copy for closure
            _soundCells[i].slider.onValueChanged.AddListener(val => OnSoundChange(index));
        }
    }

    private void OnDisable()
    {
        for (int i = 0; i < _soundCells.Length; i++)
        {
            // This is still problematic with lambdas. 
            // Best practice: Clear all listeners if the slider is dedicated to this script
            _soundCells[i].slider.onValueChanged.RemoveAllListeners();
        }
    }
    void OnSoundChange(int index, bool save = true)
    {
        _audioMixer.SetFloat(_soundCells[index].parameter, Mathf.Log10(_soundCells[index].slider.value) * 20);
        if (save)
        {
            Save();
        }
    }
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        string json = File.ReadAllText(_path);
        JsonUtility.FromJsonOverwrite(json, _saveSoundSettings);

        for (int i = 0; i < _soundCells.Length; i++)
        {
            for (int a = 0; a < _saveSoundSettings.soundElements.Length; a++)
            {
                if (_soundCells[i].parameter == _saveSoundSettings.soundElements[a].parameter)
                {
                    _soundCells[i].slider.SetValueWithoutNotify(_saveSoundSettings.soundElements[a].value);
                    OnSoundChange(i, false);
                    break;
                }
            }
        }

        
    }
    public void Save()
    {
        _saveSoundSettings.soundElements = new SaveSoundSettings.SoundElement[_soundCells.Length];
        for (int i = 0; i < _soundCells.Length; i++)
        {
            _saveSoundSettings.soundElements[i] = new SaveSoundSettings.SoundElement { parameter = _soundCells[i].parameter, value = _soundCells[i].slider.value };
        }

        string json = JsonUtility.ToJson(_saveSoundSettings);
        File.WriteAllText(_path, json);
    }



    [System.Serializable]
    public class SaveSoundSettings
    {
        public SoundElement[] soundElements;
        [System.Serializable]
        public struct SoundElement
        {
            public string parameter;
            public float value;
        }
    }
}
