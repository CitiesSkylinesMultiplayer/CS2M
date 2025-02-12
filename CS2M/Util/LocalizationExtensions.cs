using Colossal.Localization;
using Game.SceneFlow;

namespace CS2M.Util
{
    internal static class LocalizationExtensions
    {
        internal static LocalizationDictionary LocalizationDictionary => GameManager.instance.localizationManager.activeDictionary;

        /// <summary>
        /// One-off translation from a key.
        /// If the key is not found in the dictionary, it is returned as is.
        /// </summary>
        internal static string Translate(this string key)
        {
            return LocalizationDictionary.TryGetValue(key, out var value) ? value : key;
        }

        /// <summary>
        /// Same as <see cref="Translate(string)"/> but with variables interpolation.
        /// </summary>
        /// <param name="key">
        /// A key that points to a string that <see cref="string.Format(string,object[])"/> can format.
        /// </param>
        /// <param name="args">Values to interpolate</param>
        internal static string Translate(this string key, params object[] args)
        {
            return string.Format(key.Translate(), args);
        }
    }
}
