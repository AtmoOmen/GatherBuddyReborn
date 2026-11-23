using System.Numerics;

namespace GatherBuddy.AutoGather.Movement;

internal sealed class NavigationProgress
{
    // Measure displacement from an anchor so small steps count without accumulating position jitter.
    private const float MovementTolerance = 1f;
    private Vector3 _anchor;
    private long? _lastSample;
    private long _lastMovement;

    public void Reset()
        => _lastSample = null;

    public double Observe(Vector3 position, long now)
    {
        if (_lastSample == null || now - _lastSample.Value > 1000
         || Vector3.DistanceSquared(_anchor, position) >= MovementTolerance * MovementTolerance)
        {
            _anchor = position;
            _lastMovement = now;
        }

        _lastSample = now;
        return (now - _lastMovement) / 1000.0;
    }
}
