using System;
using System.Collections.Concurrent;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;

namespace CS2M.Networking
{
    /// <summary>
    ///     Host: fixes up a command from a joiner before it is applied and relayed.
    /// </summary>
    public static class JoinerInput
    {
        // Commands with a "Username" (chat, cursors, resync requests): the name others see always comes from the join
        private static readonly ConcurrentDictionary<Type, PropertyInfo> UsernameProperties =
            new ConcurrentDictionary<Type, PropertyInfo>();

        /// <summary>
        ///     Stamps the registered name and applies the chat limits. False drops the command.
        /// </summary>
        public static bool Prepare(CommandBase command, PeerState state)
        {
            PropertyInfo username = UsernameProperties.GetOrAdd(command.GetType(), type =>
            {
                PropertyInfo property = type.GetProperty("Username", BindingFlags.Public | BindingFlags.Instance);
                return property != null && property.PropertyType == typeof(string) && property.CanWrite
                    ? property
                    : null;
            });
            username?.SetValue(command, state.Player.Username);

            if (command is ChatMessageCommand chat)
            {
                if (!state.Chat.TryTake())
                {
                    RateLimitedLog.Warn($"chat:{state.Player.PlayerId}",
                        $"Player {state.Player.PlayerId} chats too fast, dropping messages");
                    return false;
                }

                chat.Message = TextRules.CleanChat(chat.Message);
                if (chat.Message.Length == 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
