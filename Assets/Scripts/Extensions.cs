using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public static class Extensions
{
    /// <summary>
    /// Groups the enumerable by 3s. Will error if there are any remainder items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static IEnumerable<(T a, T b, T c)> GroupByTripletsStrict<T>(this IEnumerable<T> source)
        where T : struct
    {
        using var iterator = source.GetEnumerator();
        while (iterator.MoveNext())
        {
            T first = iterator.Current;

            if (!iterator.MoveNext())
            {
                throw new ArgumentException("enumerable is not in 3s");
            }

            T second = iterator.Current;

            if (!iterator.MoveNext())
            {
                throw new ArgumentException("enumerable is not in 3s");
            }

            T third = iterator.Current;

            yield return (first, second, third);
        }
    }

    public static IEnumerable<(T a, T b)> GroupByPairsStrict<T>(this IEnumerable<T> source)
        where T : struct
    {
        using var iterator = source.GetEnumerator();
        while (iterator.MoveNext())
        {
            T first = iterator.Current;

            if (!iterator.MoveNext())
            {
                throw new ArgumentException("enumerable is not in 2s");
            }

            T second = iterator.Current;

            yield return (first, second);
        }
    }

    public static List<T> Flatten<T>(this IEnumerable<(T, T, T)> source)
    {
        List<T> result = new();

        foreach (var (a, b, c) in source)
        {
            result.Add(a);
            result.Add(b);
            result.Add(c);
        }

        return result;
    }

    public static bool IntersectsSegment(
        this Plane plane,
        Vector3 start,
        Vector3 end,
        out Vector3 intersectPoint
    )
    {
        intersectPoint = Vector3.zero;

        var planeNormal = plane.normal;
        var planePoint = plane.ClosestPointOnPlane(Vector3.zero);
        Vector3 segDisplacement = end - start;

        float denominator = Vector3.Dot(planeNormal, segDisplacement);
        if (denominator == 0)
            return false;

        var normalisedDistRatio = Vector3.Dot(planeNormal, planePoint - start) / denominator;

        if (normalisedDistRatio < 0 || normalisedDistRatio > 1)
            return false;

        intersectPoint = start + normalisedDistRatio * segDisplacement;
        return true;
    }

    public static T FindDuplicate<T>(this IEnumerable<T> values)
    {
        using var _hs = HashSetPool<T>.Get(out var hashSet);
        foreach (var val in values)
        {
            if (!hashSet.Add(val))
                return val;
        }

        Debug.LogError($"found no duplicates in {values}");
        return default;
    }

    public readonly struct PooledArray<T> : IDisposable
    {
        private readonly T[] array;
        private readonly ArrayPool<T> pool;

        public PooledArray(T[] array, ArrayPool<T> pool)
        {
            this.array = array;
            this.pool = pool;
        }

        public readonly void Dispose()
        {
            pool.Return(array);
        }
    }

    public static PooledArray<T> GetPooledSegment<T>(
        this ArrayPool<T> arrayPool,
        int length,
        out ArraySegment<T> segment
    )
    {
        var rentedArray = arrayPool.Rent(length);
        segment = new(rentedArray, 0, length);

        return new(rentedArray, arrayPool);
    }

    public static bool Approximately(this Vector3 v1, Vector3 v2)
    {
        return (v2 - v1).sqrMagnitude
            <= Constants.FloatingPointTolerance * Constants.FloatingPointTolerance;
    }

    public static bool Approximately(this Vector2 v1, Vector2 v2)
    {
        return (v2 - v1).sqrMagnitude
            <= Constants.FloatingPointTolerance * Constants.FloatingPointTolerance;
    }
}
