using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CS2M.Lobby;

/// <summary>Address classification and the hashed keys the limiters use.</summary>
public static class Net
{
    // Per process, so nobody can pick addresses or tokens that collide in the limiter tables on purpose
    private static readonly ulong Seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));

    /// <summary>The IPv4 address as a number, also for IPv4-mapped IPv6; null for real IPv6.</summary>
    public static uint? V4(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (!address.IsIPv4MappedToIPv6)
            {
                return null;
            }

            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    /// <summary>Limiter key for one address.</summary>
    public static ulong AddressKey(IPAddress address)
    {
        return V4(address) is uint v4 ? v4 : Hash(address.ToString()) | 1UL << 63;
    }

    /// <summary>Limiter key for the address's /24 (IPv6: its /48).</summary>
    public static ulong SubnetKey(IPAddress address)
    {
        if (V4(address) is uint v4)
        {
            return (v4 & 0xFFFFFF00u) | 1UL << 40;
        }

        byte[] bytes = address.GetAddressBytes();
        return Hash(Convert.ToHexString(bytes, 0, Math.Min(6, bytes.Length))) | 1UL << 63;
    }

    /// <summary>Seeded FNV-1a, for counting distinct strings without storing them.</summary>
    public static ulong Hash(string value)
    {
        ulong hash = 14695981039346656037UL ^ Seed;
        foreach (char c in value)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>An index into a table of <paramref name="mask" /> + 1 slots, different per <paramref name="row" />.</summary>
    public static int Slot(ulong key, int row, int mask)
    {
        ulong x = key ^ Seed ^ (0x9E3779B97F4A7C15UL * (ulong)(row + 1));
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (int)(x & (uint)mask);
    }

    /// <summary>
    ///     A LAN address a host or joiner may report for same-router joins: RFC 1918 or carrier-grade NAT space.
    ///     Never loopback, link-local, multicast, broadcast, unspecified or public.
    /// </summary>
    public static bool IsLan(IPAddress address)
    {
        if (V4(address) is not uint v)
        {
            return false;
        }

        return (v & 0xFF000000u) == 0x0A000000u || // 10.0.0.0/8
               (v & 0xFFF00000u) == 0xAC100000u || // 172.16.0.0/12
               (v & 0xFFFF0000u) == 0xC0A80000u || // 192.168.0.0/16
               (v & 0xFFC00000u) == 0x64400000u; // 100.64.0.0/10
    }

    /// <summary>A globally routable IPv4 address: what a real player's packets come from.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (V4(address) is not uint v)
        {
            return false;
        }

        return !(IsLan(address) ||
                 (v & 0xFF000000u) == 0x00000000u || // 0.0.0.0/8
                 (v & 0xFF000000u) == 0x7F000000u || // 127.0.0.0/8
                 (v & 0xFFFF0000u) == 0xA9FE0000u || // 169.254.0.0/16
                 (v & 0xFFFFFF00u) == 0xC0000000u || // 192.0.0.0/24
                 (v & 0xFFFFFF00u) == 0xC0000200u || // 192.0.2.0/24
                 (v & 0xFFFE0000u) == 0xC6120000u || // 198.18.0.0/15
                 (v & 0xFFFFFF00u) == 0xC6336400u || // 198.51.100.0/24
                 (v & 0xFFFFFF00u) == 0xCB007100u || // 203.0.113.0/24
                 (v & 0xF0000000u) == 0xE0000000u || // 224.0.0.0/4 multicast
                 (v & 0xF0000000u) == 0xF0000000u); // 240.0.0.0/4 and broadcast
    }
}
