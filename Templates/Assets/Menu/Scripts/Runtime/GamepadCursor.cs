using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PG.MenuManagement
{
    public class GamepadCursor : MonoBehaviour
    {
        [Header("Cursor Settings")]
        [SerializeField] private RectTransform cursorImage;
        [SerializeField] private Canvas canvas;
        [SerializeField] private float cursorSpeed = 1000f;
        [SerializeField] private float deadzone = 0.15f;

        [Header("Auto Snap Settings")]
        [Tooltip("Seconds for the cursor to fly to a snapped element")]
        [SerializeField] private float snapDuration = 0.25f;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference clickAction;

        private RectTransform canvasRect;
        private Vector2 moveInput;
        private Camera uiCamera;

        private GameObject currentHovered;
        private PointerEventData pointerData;
        private readonly List<RaycastResult> raycastResults = new();

        private Coroutine snapCoroutine;

        public Vector2 cursorScreenPosition => cursorImage.position;

        private void Awake()
        {
            canvasRect = canvas.GetComponent<RectTransform>();
            uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            cursorImage.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            pointerData = new PointerEventData(EventSystem.current);
        }

        private void OnEnable()
        {
            if (moveAction?.action != null)
            {
                moveAction.action.performed += OnMovePerformed;
                moveAction.action.canceled += OnMoveCanceled;
                moveAction.action.Enable();
            }

            if (clickAction?.action != null)
            {
                clickAction.action.performed += OnClickPerformed;
                clickAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (currentHovered != null)
            {
                ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerExitHandler);
                currentHovered = null;
            }

            if (moveAction?.action != null)
            {
                moveAction.action.performed -= OnMovePerformed;
                moveAction.action.canceled -= OnMoveCanceled;
            }

            if (clickAction?.action != null)
            {
                clickAction.action.performed -= OnClickPerformed;
            }
        }

        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            moveInput = ctx.ReadValue<Vector2>();
            if (moveInput.magnitude < deadzone)
            {
                moveInput = Vector2.zero;
            }
            else
            {
                StopSnapRoutine();
            }
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            moveInput = Vector2.zero;
        }

        private void OnClickPerformed(InputAction.CallbackContext ctx)
        {
            if (currentHovered == null) return;

            pointerData.position = cursorImage.position;

            ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerClickHandler);
        }

        private void Update()
        {
            UpdateCursorPosition();
            UpdateHover();
        }

        private void UpdateCursorPosition()
        {
            if (snapCoroutine != null || moveInput == Vector2.zero) return;

            Vector2 currentPos = cursorImage.position;

            currentPos += moveInput * cursorSpeed * Time.unscaledDeltaTime;
            currentPos.x = Mathf.Clamp(currentPos.x, 0f, Screen.width);
            currentPos.y = Mathf.Clamp(currentPos.y, 0f, Screen.height);

            cursorImage.position = currentPos;
        }

        private void UpdateHover()
        {
            pointerData.position = cursorImage.position;
            raycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, raycastResults);

            GameObject hitObject = raycastResults.Count > 0 ? raycastResults[0].gameObject : null;

            if (hitObject == currentHovered) return;

            if (currentHovered != null)
            {
                ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerExitHandler);
            }

            if (hitObject != null)
            {
                ExecuteEvents.Execute(hitObject, pointerData, ExecuteEvents.pointerEnterHandler);
            }

            currentHovered = hitObject;
        }

        public void SnapToUIElement(RectTransform targetElement)
        {
            if (targetElement == null) return;

            StopSnapRoutine();

            snapCoroutine = StartCoroutine(SnapToPositionRoutine(targetElement));
        }

        private IEnumerator SnapToPositionRoutine(RectTransform targetElement)
        {
            Vector2 startPosition = cursorImage.position;
            float elapsedTime = 0f;

            if (snapDuration <= 0f)
            {
                cursorImage.position = targetElement.position;
                snapCoroutine = null;
                yield break;
            }

            while (elapsedTime < snapDuration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                float t = elapsedTime / snapDuration;

                t = Mathf.SmoothStep(0f, 1f, t);

                Vector2 currentTargetPosition = targetElement.position;

                cursorImage.position = Vector2.Lerp(startPosition, currentTargetPosition, t);

                yield return null;
            }

            cursorImage.position = targetElement.position;
            snapCoroutine = null;
        }

        public void StopSnapRoutine()
        {
            if (snapCoroutine != null)
            {
                StopCoroutine(snapCoroutine);
                snapCoroutine = null;
            }
        }
    }
}
