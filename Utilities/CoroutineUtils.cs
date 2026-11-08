using System.Collections;
using UnityEngine;

namespace BoneSnap;

internal static class CoroutineUtils
{
    public static IEnumerator WaitAndThenRunCallback(float waitTime, Action callback)
    {
        yield return new WaitForSeconds(waitTime);
        callback();
    }
}