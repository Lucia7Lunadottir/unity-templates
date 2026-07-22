using System;
using UnityEngine;

namespace PG.MenuManagement
{
    public static class UIManager
    {
        private static GameObject _currentActivePanel;
        public static GameObject currentActivePanel;
        
        // �������, ������� �������� true, ���� ������� ����� ������, � false, ���� �� �������
        public static event Action<bool> OnStateChanged;

        public static bool IsAnyPanelOpen => _currentActivePanel != null;

        public static bool RequestOpen(GameObject panel)
        {
            if (IsAnyPanelOpen && _currentActivePanel != panel)
                return false;

            _currentActivePanel = panel;
            OnStateChanged?.Invoke(true); // ���������, ��� ������ �������
            return true;
        }

        public static bool RegisterClose(GameObject panel)
        {
            if (_currentActivePanel == panel)
            {
                _currentActivePanel = null;
                OnStateChanged?.Invoke(false); // ���������, ��� �� �������
                return true;
            }
            return false;
        }
    }
}