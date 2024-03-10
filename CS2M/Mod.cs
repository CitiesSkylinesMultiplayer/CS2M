using Colossal.Logging;
using Game;
using Game.Modding;

namespace CS2M
{
    public class Mod : IMod
    {
        public Mod()
        {
            Log.SetLoggingLevel(Level.Debug);
        }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
        }
    }
}
