using System;
using System.Net;
using CS2M.Commands.ApiServer;
using LiteNetLib;

namespace CS2M.Networking
{
    public class ApiServer
    {
        private readonly NetManager _netManager;
        private int _keepAlive = 1;

        public ApiServer(NetManager netManager)
        {
            _netManager = netManager;
        }

        /// <summary>
        ///     Send a message to the API server
        /// </summary>
        /// <param name="message"></param>
        public void SendCommand(ApiCommandBase message)
        {
            try
            {
                IPAddress apiServer = IpAddress.GetIpv4(Mod.ModSettings.ApiServer);
                _netManager.SendUnconnectedMessage(ApiCommand.Instance.Serialize(message),
                    new IPEndPoint(apiServer, Mod.ModSettings.GetApiServerPort()));
                Log.Debug(
                    $"Sending {message.GetType().Name} to API server at {apiServer}:{Mod.ModSettings.ApiServerPort}");
            }
            catch (Exception e)
            {
                Log.Warn($"Could not send message to API server at {Mod.ModSettings.ApiServer}:{Mod.ModSettings.ApiServerPort}: {e}");
            }
        }

        public void KeepAlive(ConnectionConfig connectionConfig)
        {
            if (_keepAlive % (60 * 5) == 0)
            {
                string localIp = NetUtils.GetLocalIp(LocalAddrType.IPv4);
                if (string.IsNullOrEmpty(localIp))
                {
                    localIp = NetUtils.GetLocalIp(LocalAddrType.IPv6);
                }

                SendCommand(new ServerRegistrationCommand
                {
                    LocalIp = localIp,
                    LocalPort = connectionConfig.Port,
                    Token = connectionConfig.Token
                });
            }
            _keepAlive += 1;
        }
    }
}