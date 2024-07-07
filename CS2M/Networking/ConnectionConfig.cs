namespace CS2M.Networking
{
    public class ConnectionConfig
    {
        public string HostAddress;

        public int Port;

        public string Password;

        public string Token;

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

        public ConnectionConfig()
        {
            Port = 4230;
        }

        public bool IsTokenBased()
        {
            return Token.Length != 0;
        }
    }
}