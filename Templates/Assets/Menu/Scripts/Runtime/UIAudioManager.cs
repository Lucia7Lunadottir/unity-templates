using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Audio; // Обязательно для AudioResource

namespace PG.MenuManagement
{
    public partial class UIAudioManager : MonoBehaviour
    {
        [AutoStaticsCleanup]
        public static UIAudioManager Instance;

        [Header("Global UI Sounds")]
        [SerializeField] private AudioResource _hoverClip;
        [SerializeField] private AudioResource _clickClip;
        [SerializeField] private AudioResource _selectClip;

        [SerializeField] private AudioSource _audioSource;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // DontDestroyOnLoad(gameObject); 
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        public void PlayHover() => Play(_hoverClip);
        public void PlayClick() => Play(_clickClip);
        public void PlaySelect() => Play(_selectClip);

        private void Play(AudioResource resource)
        {
            if (resource != null && _audioSource != null)
            {

                AudioSource audioSource = Instantiate(_audioSource);

                audioSource.resource = resource;
                audioSource.Play();


                if (resource is AudioClip clip)
                {
                    Destroy(audioSource.gameObject, clip.length);
                }
                else
                {
                    Destroy(audioSource.gameObject, 10f);
                }

            }
        }
    }
}