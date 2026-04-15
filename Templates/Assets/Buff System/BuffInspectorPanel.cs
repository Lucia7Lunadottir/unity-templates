using System.Collections.Generic;
using PG.Localization;
using TMPro;
using UnityEngine;

namespace PG.BuffManagement
{
    /// <summary>
    /// Full-screen / pause-menu panel that lists every active buff with details.
    ///
    /// Open by calling gameObject.SetActive(true).
    /// Rebuilds the list on every buff change and refreshes timers each frame.
    /// </summary>
    public class BuffInspectorPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform           _container;
        [SerializeField] private BuffInspectorSlotUI _slotPrefab;
        [SerializeField] private TMP_Text            _titleText;
        [SerializeField] private GameObject          _emptyLabel; // "No active effects"

        [Header("Localization Keys")]
        [SerializeField] private string _keyTitle = "buff_inspector_title";
        [SerializeField] private string _keyEmpty = "buff_inspector_empty";

        private readonly List<BuffInspectorSlotUI> _slots = new List<BuffInspectorSlotUI>();

        // ──────────────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            if (LocalizationSystem.instance != null)
                LocalizationSystem.instance.localizationChanged += OnLocalize;

            if (BuffSystem.Instance != null)
            {
                BuffSystem.Instance.OnBuffApplied += OnBuffListChanged;
                BuffSystem.Instance.OnBuffRemoved += OnBuffListChanged;
            }

            Rebuild();
            OnLocalize();
        }

        private void OnDisable()
        {
            if (LocalizationSystem.instance != null)
                LocalizationSystem.instance.localizationChanged -= OnLocalize;

            if (BuffSystem.Instance != null)
            {
                BuffSystem.Instance.OnBuffApplied -= OnBuffListChanged;
                BuffSystem.Instance.OnBuffRemoved -= OnBuffListChanged;
            }
        }

        // Refresh countdown timers every frame (works with unscaled time on pause too,
        // because BuffSystem.Update is also paused when timeScale = 0, so values freeze).
        private void Update()
        {
            foreach (var slot in _slots)
                slot.RefreshTimer();
        }

        // ── List management ────────────────────────────────────────────────────

        private void OnBuffListChanged(ActiveBuff _) => Rebuild();

        private void Rebuild()
        {
            foreach (Transform child in _container) Destroy(child.gameObject);
            _slots.Clear();

            bool hasBuffs = BuffSystem.Instance != null
                            && BuffSystem.Instance.activeBuffs.Count > 0;

            _emptyLabel.SetActive(!hasBuffs);
            if (!hasBuffs) return;

            foreach (var buff in BuffSystem.Instance.activeBuffs)
            {
                var slot = Instantiate(_slotPrefab, _container);
                slot.SetData(buff);
                _slots.Add(slot);
            }
        }

        // ── Localization ───────────────────────────────────────────────────────

        private void OnLocalize(string _ = "")
        {
            var loc = LocalizationSystem.instance;
            if (loc == null) return;

            if (_titleText != null)
                _titleText.text = loc.GetLocalizedValue(_keyTitle, "Active Effects");

            if (_emptyLabel != null && _emptyLabel.TryGetComponent<TMP_Text>(out var emptyTmp))
                emptyTmp.text = loc.GetLocalizedValue(_keyEmpty, "No active effects");

            foreach (var slot in _slots)
                slot.Localize();
        }
    }
}
