using OpenTelemetry.Trace;

namespace Kalandra.Hosting;

/// <summary>
/// Root sampler (wrap in <see cref="ParentBasedSampler"/>) that drops the Marten async daemon's
/// high-water-mark poll — one trace every ~2s around the clock, ~85% of the spans we exported.
/// </summary>
public sealed class MartenDaemonPollSampler : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => samplingParameters.Name.EndsWith(".daemon.highwatermark", StringComparison.Ordinal)
            ? new SamplingResult(SamplingDecision.Drop)
            : new SamplingResult(SamplingDecision.RecordAndSample);
}
