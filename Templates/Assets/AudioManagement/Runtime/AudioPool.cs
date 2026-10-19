using System.Collections;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Pool;

public partial class AudioPool : MonoBehaviour
{
    [AutoStaticsCleanup]
    public static AudioPool Instance { get; private set; }

    [Header("Pool Settings")]
    [SerializeField] private AudioSource audioSourcePrefab;
    [SerializeField] private int defaultCapacity = 20;
    [SerializeField] private int maxSize = 50;

    private ObjectPool<AudioSource> _pool;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transformHandle.SetParent(default);
        DontDestroyOnLoad(gameObject);

        _pool = new ObjectPool<AudioSource>(
            createFunc: CreateAudioSource,
            actionOnGet: OnGetAudioSource,
            actionOnRelease: OnReleaseAudioSource,
            actionOnDestroy: OnDestroyAudioSource,
            collectionCheck: true,
            defaultCapacity: defaultCapacity,
                maxSize: maxSize
        );
    }

    private AudioSource CreateAudioSource()
    {
        AudioSource source = Instantiate(audioSourcePrefab, transform);
        source.gameObject.SetActive(false);
        return source;
    }

    private void OnGetAudioSource(AudioSource source)
    {
        source.gameObject.SetActive(true);
    }

    private void OnReleaseAudioSource(AudioSource source)
    {
        source.Stop();

        // Корректно очищаем оба варианта, чтобы не держать ссылки в памяти
        source.clip = null;
        source.resource = null;

        source.outputAudioMixerGroup = null;
        source.gameObject.SetActive(false);
    }

    private void OnDestroyAudioSource(AudioSource source)
    {
        Destroy(source.gameObject);
    }

    /// <summary>
    /// Воспроизвести 2D звук через AudioResource
    /// </summary>
    public void PlaySound2D(AudioResource resource, AudioMixerGroup mixerGroup = null, float volume = 1f, float pitch = 1f)
    {
        if (resource == null) return;

        AudioSource source = _pool.Get();

        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = mixerGroup;
        source.resource = resource;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();

        // Запускаем проверку длительности ПОСЛЕ Play()
        StartCoroutine(ReturnToPoolAfterPlayback(source, pitch));
    }

    /// <summary>
    /// Воспроизвести 3D звук через AudioResource
    /// </summary>
    public void PlaySound3D(AudioResource resource, Vector3 position, AudioMixerGroup mixerGroup = null, float volume = 1f, float pitch = 1f)
    {
        if (resource == null) return;

        AudioSource source = _pool.Get();

        source.transform.position = position;
        source.spatialBlend = 1f;
        source.outputAudioMixerGroup = mixerGroup;
        source.resource = resource;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();

        // Запускаем проверку длительности ПОСЛЕ Play()
        StartCoroutine(ReturnToPoolAfterPlayback(source, pitch));
    }

    private IEnumerator ReturnToPoolAfterPlayback(AudioSource source, float pitch)
    {
        // Даем Unity один кадр, чтобы AudioSource полностью инициализировал ресурс и выбрал клип
        yield return null;

        // Если звук зациклен, авто-возврат делать нельзя — этот источник должен контролироваться вручную
        if (source.loop) yield break;

        float duration = 1f; // Безопасное значение по умолчанию, если что-то пойдет не так

        if (source.clip != null)
        {
            // Берем длину фактически выбранного движком клипа
            duration = source.clip.length / Mathf.Abs(pitch);
        }

        // Ждем оставшееся время воспроизведения
        yield return new WaitForSeconds(duration);

        // Возвращаем в пул
        _pool.Release(source);
    }
}
