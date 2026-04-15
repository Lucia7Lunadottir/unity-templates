using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class GamepadCursor : MonoBehaviour
{
    [Header("Cursor Settings")]
    [SerializeField] private RectTransform cursorImage;
    [SerializeField] private Canvas canvas;
    [SerializeField] private float cursorSpeed = 1000f;
    [SerializeField] private float deadzone = 0.15f;

    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference clickAction;

    private RectTransform canvasRect;
    private Vector2 cursorPosition;
    private Vector2 moveInput;
    private Camera uiCamera;

    private GameObject currentHovered;
    private PointerEventData pointerData;
    private readonly List<RaycastResult> raycastResults = new();

    private void Awake()
    {
        canvasRect = canvas.GetComponent<RectTransform>();
        uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;

        cursorPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
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
        // Clean up hover state
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
            moveInput = Vector2.zero;
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (currentHovered == null) return;

        pointerData.position = cursorPosition;

        // Pointer down -> up -> click sequence
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
        if (moveInput == Vector2.zero) return;

        cursorPosition += moveInput * cursorSpeed * Time.unscaledDeltaTime;
        cursorPosition.x = Mathf.Clamp(cursorPosition.x, 0f, Screen.width);
        cursorPosition.y = Mathf.Clamp(cursorPosition.y, 0f, Screen.height);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, cursorPosition, uiCamera, out Vector2 localPoint);

        cursorImage.localPosition = localPoint;
    }

    private void UpdateHover()
    {
        pointerData.position = cursorPosition;
        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        GameObject hitObject = raycastResults.Count > 0 ? raycastResults[0].gameObject : null;

        // Same object — nothing to do
        if (hitObject == currentHovered) return;

        // Exit previous
        if (currentHovered != null)
        {
            ExecuteEvents.Execute(currentHovered, pointerData, ExecuteEvents.pointerExitHandler);
        }

        // Enter new
        if (hitObject != null)
        {
            ExecuteEvents.Execute(hitObject, pointerData, ExecuteEvents.pointerEnterHandler);
        }

        currentHovered = hitObject;
    }

    public Vector2 CursorScreenPosition => cursorPosition;
}