using System.Collections;
using MelonLoader;
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
    
    // this shit is hacky please only use when needed
    public static void AfterFrame(Action callback)
        => MelonCoroutines.Start(AndThen(callback));

    private static IEnumerator AndThen(Action callback)
    {
        yield return null;
        callback();
    }
}