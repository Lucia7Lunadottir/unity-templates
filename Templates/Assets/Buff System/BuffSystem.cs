using System;
using System.Collections.Generic;
using PG.HealthSystem;
using PG.TemperatureManagement;
using UnityEngine;

namespace PG.BuffManagement
{
    /// <summary>
    /// Central manager for all player buffs and debuffs.
    ///
    /// Usage examples:
    ///   • Food:      BuffSystem.Instance.ApplyBuff(myBuffData, BuffSource.Food);
    ///   • Equip:     BuffSystem.Instance.ApplyBuff(warmthBuffData, BuffSource.Equipment);
    ///   • Unequip:   BuffSystem.Instance.RemoveBuff(warmthBuffData);
    ///   • Stat query: BuffSystem.Instance.ApplyToStat(baseDamage, BuffStatType.Damage);
    /// </summary>
    public class BuffSystem : MonoBehaviour
    {
        public static BuffSystem Instance { get; private set; }

        [Header("References")]
        [SerializeField] private GameObject        _healthOwner;
        [SerializeField] private TemperatureSystem _temperatureSystem;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly List<ActiveBuff> _activeBuffs = new List<ActiveBuff>();
        public IReadOnlyList<ActiveBuff> activeBuffs => _activeBuffs;

        // ── Events (subscribe for UI updates) ─────────────────────────────────
        public event Action<ActiveBuff> OnBuffApplied;
        public event Action<ActiveBuff> OnBuffRemoved;
        public event Action             OnBuffsChanged;

        private Health _health;
        private float  _regenAccumulator;

        // ──────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (_healthOwner != null)
                _healthOwner.TryGetComponent(out _health);

            // Push to target systems whenever the buff list changes.
            OnBuffsChanged += PushToSystems;
        }

        private void Update()
        {
            TickBuffTimers();
            TickHealthRegen();
        }

        // ── Apply / Remove ─────────────────────────────────────────────────────

        public void ApplyBuff(BuffData data, BuffSource source = BuffSource.Ability)
        {
            var existing = _activeBuffs.Find(b => b.data == data);

            if (existing != null)
            {
                if (data.canStack && existing.stacks < data.maxStacks)
                {
                    existing.stacks++;
                    if (data.refreshOnReapply) existing.remainingTime = data.duration;
                    OnBuffApplied?.Invoke(existing);
                    OnBuffsChanged?.Invoke();
                }
                else if (data.refreshOnReapply)
                {
                    existing.remainingTime = data.duration;
                    OnBuffsChanged?.Invoke();
                }
                return;
            }

            var buff = new ActiveBuff(data, source);
            _activeBuffs.Add(buff);
            OnBuffApplied?.Invoke(buff);
            OnBuffsChanged?.Invoke();
        }

        /// <summary>Remove a buff by its ScriptableObject reference (e.g. on unequip).</summary>
        public bool RemoveBuff(BuffData data)
        {
            int idx = _activeBuffs.FindIndex(b => b.data == data);
            if (idx < 0) return false;

            var removed = _activeBuffs[idx];
            _activeBuffs.RemoveAt(idx);
            OnBuffRemoved?.Invoke(removed);
            OnBuffsChanged?.Invoke();
            return true;
        }

        /// <summary>Remove ALL buffs applied by a given source (e.g. RemoveAllFromSource(Equipment) when loading a save).</summary>
        public void RemoveAllFromSource(BuffSource source)
        {
            bool changed = false;
            for (int i = _activeBuffs.Count - 1; i >= 0; i--)
            {
                if (_activeBuffs[i].source != source) continue;
                OnBuffRemoved?.Invoke(_activeBuffs[i]);
                _activeBuffs.RemoveAt(i);
                changed = true;
            }
            if (changed) OnBuffsChanged?.Invoke();
        }

        public bool HasBuff(BuffData data) => _activeBuffs.Exists(b => b.data == data);

        // ── Stat Query API ─────────────────────────────────────────────────────

        public float GetFlatBonus(BuffStatType statType)
        {
            float total = 0f;
            foreach (var buff in _activeBuffs)
                total += buff.GetValue(statType, BuffModifierType.Flat);
            return total;
        }

        public float GetPercentBonus(BuffStatType statType)
        {
            float total = 0f;
            foreach (var buff in _activeBuffs)
                total += buff.GetValue(statType, BuffModifierType.Percent);
            return total;
        }

        /// <summary>
        /// Returns baseValue with all active buffs applied:
        ///   (baseValue + flat) * (1 + percent)
        /// </summary>
        public float ApplyToStat(float baseValue, BuffStatType statType)
        {
            float flat    = GetFlatBonus(statType);
            float percent = GetPercentBonus(statType);
            return (baseValue + flat) * (1f + percent);
        }

        // ── Push bonuses to target systems ────────────────────────────────────

        /// <summary>
        /// Called every time the buff list changes. Recalculates all bonuses and
        /// writes them into Statistics and TemperatureSystem. Those systems stay
        /// completely unaware of BuffSystem.
        /// </summary>
        private void PushToSystems()
        {
            if (Statistics.Instance != null)
            {
                Statistics.Instance.extraDamageFlat          = (int)GetFlatBonus(BuffStatType.Damage);
                Statistics.Instance.extraDamagePercent        = GetPercentBonus(BuffStatType.Damage);
                Statistics.Instance.extraMagicDamageFlat      = (int)GetFlatBonus(BuffStatType.MagicDamage);
                Statistics.Instance.extraMagicDamagePercent   = GetPercentBonus(BuffStatType.MagicDamage);
                Statistics.Instance.extraMaxHealthFlat        = (int)GetFlatBonus(BuffStatType.MaxHealth);
                Statistics.Instance.extraMaxHealthPercent     = GetPercentBonus(BuffStatType.MaxHealth);
                Statistics.Instance.extraExpPercent           = GetPercentBonus(BuffStatType.ExpMultiplier);
            }

            if (_temperatureSystem != null)
            {
                _temperatureSystem.warmthBonus          = GetFlatBonus(BuffStatType.Warmth);
                _temperatureSystem.coldResistanceFactor = GetPercentBonus(BuffStatType.ColdResistance);
                _temperatureSystem.heatResistanceFactor = GetPercentBonus(BuffStatType.HeatResistance);
            }
        }

        // ── Private Ticks ──────────────────────────────────────────────────────

        private void TickBuffTimers()
        {
            bool changed = false;
            for (int i = _activeBuffs.Count - 1; i >= 0; i--)
            {
                var buff = _activeBuffs[i];
                if (buff.IsPermanent) continue;

                buff.remainingTime -= Time.deltaTime;
                if (!buff.IsExpired) continue;

                OnBuffRemoved?.Invoke(buff);
                _activeBuffs.RemoveAt(i);
                changed = true;
            }
            if (changed) OnBuffsChanged?.Invoke();
        }

        private void TickHealthRegen()
        {
            float regenPerSec = GetFlatBonus(BuffStatType.HealthRegen);
            if (regenPerSec <= 0f || _health == null) return;

            _regenAccumulator += Time.deltaTime;
            if (_regenAccumulator < 1f) return;

            _regenAccumulator -= 1f;
            _health.OnHeal(regenPerSec);
        }
    }
}
