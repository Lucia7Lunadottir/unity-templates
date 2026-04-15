using UnityEngine;
using UnityEngine.Audio;

public class ImpactAudioResourceStacker : MonoBehaviour
{
    public AudioResource impactResource;
    public int poolSize = 3; // Сколько звуков может звучать одновременно
    public float velocityThreshold = 1.0f;
    public float volumeMultiplier = 0.2f;

    private AudioSource[] _sources;
    private int _currentSourceIndex = 0;

    void Awake()
    {
        _sources = new AudioSource[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            _sources[i] = gameObject.AddComponent<AudioSource>();
            _sources[i].spatialBlend = 1.0f; // 3D звук
            _sources[i].playOnAwake = false;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        float impactForce = collision.relativeVelocity.magnitude;

        if (impactForce > velocityThreshold && impactResource != null)
        {
            AudioSource source = _sources[_currentSourceIndex];

            source.resource = impactResource;
            source.pitch = Random.Range(0.85f, 1.15f);
            source.volume = Mathf.Clamp(impactForce * volumeMultiplier, 0.05f, 1.0f);
            source.Play();

            // Переходим к следующему источнику в пуле, чтобы не прерывать текущий
            _currentSourceIndex = (_currentSourceIndex + 1) % poolSize;
        }
    }
}