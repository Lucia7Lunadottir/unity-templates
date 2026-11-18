using UnityEngine;
using UnityEngine.EventSystems;

namespace PG.InventoryManagement
{
    public class InteractInventoryCell : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        private InventoryInteraction _interaction;
        private void Awake()
        {
            _interaction = FindAnyObjectByType<InventoryInteraction>(FindObjectsInactive.Include);
        }
        public void OnSelect(BaseEventData eventData)
        {
            _interaction.targetIndex = transform.GetSiblingIndex();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _interaction.RenderInfo(transform.GetSiblingIndex());
        }
    }
}