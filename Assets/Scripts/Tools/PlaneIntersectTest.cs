using UnityEngine;

public class PlaneIntersectTest : MonoBehaviour
{
    public Vector3 segmentStart;
    public Vector3 segmentEnd;

    bool intersects = false;
    Vector3 intersectPoint;

    void OnDrawGizmos()
    {
        Plane plane = new(-transform.up, transform.position);

        intersects = plane.IntersectsSegment(segmentStart, segmentEnd, out intersectPoint);

        Gizmos.color = intersects ? Color.red : Color.blue;

        Gizmos.DrawLine(segmentStart, segmentEnd);

        Gizmos.DrawLine(
            transform.position + (-transform.right + transform.forward) * 2,
            transform.position + (-transform.right + transform.forward) * -2
        );
        Gizmos.DrawLine(
            transform.position + (transform.right + transform.forward) * 2,
            transform.position + (transform.right + transform.forward) * -2
        );

        if (intersects)
        {
            Gizmos.DrawSphere(intersectPoint, .1f);
        }
    }
}
