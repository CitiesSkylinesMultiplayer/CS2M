// Copies of CS2M/Commands/ApiServer: the property names are what matters on the wire.
// TokenSecret is the optional host secret (see CS2M.Lobby/Protocol.cs).
namespace CS2M.Commands.ApiServer
{
    public abstract class ApiCommandBase
    {
    }

    public class PortCheckRequestCommand : ApiCommandBase
    {
        public int Port { get; set; }
    }

    public class PortCheckResultCommand : ApiCommandBase
    {
        public PortCheckResult State { get; set; }
        public string Message { get; set; }
    }

    public enum PortCheckResult
    {
        Reachable,
        Unreachable,
        Error
    }

    public class ServerRegistrationCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
        public string TokenSecret { get; set; }
    }

    public class SessionInfoCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
        public string CityName { get; set; }
        public string HostName { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool HasPassword { get; set; }
        public string GameVersion { get; set; }
        public string ModVersion { get; set; }
        public string TokenSecret { get; set; }
    }

    public class ServerUnregisterCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string TokenSecret { get; set; }
    }

    public class SessionDetailsCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public int Population { get; set; }
        public int Milestone { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string[] PlayerNames { get; set; }
        public string[] PlayerColors { get; set; }
        public string[] ModIds { get; set; }
        public string[] ModNames { get; set; }
        public string[] ModVersions { get; set; }
        public bool[] ModClientSide { get; set; }
        public int ModCount { get; set; }
        public int ModOffset { get; set; }
    }
}

// The commands as older mod versions have them (no TokenSecret), registered in the same order in a second
// model, so the smoke test also speaks the older wire format
namespace CS2M.Commands.ApiServer.Legacy
{
    public class ServerRegistrationCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
    }

    public class SessionInfoCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
        public string CityName { get; set; }
        public string HostName { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool HasPassword { get; set; }
        public string GameVersion { get; set; }
        public string ModVersion { get; set; }
    }

    public class ServerUnregisterCommand : ApiCommandBase
    {
        public string Token { get; set; }
    }
}
