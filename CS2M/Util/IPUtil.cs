using System.Net;
using CS2M.Networking;
using LiteNetLib;

namespace CS2M.Util
{
    public static class IPUtil
    {
        public static IPEndPoint CreateIPEndPoint(string hostAddress, int port) 
        {
            return new IPEndPoint(NetUtils.ResolveAddress(hostAddress), port);
        }
        
        public static IPEndPoint CreateIP4EndPoint(string hostAddress, int port)
        {
            return new IPEndPoint(IpAddress.GetIpv4(hostAddress), port);
        }
    }
}