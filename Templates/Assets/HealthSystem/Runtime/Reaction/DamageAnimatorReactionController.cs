using System.Collections;
using UnityEngine;

namespace PG.HealthSystem
{
    public class DamageAnimatorReactionController : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private string _damageTrigger = "Damage";
        [SerializeField] private GameObject _damagableObject;
        public IDamagable damagable;
        [SerializeField] private Renderer[] _renderers;

        [SerializeField, ColorUsage(false, true)]
        private Color _damageColor = Color.red;
        [SerializeField] private float _durationEffect = 0.1f;
        private Coroutine _coroutine;
        [SerializeField] private string _damageShaderColor = "_SubEmission";

        private void Awake()
        {
            if (_damagableObject != null)
                _damagableObject.TryGetComponent(out damagable);
        }

        private void OnEnable()
        {
            if (damagable != null)
                damagable.damaged += OnDamage;
        }

        private void OnDisable()
        {
            if (damagable != null)
                damagable.damaged -= OnDamage;
        }

        void OnDamage(float damage, GameObject damageObject)
        {
            if (_animator != null) _animator.SetTrigger(_damageTrigger);
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }

            _coroutine = StartCoroutine(OnDamageColor());
        }

        IEnumerator OnDamageColor()
        {
            // Create a property block for each renderer
            MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                blocks[i] = new MaterialPropertyBlock();
                _renderers[i].GetPropertyBlock(blocks[i]);
            }

            float halfDuration = _durationEffect / 2f;

            // Fade the effect in (from black/original to the damage color)
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float progress = t / halfDuration;
                Color currentColor = Color.Lerp(Color.black, _damageColor, progress);

                ApplyColorToAll(currentColor, blocks);
                yield return null;
            }

            // Fade the effect out (from the damage color back to black)
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float progress = t / halfDuration;
                Color currentColor = Color.Lerp(_damageColor, Color.black, progress);

                ApplyColorToAll(currentColor, blocks);
                yield return null;
            }

            // Reset the changes at the end
            ClearColorForAll();

            _coroutine = null;
        }

        private void ApplyColorToAll(Color color, MaterialPropertyBlock[] blocks)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;

                blocks[i].SetColor(_damageShaderColor, color);
                _renderers[i].SetPropertyBlock(blocks[i]);
            }
        }

        private void ClearColorForAll()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].SetPropertyBlock(null);
                }
            }
        }
    }
}