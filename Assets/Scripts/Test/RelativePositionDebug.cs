using UnityEngine;
using UnityEngine.InputSystem;

public class RelativePositionDebug : MonoBehaviour
{
    private InputAction clickAction;
    private InputAction pointerPosAction;

    public Transform normalTr,
        normalTrIndicate;
    public Transform originTr,
        originTrIndicate;

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

        Plane plane = new(Camera.main.transform.forward, originTr.position);

        var ray = Camera.main.ScreenPointToRay(point);

        plane.Raycast(ray, out float dist);

        var cursorPoint = ray.GetPoint(dist);

        var dir = (normalTr.position - cursorPoint).normalized;

        // indicator for normal
        normalTrIndicate.position = cursorPoint;
        normalTrIndicate.rotation = Quaternion.LookRotation(dir);

        // get relative rotated point and direction
        var relPoint = normalTr.InverseTransformPoint(cursorPoint);
        var relDir = normalTr.InverseTransformDirection(dir);

        originTrIndicate.position = relPoint;
        originTrIndicate.rotation = Quaternion.LookRotation(relDir);
    }
}
