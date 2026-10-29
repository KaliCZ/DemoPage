using OpenTelemetry.Trace;

namespace Kalandra.Hosting;

/// <summary>
/// Drops the daemon's high-water-mark poll, a trace every ~2s that would dominate exported spans; wrap in <see cref="ParentBasedSampler"/>.
/// </summary>
public sealed class MartenDaemonPollSampler : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => samplingParameters.Name.EndsWith(".daemon.highwatermark", StringComparison.Ordinal)
            ? new SamplingResult(SamplingDecision.Drop)
            : new SamplingResult(SamplingDecision.RecordAndSample);
}
