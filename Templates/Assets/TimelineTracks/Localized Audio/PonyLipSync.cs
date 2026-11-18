using UnityEngine;

namespace PG.TimelineAudio
{
    // Рот пони под голос: слушает свой голосовой AudioSource (к нему привязан Voice Track катсцены) и по громкости открывает челюсть,
    // на «круглых» звуках (о, у) добавляет blendshape «Mouth O». Ключи не нужны - работает на любом языке и при любой нарезке.
    // Поверх анимации: в кадре без ключей челюсти поза не накапливается.
    public class PonyLipSync : MonoBehaviour
    {
        [SerializeField] private AudioSource _voice;
        [SerializeField] private Transform _jaw;
        [Tooltip("Ось открытия рта в локальных осях челюсти (у Лиэрис и Малышки - минус X, как в Talk Face)")]
        [SerializeField] private Vector3 _openAxis = Vector3.left;
        [Tooltip("Градусов при самом громком звуке (Talk Face: Лиэрис 24, Малышка 29)")]
        [SerializeField] private float _maxAngle = 24f;

        [Header("Mouth O (пусто - без него; ПКМ → «Найти меши с Mouth O»)")]
        [SerializeField] private SkinnedMeshRenderer[] _renderers;
        [SerializeField] private string _mouthO = "Mouth O";
        [SerializeField, Range(0f, 100f)] private float _maxMouthO = 80f;

        [Header("Чувствительность")]
        [Tooltip("Громкость (RMS), ниже которой рот закрыт - дыхание и шум")]
        [SerializeField] private float _gate = 0.01f;
        [Tooltip("Громкость (RMS), при которой рот открыт полностью")]
        [SerializeField] private float _full = 0.15f;
        [SerializeField] private float _openSpeed = 30f;
        [SerializeField] private float _closeSpeed = 14f;

        private const int Samples = 256;
        private readonly float[] _wave = new float[Samples];
        private readonly float[] _spectrum = new float[Samples];
        private int[] _mouthOIndex;
        private float[] _mouthOBase, _mouthOApplied;
        private Quaternion _jawBase, _jawApplied;
        private float _open, _round;

        public float open => _open;

        private void Awake()
        {
            if (_voice == null) Debug.LogWarning("PonyLipSync: не назначен Voice - рот не будет двигаться", this);
            if (_jaw == null) Debug.LogWarning("PonyLipSync: не назначена кость Jaw", this);
            _renderers ??= System.Array.Empty<SkinnedMeshRenderer>();
            _mouthOIndex = new int[_renderers.Length];
            _mouthOBase = new float[_renderers.Length];
            _mouthOApplied = new float[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _mouthOIndex[i] = _renderers[i] != null && _renderers[i].sharedMesh != null ? _renderers[i].sharedMesh.GetBlendShapeIndex(_mouthO) : -1;
            if (_jaw != null) _jawBase = _jawApplied = _jaw.localRotation;
        }

        private void LateUpdate()
        {
            Listen();
            ApplyJaw();
            ApplyMouthO();
        }

        // Громкость и «круглость» звука за кадр
        private void Listen()
        {
            float targetOpen = 0f, targetRound = 0f;
            if (_voice != null && _voice.isActiveAndEnabled)
            {
                _voice.GetOutputData(_wave, 0);
                float sum = 0f;
                for (int i = 0; i < Samples; i++) sum += _wave[i] * _wave[i];
                float rms = Mathf.Sqrt(sum / Samples);
                targetOpen = Mathf.Clamp01(Mathf.InverseLerp(_gate, _full, rms));

                if (targetOpen > 0f)
                {
                    // О/У: энергия в 300-1000 Гц заметно больше, чем в 1-3 кГц
                    _voice.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
                    float hz = AudioSettings.outputSampleRate * 0.5f / Samples;
                    float low = Band(300f, 1000f, hz), high = Band(1000f, 3000f, hz);
                    targetRound = Mathf.Clamp01(Mathf.InverseLerp(1.5f, 4f, low / Mathf.Max(high, 1e-6f)));
                }
            }
            float speed = targetOpen > _open ? _openSpeed : _closeSpeed;
            _open = Mathf.MoveTowards(_open, targetOpen, speed * Time.deltaTime);
            _round = Mathf.MoveTowards(_round, targetRound, _closeSpeed * Time.deltaTime);
        }

        private float Band(float from, float to, float hzPerBin)
        {
            float sum = 0f;
            // Полоса [from, to): соседние полосы не делят одну ячейку
            for (int i = Mathf.Max(1, (int)(from / hzPerBin)); i < Mathf.Min(Samples, (int)(to / hzPerBin)); i++) sum += _spectrum[i];
            return sum;
        }

        private void ApplyJaw()
        {
            if (_jaw == null) return;
            // Аниматор не трогал челюсть в этом кадре - берём прошлую основу, иначе поворот копится
            var current = _jaw.localRotation;
            if (current != _jawApplied) _jawBase = current;
            _jawApplied = _open > 0f ? _jawBase * Quaternion.AngleAxis(_open * _maxAngle, _openAxis) : _jawBase;
            _jaw.localRotation = _jawApplied;
        }

        private void ApplyMouthO()
        {
            float add = _open * _round * _maxMouthO;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_mouthOIndex[i] < 0) continue;
                float current = _renderers[i].GetBlendShapeWeight(_mouthOIndex[i]);
                if (!Mathf.Approximately(current, _mouthOApplied[i])) _mouthOBase[i] = current;
                _mouthOApplied[i] = Mathf.Min(100f, _mouthOBase[i] + add);
                if (!Mathf.Approximately(current, _mouthOApplied[i])) _renderers[i].SetBlendShapeWeight(_mouthOIndex[i], _mouthOApplied[i]);
            }
        }

#if UNITY_EDITOR
        // Все меши модели, у которых есть blendshape с именем из поля Mouth O (у Лиэрис это тело и одежда)
        [ContextMenu("Найти меши с Mouth O")]
        private void FindMouthORenderers()
        {
            var found = new System.Collections.Generic.List<SkinnedMeshRenderer>();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null && r.sharedMesh.GetBlendShapeIndex(_mouthO) >= 0) found.Add(r);
            UnityEditor.Undo.RecordObject(this, "Найти меши с Mouth O");
            _renderers = found.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"PonyLipSync: {_mouthO} найден на {found.Count} мешах", this);
        }
#endif
    }
}
