namespace CS2M.Networking
{
    public class ConnectionConfig
    {
        public readonly string HostAddress;

        public readonly int Port;

        public readonly string Password;

        public readonly string Token;

        public ConnectionConfig(string hostAddress, int port, string password)
        {
            HostAddress = hostAddress;
            Port = port;
            Password = password;
        }
        
        public ConnectionConfig(string token)
        {
            Token = token;
        }
        
        public ConnectionConfig(string token, string password)
        {
            Token = token;
            Password = password;
        }

        public ConnectionConfig(int port, string password)
        {
            Port = port;
            Password = password;
        }

        public ConnectionConfig(int port)
        {
            Port = port;
        }

        public ConnectionConfig()
        {
            Port = 4230;
        }

        public bool IsTokenBased()
        {
            return !string.IsNullOrEmpty(Token);
        }
    }
}