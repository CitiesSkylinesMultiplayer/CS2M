using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CS2M.Networking
{
    /// <summary>
    ///     Checks for text that comes over the network: player names and chat.
    /// </summary>
    public static class TextRules
    {
        public const int MaxUsernameLength = 32;
        public const int MaxChatLength = 300;

        /// <summary>
        ///     A trimmed player name, or null when it isn't acceptable: empty, longer than
        ///     <see cref="MaxUsernameLength" />, with control or invisible formatting characters, or the mod's own
        ///     name (which system messages use).
        /// </summary>
        public static string ValidUsername(string name)
        {
            if (name == null)
            {
                return null;
            }

            name = name.Trim();
            if (name.Length == 0 || name.Length > MaxUsernameLength)
            {
                return null;
            }

            foreach (char c in name)
            {
                if (IsHidden(c))
                {
                    return null;
                }
            }

            return IsReserved(name) ? null : name;
        }

        /// <summary>
        ///     Whether a name could be mistaken for the mod's system messages.
        /// </summary>
        public static bool IsReserved(string name) =>
            string.Equals(name?.Trim(), Mod.Name, StringComparison.OrdinalIgnoreCase);

        public static bool SameName(string a, string b) =>
            a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Chat text without control or invisible formatting characters (line breaks become spaces), trimmed
        ///     and cut to <paramref name="maxLength" /> without splitting a surrogate pair.
        /// </summary>
        public static string CleanChat(string text, int maxLength = MaxChatLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var sb = new StringBuilder(Math.Min(text.Length, maxLength + 1));
            foreach (char c in text)
            {
                if (sb.Length >= maxLength + 1)
                {
                    break;
                }

                if (c == '\n' || c == '\r' || c == '\t')
                {
                    sb.Append(' ');
                }
                else if (!IsHidden(c))
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString().Trim();
            if (result.Length > maxLength)
            {
                int cut = maxLength;
                if (char.IsHighSurrogate(result[cut - 1]))
                {
                    cut--;
                }

                result = result.Substring(0, cut).TrimEnd();
            }

            return result;
        }

        /// <summary>
        ///     Compares two passwords in constant time (for equal lengths), so timing doesn't leak how much matched.
        /// </summary>
        public static bool PasswordMatches(string expected, string given)
        {
            byte[] a = Encoding.UTF8.GetBytes(expected ?? "");
            byte[] b = Encoding.UTF8.GetBytes(given ?? "");
            using (SHA256 sha = SHA256.Create())
            {
                // Comparing hashes keeps the time independent of where the inputs differ and of their lengths
                byte[] ha = sha.ComputeHash(a);
                byte[] hb = sha.ComputeHash(b);
                int diff = 0;
                for (int i = 0; i < ha.Length; i++)
                {
                    diff |= ha[i] ^ hb[i];
                }

                return diff == 0;
            }
        }

        private static bool IsHidden(char c)
        {
            if (char.IsControl(c))
            {
                return true;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator ||
                   category == UnicodeCategory.ParagraphSeparator || category == UnicodeCategory.PrivateUse;
        }
    }
}
