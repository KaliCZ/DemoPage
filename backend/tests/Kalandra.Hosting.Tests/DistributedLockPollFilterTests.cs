using System.Diagnostics;

namespace Kalandra.Hosting.Tests;

public class DistributedLockPollFilterTests
{
    private static Activity NewRecordedActivity(string? statement)
    {
        var activity = new Activity("postgresql") { ActivityTraceFlags = ActivityTraceFlags.Recorded };

        if (statement is not null)
            activity.SetTag("db.query.text", statement);

        return activity;
    }

    [Theory]
    [InlineData("SAVEPOINT medallion_threading_postgres_database_connection_sleep")]
    [InlineData("ROLLBACK TO SAVEPOINT medallion_threading_postgres_database_connection_sleep")]
    [InlineData("SELECT pg_catalog.pg_sleep(@sleepTimeSeconds)")]
    public void OnEnd_LockWaitLoopStatement_Unrecords(string statement)
    {
        var activity = NewRecordedActivity(statement);

        new DistributedLockPollFilter().OnEnd(activity);

        Assert.False(activity.Recorded);
    }

    [Theory]
    [InlineData("select d.data from public.mt_doc_blogreaction as d where d.slug = $1;")]
    [InlineData("SELECT 1;")]
    public void OnEnd_RegularStatement_StaysRecorded(string statement)
    {
        var activity = NewRecordedActivity(statement);

        new DistributedLockPollFilter().OnEnd(activity);

        Assert.True(activity.Recorded);
    }

    [Fact]
    public void OnEnd_NoStatementTag_StaysRecorded()
    {
        var activity = NewRecordedActivity(statement: null);

        new DistributedLockPollFilter().OnEnd(activity);

        Assert.True(activity.Recorded);
    }
}
