using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.BuffSystem
{
    public class ActiveBuffCell : MonoBehaviour
    {
        [field:SerializeField] public Image icon { get; private set; }
        [field:SerializeField] public TMP_Text timeText { get; private set; }
        private Buff _buff;
        private BuffManager _buffManager;
        
        public void Initialize(Buff buff, BuffManager  buffManager)
        {
            _buff = buff;
            _buffManager = buffManager;
            icon.sprite = buff.buffData.icon;
            _buffManager.OnBuffRemoved += DestroyBuff;
        }

        void DestroyBuff(Buff buff)
        {
            _buffManager.OnBuffRemoved -= DestroyBuff;
            Destroy(gameObject);
        }
        private void Update()
        {
            if (_buff != null)
            {
                timeText.text = _buff.duration < 0f ? "∞" : $"{_buff.timer:F0}с";
                
            }
        }
    }
}