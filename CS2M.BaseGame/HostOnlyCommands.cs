using System;
using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Which game commands only one side may send.
    ///     <para>
    ///         Host-only commands carry the host's decisions and state (spawned buildings, level-ups, money,
    ///         progression, map tiles...). Only the host sends them, to every joiner. The host must drop them when
    ///         they come from a peer and must never relay them.
    ///     </para>
    ///     <para>
    ///         Joiner requests (charges, loans, development tree and map tile purchases) go from one joiner to the
    ///         host, which checks and applies them. They are never relayed, and joiners ignore them.
    ///     </para>
    ///     The network layer can use <see cref="IsHostOnly" /> and <see cref="IsJoinerRequest" /> to drop these
    ///     before parsing or relaying. The handlers in this assembly also check them themselves with
    ///     <see cref="Accept" />, so a command that gets through is still refused.
    /// </summary>
    public static class HostOnlyCommands
    {
        private static readonly HashSet<Type> HostOnly = new HashSet<Type>
        {
            typeof(SpawnCommand),
            typeof(LevelUpCommand),
            typeof(AbandonCommand),
            typeof(MoneyCommand),
            typeof(PopulationCommand),
            typeof(BudgetFiguresCommand),
            typeof(DistrictNamesCommand),
            typeof(ProgressionCommand),
            typeof(DevTreeCommand),
            typeof(MapTilesCommand)
        };

        private static readonly HashSet<Type> JoinerRequests = new HashSet<Type>
        {
            typeof(ChargeCommand),
            typeof(LoanRequestCommand),
            typeof(DevTreeRequestCommand),
            typeof(TilePurchaseRequestCommand)
        };

        /// <summary>
        ///     Every host-only command type.
        /// </summary>
        public static IEnumerable<Type> HostOnlyTypes => HostOnly;

        /// <summary>
        ///     Every joiner-to-host request type.
        /// </summary>
        public static IEnumerable<Type> JoinerRequestTypes => JoinerRequests;

        /// <summary>
        ///     True for commands that only the host may send (to joiners). The host drops them from peers and
        ///     never relays them; joiners accept them only from the host (SenderId 0).
        /// </summary>
        public static bool IsHostOnly(Type commandType) => commandType != null && HostOnly.Contains(commandType);

        /// <summary>
        ///     True for joiner-to-host requests. Only the host applies them, and they are never relayed.
        /// </summary>
        public static bool IsJoinerRequest(Type commandType) => commandType != null && JoinerRequests.Contains(commandType);

        /// <summary>
        ///     True when the command may be relayed by the host to the other joiners.
        /// </summary>
        public static bool MayRelay(Type commandType) => !IsHostOnly(commandType) && !IsJoinerRequest(commandType);

        /// <summary>
        ///     Whether this game should accept a received command, from the local role and the sender.
        ///     The host stamps SenderId from the connection, and its own messages carry 0, so on a joiner a
        ///     host-only command with SenderId != 0 was relayed from another joiner.
        /// </summary>
        public static bool Accept(CommandBase command)
        {
            if (command == null)
            {
                return false;
            }

            Type type = command.GetType();
            MultiplayerRole role = Command.CurrentRole;
            if (IsHostOnly(type))
            {
                // the host is the authority: a host-only command reaching it came from a joiner
                if (role == MultiplayerRole.Server || command.SenderId != 0)
                {
                    Inbound.Warn("hostonly:" + type.Name,
                        $"Refused {type.Name} from player {command.SenderId}: only the host sends it");
                    return false;
                }

                return true;
            }

            if (IsJoinerRequest(type) && role != MultiplayerRole.Server)
            {
                Inbound.Warn("request:" + type.Name, $"Refused {type.Name} from player {command.SenderId}: only the host applies it");
                return false;
            }

            return true;
        }
    }
}
