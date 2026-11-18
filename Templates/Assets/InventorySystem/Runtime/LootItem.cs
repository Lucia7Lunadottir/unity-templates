using System;
using PG.InventoryManagement;
using PG.Tween;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

namespace PG.InteractSystem
{
    public class LootItem : MonoBehaviour, IInteractable, IInteractInfo
    {
        // Подпись в списке выбора - из предмета
        public string interactName => _item != null ? _item.localizedName : "";
        public Sprite interactIcon => _item != null ? _item.icon : null;

        [SerializeField] private Item _item;
        public Item item => _item;
        
        [Tooltip("������ ����� ���� Outline")]
        [SerializeField] private LayerMask _interactLayerMask;
        [SerializeField] private AudioResource _audioResource;

        public event System.Action interacted;
        private const string _PLAYER_TAG = "Player";

        // ������ �� �����, � ID ����� (����� 0-31)
        [SerializeField] private int _baseLayerIndex;
        [SerializeField] private int _outlineLayerIndex;

        [field: SerializeField] public UnityEvent interactEvent { get; set; }
        
        [ContextMenu("Get Cache")]
        private void GetCache()
        {
            _baseLayerIndex = gameObject.layer;
            _outlineLayerIndex = (int)Mathf.Log(_interactLayerMask.value, 2);
        }

        public void OnInteract()
        {
            if (Inventory.instance == null) return;

            if (Inventory.instance.AddItem(_item))
            {
                AudioPool.Instance?.PlaySound2D(_audioResource);
                interacted?.Invoke();
                interactEvent?.Invoke();
                enabled = false;
                
                TransformHandle mainTransformHandle = transformHandle;
                PGTween.OnValueTween(transformHandle.position, Inventory.instance.bagPoint.transformHandle.position, 0.1f, true,
                    v =>
                    {
                        mainTransformHandle.position = v;
                    }, null, () =>
                    {
                        Destroy(gameObject);
                    });
            }
        }

        private void Awake()
        {
            IInteractVisible.visibleInteracted += OnVisibleInteract;
        }

        private void OnVisibleInteract(bool value)
        {
            if (TriggerInteractManager.instance.IsInteractableExist(this))
            {
                SetSelectLayer();
            }
            else
            {
                SetBaseLayer();
            }
        }

        public void SetSelectLayer()
        {
            gameObject.layer = _outlineLayerIndex;
        }

        public void SetBaseLayer()
        {
            gameObject.layer = _baseLayerIndex;
        }

        private void OnDisable()
        {
            if (TriggerInteractManager.instance != null)
                TriggerInteractManager.instance.RemoveTrigger(this);
        }
        private void OnDestroy()
        {
            if (TriggerInteractManager.instance != null)
                TriggerInteractManager.instance.RemoveTrigger(this);
            IInteractVisible.visibleInteracted -= OnVisibleInteract;
        }
    }
}