using System.Collections.Generic;
using UnityEngine;

namespace PG.BuffManagement
{
    /// <summary>
    /// Gameplay HUD bar — spawns a BuffHUDSlotUI for every active buff.
    /// Place in the main UI Canvas (always visible during gameplay).
    /// </summary>
    public class BuffHUD : MonoBehaviour
    {
        [SerializeField] private Transform     _container;
        [SerializeField] private BuffHUDSlotUI _slotPrefab;

        private readonly Dictionary<ActiveBuff, BuffHUDSlotUI> _slots =
            new Dictionary<ActiveBuff, BuffHUDSlotUI>();

        private void OnEnable()
        {
            if (BuffSystem.Instance == null) return;

            BuffSystem.Instance.OnBuffApplied += HandleBuffApplied;
            BuffSystem.Instance.OnBuffRemoved += HandleBuffRemoved;

            // Populate with buffs that were already active when this opens
            foreach (var buff in BuffSystem.Instance.activeBuffs)
                SpawnSlot(buff);
        }

        private void OnDisable()
        {
            if (BuffSystem.Instance == null) return;

            BuffSystem.Instance.OnBuffApplied -= HandleBuffApplied;
            BuffSystem.Instance.OnBuffRemoved -= HandleBuffRemoved;
        }

        private void HandleBuffApplied(ActiveBuff buff)
        {
            if (!_slots.ContainsKey(buff))
                SpawnSlot(buff);
        }

        private void HandleBuffRemoved(ActiveBuff buff)
        {
            if (!_slots.TryGetValue(buff, out var slot)) return;
            Destroy(slot.gameObject);
            _slots.Remove(buff);
        }

        private void SpawnSlot(ActiveBuff buff)
        {
            var slot = Instantiate(_slotPrefab, _container);
            slot.SetData(buff);
            _slots[buff] = slot;
        }
    }
}
