using System.Collections.Generic;
using UnityEngine;

namespace PG.VFXPool
{
    public class VFXPoolElementInvoker : MonoBehaviour
    {
        [SerializeField] private VFXElement[] _particles;

        public Dictionary<string, VFXPoolElement> particles { get; private set; } = new Dictionary<string, VFXPoolElement>();

        [System.Serializable]
        public class VFXElement
        {
            public VFXPoolElement vfx;
            public string name;
        }

        private void Awake()
        {
            InitializeDictionary();
        }

        private void InitializeDictionary()
        {
            if (particles.Count > 0 || _particles == null) return;

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].vfx != null && !string.IsNullOrEmpty(_particles[i].name))
                {
                    if (!particles.ContainsKey(_particles[i].name))
                    {
                        particles.Add(_particles[i].name, _particles[i].vfx);
                    }
                    else
                    {
                        Debug.LogWarning($"Duplicate VFX name '{_particles[i].name}' on GameObject '{gameObject.name}'!");
                    }
                }
            }
        }

        public void OnInvoke(int value)
        {
            var element = GetVFXElement(value);
            if (element != null)
            {
                element.Play();
            }
        }

        public VFXPoolElement GetVFXElement(int index)
        {
            if (_particles != null && index >= 0 && index < _particles.Length)
            {
                return _particles[index].vfx;
            }

            Debug.LogWarning($"VFX index {index} is out of bounds on GameObject '{gameObject.name}'!");
            return null;
        }

        public VFXPoolElement GetVFXElement(string name)
        {
            InitializeDictionary();

            if (particles.TryGetValue(name, out var vfxElement))
            {
                return vfxElement;
            }

            Debug.LogWarning($"VFX with name '{name}' not found on GameObject '{gameObject.name}'!");
            return null;
        }
    }
}