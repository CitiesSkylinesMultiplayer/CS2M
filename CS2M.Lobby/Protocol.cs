using System.Buffers;
using MessagePack;

namespace CS2M.Lobby;

/// <summary>
///     The mod's API messages (CS2M/Commands/ApiServer). The mod serializes them with MessagePack.Attributeless,
///     which writes a bare sequence of MessagePack values, no surrounding array or map:
///     <code>
///     int typeKey          the subtype key, in the order ApiCommand.RefreshModel registers the commands
///     int propertyCount
///     (int propertyKey, value) * propertyCount
///                          property keys are the command's writable properties sorted by name, from 0
///                          (case-insensitive: Attributeless puts "aB" before "Ca")
///     </code>
///     The whole thing is the payload of a LiteNetLib unconnected message.
///     <para>
///         Compatibility rules: a new command goes at the end of ApiCommand.ProtocolOrder. An existing command may
///         only gain a property whose name sorts after all of its current ones, so no existing key moves (that is
///         how TokenSecret was added). Unknown keys are skipped here, so a newer mod still works with an older
///         lobby, and a missing key reads as its default, so an older mod still works with this one.
///     </para>
/// </summary>
public static class Protocol
{
    // ApiCommand.ProtocolOrder in the mod. New commands only ever go at the end.
    public const int PortCheckRequest = 0;
    public const int PortCheckResult = 1;
    public const int ServerRegistration = 2;
    public const int SessionInfo = 3;
    public const int ServerUnregister = 4;
    public const int SessionDetails = 5;

    // What SessionDetails may carry; the mod sends less (it must fit one packet), this only bounds bad input
    public const int MaxListedPlayers = 32;
    public const int MaxListedMods = 64;

    public enum PortCheckState
    {
        Reachable = 0,
        Unreachable = 1,
        Error = 2
    }

    public abstract record Message;

    /// <summary>Properties: Port = 0</summary>
    public sealed record PortCheckRequestMessage(int Port) : Message;

    /// <summary>
    ///     Properties: LocalIp = 0, LocalPort = 1, Token = 2, TokenSecret = 3.
    ///     TokenSecret (optional, newer mods): see <see cref="SessionRegistry.IsValidSecret" />.
    /// </summary>
    public sealed record ServerRegistrationMessage(string? Token, string? LocalIp, int LocalPort, string? Secret)
        : Message;

    /// <summary>
    ///     Properties: CityName = 0, GameVersion = 1, HasPassword = 2, HostName = 3, LocalIp = 4, LocalPort = 5,
    ///     MaxPlayers = 6, ModVersion = 7, PlayerCount = 8, Token = 9, TokenSecret = 10 (optional, newer mods)
    /// </summary>
    public sealed record SessionInfoMessage(
        string? Token, string? LocalIp, int LocalPort,
        string? CityName, string? HostName, int PlayerCount, int MaxPlayers, bool HasPassword,
        string? GameVersion, string? ModVersion, string? Secret = null) : Message;

    /// <summary>Properties: Token = 0, TokenSecret = 1 (optional, newer mods)</summary>
    public sealed record ServerUnregisterMessage(string? Token, string? Secret = null) : Message;

    public sealed record ListedMod(string Id, string? Name, string? Version, bool ClientSide);

    /// <summary>
    ///     Properties: Milestone = 0, ModClientSide = 1, ModCount = 2, ModIds = 3, ModNames = 4, ModOffset = 5,
    ///     ModVersions = 6, Month = 7, PlayerColors = 8, PlayerNames = 9, Population = 10, Token = 11, Year = 12.
    ///     The Player* and Mod* properties are arrays (nil when absent) of the same length.
    ///     A long mod list comes in several messages: ModOffset 0 has everything, the next ones continue the mods.
    /// </summary>
    public sealed record SessionDetailsMessage(
        string? Token, int Population, int Milestone, int Year, int Month,
        IReadOnlyList<(string Name, string? Color)> Players, IReadOnlyList<ListedMod> Mods, int ModCount,
        int ModOffset) : Message;

    /// <summary>
    ///     Decodes a message from the mod. Unknown properties are skipped, so newer mods can add some;
    ///     unknown types return null.
    /// </summary>
    public static Message? Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new MessagePackReader(data);
        if (reader.TryReadNil())
        {
            return null;
        }

        int type = reader.ReadInt32();
        if (type is < PortCheckRequest or > SessionDetails)
        {
            return null;
        }

        int count = reader.ReadInt32();
        if (count is < 0 or > 64)
        {
            throw new MessagePackSerializationException($"Bad property count {count}");
        }

        int port = 0, localPort = 0, playerCount = 0, maxPlayers = 0;
        bool hasPassword = false;
        string? token = null, secret = null, localIp = null, cityName = null, hostName = null, gameVersion = null, modVersion = null;
        int population = 0, milestone = 0, year = 0, month = 0, modCount = 0, modOffset = 0;
        List<string?>? playerNames = null, playerColors = null, modIds = null, modNames = null, modVersions = null;
        List<bool>? modClientSide = null;

        for (int i = 0; i < count; i++)
        {
            int key = reader.ReadInt32();
            switch (type, key)
            {
                case (PortCheckRequest, 0): port = reader.ReadInt32(); break;

                case (ServerRegistration, 0): localIp = ReadString(ref reader); break;
                case (ServerRegistration, 1): localPort = reader.ReadInt32(); break;
                case (ServerRegistration, 2): token = ReadString(ref reader); break;
                case (ServerRegistration, 3): secret = ReadString(ref reader); break;

                case (SessionInfo, 0): cityName = ReadString(ref reader); break;
                case (SessionInfo, 1): gameVersion = ReadString(ref reader); break;
                case (SessionInfo, 2): hasPassword = reader.ReadBoolean(); break;
                case (SessionInfo, 3): hostName = ReadString(ref reader); break;
                case (SessionInfo, 4): localIp = ReadString(ref reader); break;
                case (SessionInfo, 5): localPort = reader.ReadInt32(); break;
                case (SessionInfo, 6): maxPlayers = reader.ReadInt32(); break;
                case (SessionInfo, 7): modVersion = ReadString(ref reader); break;
                case (SessionInfo, 8): playerCount = reader.ReadInt32(); break;
                case (SessionInfo, 9): token = ReadString(ref reader); break;
                case (SessionInfo, 10): secret = ReadString(ref reader); break;

                case (ServerUnregister, 0): token = ReadString(ref reader); break;
                case (ServerUnregister, 1): secret = ReadString(ref reader); break;

                case (SessionDetails, 0): milestone = reader.ReadInt32(); break;
                case (SessionDetails, 1): modClientSide = ReadBoolArray(ref reader, MaxListedMods); break;
                case (SessionDetails, 2): modCount = reader.ReadInt32(); break;
                case (SessionDetails, 3): modIds = ReadStringArray(ref reader, MaxListedMods); break;
                case (SessionDetails, 4): modNames = ReadStringArray(ref reader, MaxListedMods); break;
                case (SessionDetails, 5): modOffset = reader.ReadInt32(); break;
                case (SessionDetails, 6): modVersions = ReadStringArray(ref reader, MaxListedMods); break;
                case (SessionDetails, 7): month = reader.ReadInt32(); break;
                case (SessionDetails, 8): playerColors = ReadStringArray(ref reader, MaxListedPlayers); break;
                case (SessionDetails, 9): playerNames = ReadStringArray(ref reader, MaxListedPlayers); break;
                case (SessionDetails, 10): population = reader.ReadInt32(); break;
                case (SessionDetails, 11): token = ReadString(ref reader); break;
                case (SessionDetails, 12): year = reader.ReadInt32(); break;

                default: reader.Skip(); break;
            }
        }

        return type switch
        {
            PortCheckRequest => new PortCheckRequestMessage(port),
            ServerRegistration => new ServerRegistrationMessage(token, localIp, localPort, secret),
            SessionInfo => new SessionInfoMessage(token, localIp, localPort, cityName, hostName, playerCount,
                maxPlayers, hasPassword, gameVersion, modVersion, secret),
            ServerUnregister => new ServerUnregisterMessage(token, secret),
            SessionDetails => new SessionDetailsMessage(token, population, milestone, year, month,
                ZipPlayers(playerNames, playerColors), ZipMods(modIds, modNames, modVersions, modClientSide), modCount, modOffset),
            _ => null // PortCheckResult only goes from us to the mod
        };
    }

    /// <summary>PortCheckResultCommand, properties: Message = 0, State = 1 (enum as its int value).</summary>
    public static byte[] EncodePortCheckResult(PortCheckState state, string? message)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        var writer = new MessagePackWriter(buffer);
        writer.Write(PortCheckResult);
        writer.Write(2);
        writer.Write(0);
        writer.Write(message);
        writer.Write(1);
        writer.Write((int)state);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string? ReadString(ref MessagePackReader reader)
    {
        return reader.TryReadNil() ? null : reader.ReadString();
    }

    /// <summary>An array of strings, or nil. Entries past <paramref name="max" /> are skipped.</summary>
    private static List<string?>? ReadStringArray(ref MessagePackReader reader, int max)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        int count = reader.ReadArrayHeader();
        var list = new List<string?>(Math.Min(count, max));
        for (int i = 0; i < count; i++)
        {
            if (i < max)
            {
                list.Add(ReadString(ref reader));
            }
            else
            {
                reader.Skip();
            }
        }

        return list;
    }

    private static List<bool>? ReadBoolArray(ref MessagePackReader reader, int max)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        int count = reader.ReadArrayHeader();
        var list = new List<bool>(Math.Min(count, max));
        for (int i = 0; i < count; i++)
        {
            if (i < max)
            {
                list.Add(reader.ReadBoolean());
            }
            else
            {
                reader.Skip();
            }
        }

        return list;
    }

    private static List<(string Name, string? Color)> ZipPlayers(List<string?>? names, List<string?>? colors)
    {
        var players = new List<(string, string?)>();
        for (int i = 0; names != null && i < names.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
            {
                players.Add((names[i]!, colors != null && i < colors.Count ? colors[i] : null));
            }
        }

        return players;
    }

    private static List<ListedMod> ZipMods(List<string?>? ids, List<string?>? names, List<string?>? versions,
        List<bool>? clientSide)
    {
        var mods = new List<ListedMod>();
        for (int i = 0; ids != null && i < ids.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(ids[i]))
            {
                mods.Add(new ListedMod(ids[i]!,
                    names != null && i < names.Count ? names[i] : null,
                    versions != null && i < versions.Count ? versions[i] : null,
                    clientSide != null && i < clientSide.Count && clientSide[i]));
            }
        }

        return mods;
    }
}
