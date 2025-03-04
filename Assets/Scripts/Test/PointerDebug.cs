using UnityEngine;
using UnityEngine.InputSystem;

public class PointerDebug : MonoBehaviour
{
    private InputAction clickAction;
    private InputAction pointerPosAction;
    public Transform follow;

    void Start()
    {
        clickAction = InputSystem.actions.FindAction("Attack");
        pointerPosAction = InputSystem.actions.FindAction("PointerPos");
    }

    void Update()
    {
        if (!clickAction.WasPerformedThisFrame())
            return;

        var point = pointerPosAction.ReadValue<Vector2>();

        Plane plane = new(Camera.main.transform.forward, follow.position);

        var ray = Camera.main.ScreenPointToRay(point);

        plane.Raycast(ray, out float dist);

        var cursorPoint = ray.GetPoint(dist);

        transform.position = cursorPoint;
    }
}
