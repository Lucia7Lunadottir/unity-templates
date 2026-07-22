using Unity.Scripting.LifecycleManagement;
using UnityEngine;
public partial class AudioManager : MonoBehaviour
{
    [field:SerializeField] public AudioManagerProfile profile {  get; private set; }
    [SerializeField] private AudioSource[] _audioSources;

    [AutoStaticsCleanup]
    public static AudioManager instance;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            transformHandle.SetParent(default);
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Play(string audioName, int index = 0)
    {
        
        AudioSource audioSource = Instantiate(_audioSources[index]);
        audioSource.resource = profile.GetAudioResource(audioName);
        audioSource.Play();
        Destroy(audioSource.gameObject, 10f);
    }
}
