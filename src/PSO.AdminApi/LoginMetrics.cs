using System.Text;
using System.Threading;

namespace PSO.AdminApi;

public sealed class LoginMetrics
{
    private long _successfulLogins;
    private long _failedLogins;

    public long SuccessfulLogins => Volatile.Read(ref _successfulLogins);
    public long FailedLogins => Volatile.Read(ref _failedLogins);
    public long TotalLogins => SuccessfulLogins + FailedLogins;

    public void IncrementSuccess() => Interlocked.Increment(ref _successfulLogins);
    public void IncrementFailure() => Interlocked.Increment(ref _failedLogins);

    public string ToPrometheusPayload(int worldCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HELP photoncore_logins_total Total number of login attempts.");
        builder.AppendLine("# TYPE photoncore_logins_total counter");
        builder.Append("photoncore_logins_total ").AppendLine(TotalLogins.ToString());
        builder.AppendLine("# HELP photoncore_failed_logins_total Total number of failed login attempts.");
        builder.AppendLine("# TYPE photoncore_failed_logins_total counter");
        builder.Append("photoncore_failed_logins_total ").AppendLine(FailedLogins.ToString());
        builder.AppendLine("# HELP photoncore_worlds_current Number of registered worlds.");
        builder.AppendLine("# TYPE photoncore_worlds_current gauge");
        builder.Append("photoncore_worlds_current ").AppendLine(worldCount.ToString());
        return builder.ToString();
    }
}
