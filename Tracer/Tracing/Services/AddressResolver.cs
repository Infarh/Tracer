using System.Net;
using System.Net.Sockets;
using Tracer.Tracing.Models;

namespace Tracer.Tracing.Services;

/// <summary>Сервис разрешения сетевых адресов</summary>
internal static class AddressResolver
{
    /// <summary>Разрешает хост в IPv4-адрес</summary>
    /// <param name="Address">Хост или IP-адрес</param>
    /// <param name="CancellationToken">Токен отмены</param>
    /// <returns>Результат разрешения</returns>
    public static async Task<ResolveAddressResult> ResolveAddressAsync(string Address, CancellationToken CancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Address);

        CancellationToken.ThrowIfCancellationRequested();

        if (IPAddress.TryParse(Address, out var ip))
            return new(ip, null);

        try
        {
            var entry = await Dns.GetHostEntryAsync(Address).WaitAsync(CancellationToken).ConfigureAwait(false);

            if (entry.AddressList.Length == 0)
                return new(null, $"Unknown host ip \"{Address}\"");

            ip = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ip is null
                ? new(null, $"Host \"{Address}\" has no IPv4 address")
                : new(ip, null);
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return new(null, $"Unknown host ip \"{Address}\"");
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.TryAgain or SocketError.TimedOut)
        {
            return new(null, $"Temporary DNS failure for \"{Address}\", try again later");
        }
        catch (SocketException e)
        {
            return new(null, $"DNS resolve error for \"{Address}\": {e.SocketErrorCode}");
        }
    }

    /// <summary>Пытается получить reverse DNS-имя адреса</summary>
    /// <param name="Address">IP-адрес</param>
    /// <param name="CancellationToken">Токен отмены</param>
    /// <returns>Имя хоста или null</returns>
    public static async Task<string?> TryGetHostNameAsync(IPAddress Address, CancellationToken CancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(Address);

        try
        {
            CancellationToken.ThrowIfCancellationRequested();
            var ip_host_entry = await Dns.GetHostEntryAsync(Address).WaitAsync(CancellationToken).ConfigureAwait(false);
            return ip_host_entry.HostName;
        }
        catch (SocketException e) when (e is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
        {
            return null;
        }
    }
}
