using System;
using UnityEngine;

public static class Helpers
{
    public static (int x, int y) Quantize(Vector2 v)
    {
        int x = Mathf.RoundToInt(v.x / Constants.FloatingPointTolerance);
        int y = Mathf.RoundToInt(v.y / Constants.FloatingPointTolerance);

        return (x, y);
    }

    public static (int x, int y, int z) Quantize(Vector3 v)
    {
        int x = Mathf.RoundToInt(v.x / Constants.FloatingPointTolerance);
        int y = Mathf.RoundToInt(v.y / Constants.FloatingPointTolerance);
        int z = Mathf.RoundToInt(v.z / Constants.FloatingPointTolerance);

        return (x, y, z);
    }
}
