using UnityEngine;
using UnityEngine.InputSystem;

public class Cutter : MonoBehaviour
{
    public LineRenderer lineRenderer;

    Camera mainCam;
    InputAction clickAction;
    InputAction pointerPosAction;

    public bool debug = false;
    public VisualiseBoxCast visualiseBoxCast;

    private readonly RaycastHit[] raycastHits = new RaycastHit[20];

    private (Vector3 start, Vector3 current)? currentPointerWorldPos;

    private Vector3 GetCursorPoint()
    {
        var screenPoint = pointerPosAction.ReadValue<Vector2>();

        var ray = mainCam.ScreenPointToRay(screenPoint);
        // XY world plane
        var plane = new Plane(Vector3.forward, 0);
        plane.Raycast(ray, out var dist);
        return ray.GetPoint(dist);
    }

    void Start()
    {
        mainCam = Camera.main;
        clickAction = InputSystem.actions.FindAction("Attack");
        pointerPosAction = InputSystem.actions.FindAction("PointerPos");

        clickAction.started += _ =>
        {
            lineRenderer.enabled = true;
            var pointerStartPos = GetCursorPoint();
            currentPointerWorldPos = (pointerStartPos, pointerStartPos);
        };

        clickAction.canceled += _ =>
        {
            lineRenderer.enabled = false;

            if (!currentPointerWorldPos.HasValue)
            {
                Debug.LogError("empty pointer pos");
                return;
            }

            var pointerStartPos = currentPointerWorldPos.Value.start;

            // world position of where pointer was released
            Vector3 pointerEndPos = GetCursorPoint();
            var pointerDir = (pointerEndPos - pointerStartPos).normalized;

            var cameraPos = mainCam.transform.position;
            // calculate normal from camera -- this makes the plane rotated correctly so that it would be invisibly thin from the camera's viewpoint
            var cutNormal = Vector3
                .Cross(pointerStartPos - cameraPos, pointerEndPos - cameraPos)
                .normalized;

            // bias for the normal to face up relative to ground
            if (Vector3.Dot(cutNormal, Vector3.up) < 0)
                cutNormal = -cutNormal;

            var boxCastSize = new Vector3(5, .1f, .1f);
            var boxCastRotation = Quaternion.LookRotation(pointerDir, cutNormal);
            var resultCount = Physics.BoxCastNonAlloc(
                pointerStartPos,
                boxCastSize,
                pointerDir,
                raycastHits,
                boxCastRotation
            );

            if (debug && visualiseBoxCast)
            {
                visualiseBoxCast.transform.localScale = boxCastSize * 2;
                visualiseBoxCast.transform.SetPositionAndRotation(pointerStartPos, boxCastRotation);
            }

            ProcessCuts(pointerEndPos, cutNormal, resultCount);

            // reset state
            currentPointerWorldPos = null;
        };
    }

    private void ProcessCuts(Vector3 cutPoint, Vector3 cutNormal, int resultCount)
    {
        if (resultCount == 0)
            return;

        for (int i = 0; i < resultCount; i++)
        {
            var meshTr = raycastHits[i].transform;
            if (!meshTr.TryGetComponent<CuttableMesh>(out var cuttableMesh))
                continue;

            // get local position for mesh
            var localPoint = meshTr.InverseTransformPoint(cutPoint);
            var localCutNormal = meshTr.InverseTransformDirection(cutNormal);

            // the cutting plane for the mesh using cutNormal and a point on the plane relative to the mesh position
            Plane localCuttingPlane = new(localCutNormal, localPoint);

            // because the cut normal is always up, the positive side will be the part that "comes off" and the negative side is more the "base" because gravity
            // this is just to determine how to apply our force afterwards; not crucial to have this order
            var cutResult = MeshCut.CutMesh(cuttableMesh.meshFilter, localCuttingPlane);

            // some checks don't result in a cut as our boxCast has thickness but a plane does not
            if (cutResult == null)
                continue;

            var (posMesh, negMesh) = cutResult.Value;

            // original mesh
            cuttableMesh.meshFilter.mesh = negMesh;
            cuttableMesh.meshCollider.sharedMesh = negMesh;

            // new mesh
            GameObject newPart =
                new(
                    $"{meshTr.name}-new",
                    new[]
                    {
                        typeof(MeshFilter),
                        typeof(MeshRenderer),
                        typeof(MeshCollider),
                        typeof(Rigidbody),
                        typeof(CuttableMesh)
                    }
                );
            newPart.transform.SetPositionAndRotation(
                meshTr.transform.position,
                meshTr.transform.rotation
            );
            var newMeshFilter = newPart.GetComponent<MeshFilter>();
            var newMeshRenderer = newPart.GetComponent<MeshRenderer>();
            var newMeshCollider = newPart.GetComponent<MeshCollider>();
            var newRigidbody = newPart.GetComponent<Rigidbody>();

            newMeshFilter.mesh = posMesh;
            newMeshRenderer.materials = cuttableMesh.meshRenderer.materials;
            newMeshCollider.convex = true;
            newMeshCollider.sharedMesh = posMesh;
#if UNITY_EDITOR
            if (debug)
                Debug.Break();
#endif
            // apply a bit of force on the positive part
            newRigidbody.AddForce(cutNormal * 2.5f, ForceMode.Impulse);
        }
    }

    void Update()
    {
        if (!clickAction.IsInProgress() || !currentPointerWorldPos.HasValue)
            return;

        var workingPointerPos = currentPointerWorldPos.Value;
        workingPointerPos.current = GetCursorPoint();
        currentPointerWorldPos = workingPointerPos;
    }

    void LateUpdate()
    {
        if (!currentPointerWorldPos.HasValue)
            return;

        var (start, end) = currentPointerWorldPos.Value;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }
}
