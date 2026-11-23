using System.Numerics;

namespace GatherBuddy.AutoGather.Movement;

internal sealed class WaypointProgress
{
    private long? _lastSample;
    private Vector3 _waypoint;
    private float _distanceAtLastProgress;
    private long _lastProgress;

    public void Reset() => _lastSample = null;

    // Reading the active waypoint list through IPC allocates a copy.
    public bool ShouldSample(long now) => !_lastSample.HasValue || now - _lastSample.Value >= 500;

    public bool IsStalled(Vector3 waypoint, float distance, long now, float timeoutSeconds)
    {
        if (!_lastSample.HasValue || now - _lastSample.Value > 1500 || waypoint != _waypoint
            || _distanceAtLastProgress - distance >= 1f)
        {
            _waypoint = waypoint;
            _distanceAtLastProgress = distance;
            _lastProgress = now;
        }

        _lastSample = now;
        return now - _lastProgress > timeoutSeconds * 1000;
    }
}
