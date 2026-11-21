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

    /// <summary>
    /// Waits for <paramref name="task"/> to finish, then runs <paramref name="callback"/> on the main thread.
    /// </summary>
    public static IEnumerator WaitForTaskAndThenRunCallback(Task task, Action<Task> callback)
    {
        while (!task.IsCompleted)
            yield return null;

        callback(task);
    }
}