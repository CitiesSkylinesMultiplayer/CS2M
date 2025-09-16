using System.Reflection;

namespace CS2M.Util
{
    public static class VersionUtil
    {
        public static System.Version GetModVersion()
        {
            return Assembly.GetAssembly(typeof(VersionUtil)).GetName().Version;
        }

        public static Colossal.Version GetGameVersion()
        {
            return Game.Version.current;
        }
    }
}