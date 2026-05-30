using System;
using UnityEngine;

public static class HashUtility
{
    public static unsafe Hash128 Compute<T>(in ReadOnlySpan<T> data) where T : unmanaged
    {
        fixed (void* ptr = data)
        {
            Hash128 result;
            HashUnsafeUtilities.ComputeHash128(ptr, (ulong)(data.Length * sizeof(T)), &result);

            return result;
        }
    }
}
