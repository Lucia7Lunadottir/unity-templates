using System;
using System.Collections.Generic;
using UnityEngine;

namespace PG.BuffSystem
{
    public class BuffManager : MonoBehaviour
    {
        [SerializeField] private List<Buff> _buffs = new List<Buff>();
        private Dictionary<Type, Buff> _buffDict = new Dictionary<Type, Buff>();
        public event Action<Buff, float> OnBuffAdded;
        public event Action<Buff> OnBuffRemoved;

        public List<Buff> GetActiveBuffs()
        {
            List<Buff> buffs = new List<Buff>();
            foreach (Buff buff in _buffs)
            {
                if (buff.isActiveAndEnabled)
                {
                    buffs.Add(buff);
                }
            }
            return buffs;
        }
    
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Awake()
        {
            foreach (var buff in _buffs)
            {
                _buffDict.Add(buff.GetType(), buff);
            }
        }


        public void AddBuff(Type buffType, float duration = 0f)
        {
            if (buffType != null && _buffDict.TryGetValue(buffType, out var buff))
            {
                buff.StartBuff(duration);
                OnBuffAdded?.Invoke(buff, duration);
            }
        
        }
        public void RemoveBuff(Type buffType)
        {
            if (buffType != null && _buffDict.TryGetValue(buffType, out var buff))
            {
                buff.StopBuff();
                OnBuffRemoved?.Invoke(buff);
            }
        
        }

        public void RemoveBuffInvoke(Buff buff)
        {
            OnBuffRemoved?.Invoke(buff);
        }
    }
}


