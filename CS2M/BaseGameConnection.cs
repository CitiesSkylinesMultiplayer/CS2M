using CS2M.API;
using CS2M.BaseGame;

namespace CS2M
{
    public class BaseGameConnection : ModConnection
    {
        public BaseGameConnection()
        {
            Name = "Cities: Skylines II";
            Enabled = true;
            ModClass = null;
            CommandAssemblies.Add(typeof(BaseGameMain).Assembly);
        }

        public override void RegisterHandlers()
        {
            
        }

        public override void UnregisterHandlers()
        {
            
        }
    }
}
