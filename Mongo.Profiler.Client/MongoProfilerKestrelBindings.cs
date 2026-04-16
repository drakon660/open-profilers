using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Mongo.Profiler.Client;

internal static class MongoProfilerKestrelBindings
{
    public static void BindConfiguredUrl(
        KestrelServerOptions serverOptions,
        Uri url,
        HttpProtocols protocols)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentNullException.ThrowIfNull(url);

        if (string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            serverOptions.ListenLocalhost(url.Port, listenOptions => { listenOptions.Protocols = protocols; });
            return;
        }

        serverOptions.ListenAnyIP(url.Port, listenOptions => { listenOptions.Protocols = protocols; });
    }

    public static void BindProfilerPort(
        KestrelServerOptions serverOptions,
        int port,
        bool listenOnAnyIp,
        HttpProtocols protocols)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);

        if (listenOnAnyIp)
        {
            serverOptions.ListenAnyIP(port, listenOptions => { listenOptions.Protocols = protocols; });
            return;
        }

        serverOptions.ListenLocalhost(port, listenOptions => { listenOptions.Protocols = protocols; });
    }
}
