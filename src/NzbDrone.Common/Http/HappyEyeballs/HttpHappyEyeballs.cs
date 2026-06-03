using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Http.HappyEyeballs;

public class HttpHappyEyeballs
{
    private const int ConnectionAttemptDelay = 250;

    private readonly Logger _logger;

    public HttpHappyEyeballs(Logger logger)
    {
        _logger = logger;
    }

    public async ValueTask<Stream> OnConnect(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var resolvedAddresses = await ResolveAddresses(endPoint, cancellationToken).ConfigureAwait(false);

        if (resolvedAddresses.Length == 0)
        {
            throw new WebException(
                $"The remote name {endPoint.Host} could not be resolved",
                WebExceptionStatus.NameResolutionFailure);
        }

        var happyEyeballs = CreateHappyEyeballs(endPoint);
        var socket = await happyEyeballs.Connect(resolvedAddresses, cancellationToken).ConfigureAwait(false);
        _logger.Trace("Successfully connected to {0} for host {1}", socket.RemoteEndPoint, endPoint.HostPort);

        return new NetworkStream(socket, ownsSocket: true);
    }

    // Resolve the connect target's IP address list. A literal IP host
    // (e.g. http://192.168.0.246:9191 — common for self-hosted gateway / NAS /
    // Docker-host addresses) MUST NOT be sent through Dns.GetHostEntryAsync: for
    // an IP literal that performs a REVERSE (PTR) lookup, which throws
    // "No such host is known" on any address that lacks a PTR record and breaks
    // every HTTP provider configured by raw IP. Use the literal directly.
    public static async Task<IPAddress[]> ResolveAddresses(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(endPoint.Host, out var literalIp))
        {
            return new[] { literalIp };
        }

        var ipHostEntry = await Dns.GetHostEntryAsync(endPoint.Host, endPoint.AddressFamily, cancellationToken).ConfigureAwait(false);
        return ipHostEntry.AddressList;
    }

    private HappyEyeballs<Socket> CreateHappyEyeballs(DnsEndPoint endPoint)
    {
        return new HappyEyeballs<Socket>(
            (ipAddress, cancel) => ConnectSocket(ipAddress, endPoint, cancel),
            TaskDelay);
    }

    private async Task TaskDelay(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeSpan = TimeSpan.FromMilliseconds(ConnectionAttemptDelay);

        await Task.Delay(timeSpan, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Socket> ConnectSocket(
        IPAddress ipAddress,
        DnsEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        var remoteEP = new IPEndPoint(ipAddress, endPoint.Port);

        // The following socket constructor will create a dual-mode socket on
        // systems where IPv6 is available.
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            // Turn off Nagle's algorithm since it degrades performance in most
            // HttpClient scenarios.
            NoDelay = true
        };

        _logger.Trace("Trying Happy Eyeballs connection to {0} for host {1}", ipAddress, endPoint.HostPort);

        try
        {
            await socket.ConnectAsync(remoteEP, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            socket.Dispose();
            _logger.Trace("Failed Happy Eyeballs connection to {0} for host {1} ({2})", ipAddress, endPoint.HostPort, e.Message);
            throw;
        }

        return socket;
    }
}
