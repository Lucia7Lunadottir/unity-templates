using PG.Tween;
using System.Collections.Generic;
using UnityEngine;

public class AmbientMusicChanger : MonoBehaviour
{
    [SerializeField] private MusicElement[] _musicElements;
    [SerializeField] private int _targetIndex;
    [System.Serializable]
    public class MusicElement
    {
        public string objectName;
        public AudioSource audioSource;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        SetMusicIndex(_targetIndex);
    }

    public int GetTargetIndex(string value)
    {
        for (int i = 0; i < _musicElements.Length; i++)
        {
            if (_musicElements[i].objectName == value)
            {
                return i;
            }
        }
        return 0;
    }

    public void SetMusic(string value)
    {
        for (int i = 0; i < _musicElements.Length; i++)
        {
            int currentIndex = i;

            if (value == _musicElements[i].objectName)
            {
                _targetIndex = i;
                _musicElements[currentIndex].audioSource.Play();
                PGTween.OnValueTween(0f, 1f, 0.3f, true, v => _musicElements[currentIndex].audioSource.volume = v);
            }
            else
            {
                PGTween.OnValueTween(1f, 0f, 0.3f, true, v => _musicElements[currentIndex].audioSource.volume = v, null, _musicElements[currentIndex].audioSource.Stop);
            }
        }
    }

    public void SetMusicIndex(int value)
    {
        _targetIndex = value;
        for (int i = 0; i < _musicElements.Length; i++)
        {
            // Создаем локальную переменную внутри цикла!
            int currentIndex = i;

            if (value == currentIndex)
            {
                _musicElements[currentIndex].audioSource.Play();
                PGTween.OnValueTween(0f, 1f, 0.3f, true,
                    v => _musicElements[currentIndex].audioSource.volume = v);
            }
            else
            {
                PGTween.OnValueTween(1f, 0f, 0.3f, true,
                    v => _musicElements[currentIndex].audioSource.volume = v,
                    null,
                    _musicElements[currentIndex].audioSource.Stop);
            }
        }
    }
}
