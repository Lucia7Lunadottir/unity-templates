using UnityEngine;
public class AudioManager : MonoBehaviour
{
    [field:SerializeField] public AudioManagerProfile profile {  get; private set; }
    [SerializeField] private AudioSource[] _audioSources;

    public static AudioManager instance;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            transform.parent = null;
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
