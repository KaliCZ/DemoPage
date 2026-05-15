using System.Net;
using System.Net.Sockets;

namespace Kalandra.AppHost;

internal sealed record ReservedPort(int Port, TcpListener Listener);

// One port per AppHost-owned endpoint. OTLP HTTP is separate from gRPC because the browser exporter speaks HTTP only.
internal sealed record AppHostPorts(
    ReservedPort Dashboard,
    ReservedPort Otlp,
    ReservedPort OtlpHttp,
    ReservedPort Resource,
    string Source)
{
    // Drops the placeholder listeners so Aspire can bind; call under the reservation mutex.
    public void StopListeners()
    {
        Dashboard.Listener.Stop();
        Otlp.Listener.Stop();
        OtlpHttp.Listener.Stop();
        Resource.Listener.Stop();
    }
}

// Reserves AppHost ports by holding a TcpListener on each so parallel AppHosts step past in-use ports.
internal static class PortReservation
{
    private const int DashboardDefault = 15036;
    private const int OtlpDefault = 19200;
    private const int OtlpHttpDefault = 19400;
    private const int ResourceDefault = 20056;

    public static AppHostPorts Reserve()
    {
        var offsetEnv = Environment.GetEnvironmentVariable("KALANDRA_PORT_OFFSET");
        if (string.IsNullOrEmpty(offsetEnv))
        {
            return ReserveAuto();
        }

        if (!int.TryParse(offsetEnv, out var offset))
        {
            Console.Error.WriteLine($"KALANDRA_PORT_OFFSET must be an integer, got: {offsetEnv}");
            Environment.Exit(1);
        }
        return ReservePinned(offset);
    }

    // Fail early with a clear message if a pinned port is taken.
    private static AppHostPorts ReservePinned(int offset)
    {
        var pinned = new (int Port, string Label)[]
        {
            (DashboardDefault + offset, "dashboard"),
            (OtlpDefault + offset, "otlp"),
            (OtlpHttpDefault + offset, "otlp-http"),
            (ResourceDefault + offset, "resource"),
        };
        var listeners = new TcpListener[pinned.Length];
        for (var i = 0; i < pinned.Length; i++)
        {
            try
            {
                listeners[i] = new TcpListener(IPAddress.Loopback, pinned[i].Port);
                listeners[i].Start();
            }
            catch (SocketException)
            {
                for (var j = 0; j < i; j++) listeners[j].Stop();
                Console.Error.WriteLine($"KALANDRA_PORT_OFFSET={offset}: {pinned[i].Label} port {pinned[i].Port} is already in use");
                Environment.Exit(1);
            }
        }
        return new AppHostPorts(
            Dashboard: new ReservedPort(pinned[0].Port, listeners[0]),
            Otlp: new ReservedPort(pinned[1].Port, listeners[1]),
            OtlpHttp: new ReservedPort(pinned[2].Port, listeners[2]),
            Resource: new ReservedPort(pinned[3].Port, listeners[3]),
            Source: $"KALANDRA_PORT_OFFSET={offset}");
    }

    // No offset: walk up from each default until a free port is found.
    private static AppHostPorts ReserveAuto()
    {
        var dashboard = ReserveFreePortFrom(DashboardDefault, "dashboard");
        var otlp = ReserveFreePortFrom(OtlpDefault, "otlp");
        var otlpHttp = ReserveFreePortFrom(OtlpHttpDefault, "otlp-http");
        var resource = ReserveFreePortFrom(ResourceDefault, "resource");
        var allAtDefault = dashboard.Port == DashboardDefault
            && otlp.Port == OtlpDefault
            && otlpHttp.Port == OtlpHttpDefault
            && resource.Port == ResourceDefault;
        return new AppHostPorts(dashboard, otlp, otlpHttp, resource,
            Source: allAtDefault ? "default ports" : "default ports, stepped past in-use");
    }

    // Binds the first free port at or above `start`; caller keeps the listener until hand-off.
    private static ReservedPort ReserveFreePortFrom(int start, string label, int maxAttempts = 100)
    {
        for (var port = start; port < start + maxAttempts; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return new ReservedPort(port, listener);
            }
            catch (SocketException)
            {
            }
        }
        throw new InvalidOperationException($"No free {label} port found in range {start}..{start + maxAttempts - 1}");
    }
}
