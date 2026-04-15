using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName ="PG/Audio Manager Profile")]
public class AudioManagerProfile : ScriptableObject
{
    [field:SerializeField] public AudioResource[] audioResources {  get; private set; }
    [field: SerializeField] public AudioManagerProfile[] audioManagerProfiles { get; private set; }

    public AudioResource GetAudioResource(string name)
    {
        for (int i = 0; i < audioResources.Length; i++)
        {
            if (audioResources[i].name == name)
            {
                return audioResources[i];
            }
        }
        for (int i = 0; i < audioManagerProfiles.Length; i++)
        {
            AudioResource audioResource = audioManagerProfiles[i].GetAudioResource(name);
            if (audioResource != null)
            {
                return (audioResource);
            }
        }
        return null;
    }
}
