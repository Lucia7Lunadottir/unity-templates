using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace PG.NotificationManagement
{
    public partial class NotificationSystem : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Transform _cellContainer;
        [SerializeField] private NotificationView _prefab;
        [SerializeField] private float _duration = 2f;
        [SerializeField] private int _initialPoolSize = 5; // ������������ ��������

        // ���� ��� �������� ���������� �������� (��� ���)
        private Stack<NotificationView> _pool = new Stack<NotificationView>();

        [AutoStaticsCleanup]
        public static NotificationSystem instance;

        bool InitializeInstance()
        {
            //if (instance != null && instance != this)
            //{
            //    Destroy(gameObject);
            //    return false;
            //}
            instance = this;
            //transformHandle.SetParent(default);
            //DontDestroyOnLoad(instance.gameObject);
            return true;
        }
        private void Awake()
        {
            if (!InitializeInstance()) return;

            // ��������������� �������� ��������, ����� �� ������ ��� ������ �����������
            for (int i = 0; i < _initialPoolSize; i++)
            {
                CreateNewItemAndAddToPool();
            }
        }

        public void Add(string title, string description, Sprite icon)
        {
            NotificationData data = new NotificationData(title, description, icon);
            ShowNotification(data);
        }

        private void ShowNotification(NotificationData data)
        {
            NotificationView view = null;

            // 1. ����� �� ���� ��� ������� �����
            // Skip views destroyed with their container
            while (view == null && _pool.Count > 0)
                view = _pool.Pop();
            if (view == null)
                view = InstantiateView();

            // 2. ���������� � �������� � ����� ������ (����� ������� � LayoutGroup ��� ������)
            view.transform.SetAsLastSibling();
            view.gameObject.SetActive(true);

            // 3. ����������� � �������� ����� �������� � ���
            view.Setup(data, _duration, ReturnToPool);
        }

        private void ReturnToPool(NotificationView view)
        {
            view.gameObject.SetActive(false); // ��������� ������
            _pool.Push(view); // ���������� � ����
        }

        private NotificationView InstantiateView()
        {
            NotificationView newView = Instantiate(_prefab, _cellContainer);
            newView.gameObject.SetActive(false);
            return newView;
        }

        private void CreateNewItemAndAddToPool()
        {
            var view = InstantiateView();
            _pool.Push(view);
        }
    }
}