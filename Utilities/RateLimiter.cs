using UnityEngine;

namespace BoneSnap.Utilities;

internal class RateLimiter(int maxEvents, float windowSeconds)
{
    private readonly Queue<float> _eventTimes = new();

    public bool TryAcquire()
    {
        var now = Time.realtimeSinceStartup;

        while (_eventTimes.Count > 0 && now - _eventTimes.Peek() >= windowSeconds)
            _eventTimes.Dequeue();

        if (_eventTimes.Count >= maxEvents)
            return false;

        _eventTimes.Enqueue(now);
        return true;
    }
}
