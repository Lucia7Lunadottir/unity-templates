using System.Collections;
using UnityEngine;

namespace PG.BuffSystem
{
    public abstract class Buff : MonoBehaviour
    {
        
        [SerializeField] private BuffManager _buffManager;
        public float timer;
        public float duration = 10f;
        private float _baseDuration;
        public float delay = 1f;
        private Coroutine _buffActiveCoroutine;
        [field:SerializeField] public BuffData buffData { get; private set; }
        [SerializeField] private Buff[] _removeBuffs;
        
        protected virtual void Awake()
        {
            _baseDuration = duration;
        }

        IEnumerator BuffActive()
        {
            timer = duration;
            float baseTime = Time.time;
            while (timer > 0)
            {
                timer -= Time.deltaTime;
                if (Time.time > baseTime + delay)
                {
                    ApplyBuff();
                    baseTime = Time.time;
                }
                yield return null;
            }
            StopBuff();
            _buffActiveCoroutine = null;
        }

        protected virtual void OnEnable()
        {
            _buffActiveCoroutine = StartCoroutine(BuffActive());
        }

        protected virtual void OnDisable()
        {
            if (_buffActiveCoroutine != null)
            {
                StopCoroutine(_buffActiveCoroutine);
                _buffActiveCoroutine = null;
            }
        }
        abstract protected void ApplyBuff();

        public void StartBuff(float newDuration = 0f)
        {
            if (_removeBuffs.Length > 0)
            {
                foreach (Buff buff in _removeBuffs)
                {
                    if (buff ==null)
                    {
                        continue;    
                    }
                    _buffManager.RemoveBuff(buff.GetType());
                }
            }
            if (newDuration > 0f)
            {
                duration = newDuration;
            }
            else
            {
                duration = _baseDuration;
            }
            timer = 0;
            enabled = true;
        }

        public void StopBuff()
        {
            enabled = false;
            timer = 0;
            _buffManager.RemoveBuffInvoke(this);
        }
    }
}