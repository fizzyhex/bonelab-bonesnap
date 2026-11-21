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

    public static IEnumerator WaitForTaskAndThenRunCallback(Task task, Action<Task> callback)
    {
        while (!task.IsCompleted)
            yield return null;

        callback(task);
    }
}