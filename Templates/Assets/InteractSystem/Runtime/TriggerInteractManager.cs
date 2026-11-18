using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Scripting.LifecycleManagement;

namespace PG.InteractSystem
{
    public partial class TriggerInteractManager : MonoBehaviour, IInteractVisible
    {
        [SerializeField] private InputActionProperty _interactProperty;
        private float _currentTime;
        [SerializeField] private float _targetTimeFrame = 1f;
        [SerializeField] private float _radius = 3f;
        [SerializeField] private Transform _objectTransform;
        private Collider[] _colliders = new Collider[10];
        // Ближайшее первым; Interact срабатывает у выбранного
        private List<IInteractable> _interactables = new List<IInteractable>();
        // Добавленные триггерами (AddTrigger) - не теряются при обновлении сферы
        private readonly List<IInteractable> _triggered = new List<IInteractable>();
        private readonly List<IInteractable> _found = new List<IInteractable>();
        private IInteractable _selected;
        [AutoStaticsCleanup]
        public static TriggerInteractManager instance;

        public IReadOnlyList<IInteractable> interactables => _interactables;
        public IInteractable selected => _selected;
        public int selectedIndex => _selected != null ? _interactables.IndexOf(_selected) : -1;
        // Рядом больше одного - есть из чего выбирать
        public bool hasChoice => _interactables.Count > 1;
        // Список или выбор изменились
        public event System.Action changed;

        private void Awake()
        {
            instance = this;
            _currentTime = Time.time;
        }

        public bool IsInteractableExist(IInteractable interactable)
        {
            return _interactables.Contains(interactable);
        }

        void FixedUpdate()
        {
            if (Time.time > _currentTime + _targetTimeFrame)
            {
                _currentTime = Time.time;
                Refresh();
            }
        }

        private void Refresh()
        {
            _found.Clear();
            int count = Physics.OverlapSphereNonAlloc(Center(), _radius, _colliders);
            // Буфер забит целиком - часть коллайдеров не вошла (каждый раз разная): расширяем и спрашиваем снова
            while (count == _colliders.Length)
            {
                _colliders = new Collider[_colliders.Length * 2];
                count = Physics.OverlapSphereNonAlloc(Center(), _radius, _colliders);
            }
            // Заполнены только первые count ячеек: _colliders[count] пуст или остался с прошлого вызова.
            for (int i = 0; i < count; i++)
                if (_colliders[i].TryGetComponent(out IInteractable interactable) && !_found.Contains(interactable))
                    _found.Add(interactable);
            for (int i = _triggered.Count - 1; i >= 0; i--)
            {
                if (_triggered[i] is Object unityObject && unityObject == null) { _triggered.RemoveAt(i); continue; }
                if (!_found.Contains(_triggered[i])) _found.Add(_triggered[i]);
            }
            Apply();
        }

        // _found → _interactables: по расстоянию, выбор держится за объект, ушёл из радиуса - ближайший
        private void Apply()
        {
            Vector3 center = Center();
            _found.Sort((a, b) => Distance(a, center).CompareTo(Distance(b, center)));
            bool same = _found.Count == _interactables.Count;
            for (int i = 0; same && i < _found.Count; i++) same = _found[i] == _interactables[i];
            if (!same)
            {
                _interactables.Clear();
                _interactables.AddRange(_found);
            }
            var previous = _selected;
            if (_selected == null || !_interactables.Contains(_selected)) _selected = _interactables.Count > 0 ? _interactables[0] : null;
            IInteractVisible.visibleInteracted?.Invoke(_interactables.Count > 0);
            if (!same || previous != _selected) changed?.Invoke();
        }

        private Vector3 Center() => _objectTransform != null ? _objectTransform.transformHandle.position : transformHandle.position;

        private static float Distance(IInteractable interactable, Vector3 center) =>
            interactable is Component component && component != null ? (component.transform.position - center).sqrMagnitude : float.MaxValue;

        // Следующий / предыдущий по кругу
        public void SelectNext(int direction)
        {
            if (_interactables.Count < 2 || direction == 0) return;
            int index = Mathf.Max(0, selectedIndex);
            index = (index + (direction > 0 ? 1 : -1) + _interactables.Count) % _interactables.Count;
            _selected = _interactables[index];
            changed?.Invoke();
        }

        // Имя и иконка для списка: IInteractInfo на том же объекте; нет или пустое имя - имя объекта как есть (не настроен)
        public static string GetName(IInteractable interactable, out Sprite icon)
        {
            icon = null;
            if (interactable is not Component component || component == null) return "";
            if (component.TryGetComponent(out IInteractInfo info))
            {
                icon = info.interactIcon;
                if (!string.IsNullOrEmpty(info.interactName)) return info.interactName;
            }
            return component.gameObject.name;
        }

        private void OnEnable()
        {
            _interactProperty.action.performed += Interact;
        }
        private void OnDisable()
        {
            _interactProperty.action.performed -= Interact;
        }
        void Interact(InputAction.CallbackContext context)
        {
            if (_selected is Object unityObject && unityObject == null) _selected = null;
            if (_selected == null && _interactables.Count > 0) _selected = _interactables[0];
            _selected?.OnInteract();
        }
        public void AddTrigger(IInteractable interactable)
        {
            if (interactable == null || _triggered.Contains(interactable)) return;
            _triggered.Add(interactable);
            if (_interactables.Contains(interactable)) return;
            _found.Clear();
            _found.AddRange(_interactables);
            _found.Add(interactable);
            Apply();
        }
        public void RemoveTrigger(IInteractable interactable)
        {
            _triggered.Remove(interactable);
            if (!_interactables.Contains(interactable)) return;
            _found.Clear();
            _found.AddRange(_interactables);
            _found.Remove(interactable);
            Apply();
        }
    }
}
