using CS2M.Commands.ApiServer;
using CS2M.Util;
using LiteNetLib;
using System;

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
                _netManager.SendUnconnectedMessage(ApiCommand.Instance.Serialize(message),
                    IPUtil.CreateIP4EndPoint(Mod.Instance.Settings.ApiServer, Mod.Instance.Settings.GetApiServerPort()));
                Log.Debug(
                    $"Sending {message.GetType().Name} to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}");
            }
            catch (Exception e)
            {
                Log.Warn($"Could not send message to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}: {e}");
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