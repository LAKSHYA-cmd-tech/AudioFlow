using AudioFlow.Models;

namespace AudioFlow.Services;

/// <summary>
/// A simple, original low-frequency stereo crossfeed stage. It is not an HRTF,
/// surround decoder, or replacement for HeSuVi.
/// </summary>
public sealed class StereoCrossfeed
{
    private readonly double _mix;
    private readonly double _alpha;
    private double _leftLow;
    private double _rightLow;

    public StereoCrossfeed(SpatialSettings settings, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (sampleRate is < 8000 or > 384000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _mix = settings.CrossfeedEnabled ? Math.Clamp(settings.CrossfeedAmount, 0, .35) : 0;
        _alpha = 1 - Math.Exp(-2 * Math.PI * 700 / sampleRate);
    }

    public void Process(Span<float> interleavedStereo)
    {
        if (interleavedStereo.Length % 2 != 0)
            throw new ArgumentException("Crossfeed requires complete stereo frames.", nameof(interleavedStereo));
        if (_mix == 0) return;
        for (var i = 0; i < interleavedStereo.Length; i += 2)
        {
            var left = (double)interleavedStereo[i];
            var right = (double)interleavedStereo[i + 1];
            _leftLow += _alpha * (left - _leftLow);
            _rightLow += _alpha * (right - _rightLow);
            interleavedStereo[i] = (float)((1 - _mix) * left + _mix * _rightLow);
            interleavedStereo[i + 1] = (float)((1 - _mix) * right + _mix * _leftLow);
        }
    }

    public void Reset() => _leftLow = _rightLow = 0;
}
