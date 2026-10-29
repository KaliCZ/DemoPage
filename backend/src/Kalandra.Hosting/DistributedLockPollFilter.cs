using System.Diagnostics;
using OpenTelemetry;

namespace Kalandra.Hosting;

/// <summary>
/// Unrecords the spans of DistributedLock's leader-election wait loop (savepoint + pg_sleep +
/// rollback), which the HotCold standby daemon runs around the clock.
/// </summary>
public sealed class DistributedLockPollFilter : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.GetTagItem("db.query.text") is not string statement)
            return;

        if (statement.Contains("medallion_threading", StringComparison.Ordinal)
            || statement.Contains("pg_sleep", StringComparison.Ordinal))
        {
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
