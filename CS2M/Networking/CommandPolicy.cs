using System;
using System.Collections.Generic;
using CS2M.BaseGame.Commands;
using CS2M.Commands.Data.Internal;
using LiteNetLib;

namespace CS2M.Networking
{
    /// <summary>
    ///     Which commands a joiner may send, how big they may be and how they travel.
    ///
    ///     The host is the authority. A command a joiner sends is applied by the host, and relayed to the other
    ///     joiners when its handler has RelayOnServer, only if its type is in <see cref="JoinerCommands" />.
    ///     Every other type is host to joiner only: the host drops it when a joiner sends it (with a
    ///     rate-limited warning), and a joiner ignores it unless it came from the host itself (SenderId 0).
    ///     New command types are host-only until they are added here, so a new sync feature can never be
    ///     driven by a joiner by accident.
    ///
    ///     To let joiners send a new command: add its type to <see cref="JoinerCommands" />, and only if it is
    ///     a player action (something a player does with a tool or panel). Sync state the host computes
    ///     (simulation results, money, progression) must stay host-only. If the command can be bigger than
    ///     <see cref="DefaultMaxBytes" />, add it to <see cref="LargeJoinerCommands" />. If it is sent often and
    ///     only the newest copy matters, give it a lane in <see cref="DeliveryOf" />.
    /// </summary>
    public static class CommandPolicy
    {
        /// <summary>
        ///     Player actions a joiner may originate. Kept in sync with CS2M.BaseGame/Commands and CS2M/Commands/Data.
        /// </summary>
        private static readonly HashSet<Type> JoinerCommands = new HashSet<Type>
        {
            // Building and editing the city
            typeof(RoadCommand),
            typeof(NetDiffCommand),
            typeof(RoadUpgradeCommand),
            typeof(BulldozeCommand),
            typeof(PlaceCommand),
            typeof(ZoneCellsCommand),
            typeof(AreaChangeCommand),
            typeof(TerrainBrushCommand),
            typeof(WaterSourceCommand),
            typeof(TransitLineCommand),
            typeof(TransitLineDeleteCommand),
            typeof(TransitLineSettingsCommand),
            // Panels
            typeof(PolicyCommand),
            typeof(BuildingPolicyCommand),
            typeof(RenameCommand),
            typeof(ColorCommand),
            typeof(TaxCommand),
            typeof(BudgetCommand),
            typeof(SpeedCommand),
            // Requests the host decides on (never relayed: their handlers have RelayOnServer = false)
            typeof(ChargeCommand),
            typeof(LoanRequestCommand),
            typeof(DevTreeRequestCommand),
            typeof(TilePurchaseRequestCommand),
            typeof(ResyncRequestCommand),
            // Presence
            typeof(CursorCommand),
            typeof(ChatMessageCommand),
        };

        /// <summary>
        ///     Joiner commands that can legitimately be large (a long road drag, a big zoning or area apply).
        /// </summary>
        private static readonly HashSet<Type> LargeJoinerCommands = new HashSet<Type>
        {
            typeof(NetDiffCommand),
            typeof(RoadCommand),
            typeof(ZoneCellsCommand),
            typeof(AreaChangeCommand),
            typeof(BulldozeCommand),
            typeof(PlaceCommand),
            typeof(TerrainBrushCommand),
            typeof(TransitLineCommand),
        };

        /// <summary>
        ///     Largest command a joiner may send, unless it is in <see cref="LargeJoinerCommands" />.
        /// </summary>
        public const int DefaultMaxBytes = 64 * 1024;

        /// <summary>
        ///     Largest packet the host reads from a joiner at all (checked before deserializing).
        /// </summary>
        public const int MaxJoinerPacketBytes = 1024 * 1024;

        /// <summary>
        ///     Largest packet the host reads from a peer that has not joined yet (only a PreconditionsCheck).
        /// </summary>
        public const int MaxPendingPacketBytes = 16 * 1024;

        /// <summary>
        ///     Largest packet a joiner reads from the host (world slices are one MTU each).
        /// </summary>
        public const int MaxHostPacketBytes = 8 * 1024 * 1024;

        /// <summary>
        ///     NetManager.ChannelsCount on both sides. Channels are numbered per delivery method.
        /// </summary>
        public const int ChannelsCount = 4;

        /// <summary>
        ///     ReliableOrdered channel for the world transfer and the join handshake (so the success
        ///     message is always ahead of the first slice).
        /// </summary>
        public const byte WorldChannel = 1;

        /// <summary>
        ///     ReliableOrdered channel for everything else (world edits).
        /// </summary>
        public const byte EditChannel = 0;

        /// <summary>
        ///     Whether a joiner may send this command type.
        /// </summary>
        public static bool IsJoinerCommand(Type type) => type != null && JoinerCommands.Contains(type);

        /// <summary>
        ///     Host to joiner only: a joiner must never originate it, and the host never relays it.
        /// </summary>
        public static bool IsHostOnly(Type type) => !IsJoinerCommand(type);

        /// <summary>
        ///     Largest serialized size the host accepts for this command from a joiner.
        /// </summary>
        public static int MaxBytesFromJoiner(Type type) =>
            LargeJoinerCommands.Contains(type) ? MaxJoinerPacketBytes : DefaultMaxBytes;

        /// <summary>
        ///     How a command type travels. Latest-wins data that is resent often goes sequenced on its own
        ///     channel, so a lost or late copy never holds up world edits; everything else is ReliableOrdered.
        /// </summary>
        public static void DeliveryOf(Type type, out DeliveryMethod method, out byte channel)
        {
            if (type == typeof(CursorCommand))
            {
                // 20 a second while moving plus a heartbeat every 2 s: losing one doesn't matter
                method = DeliveryMethod.Sequenced;
                channel = 0;
            }
            else if (type == typeof(MoneyCommand))
            {
                // ReliableSequenced keeps only the newest packet per channel, so each snapshot type has its own
                method = DeliveryMethod.ReliableSequenced;
                channel = 0;
            }
            else if (type == typeof(PopulationCommand))
            {
                method = DeliveryMethod.ReliableSequenced;
                channel = 1;
            }
            else if (type == typeof(BudgetFiguresCommand))
            {
                method = DeliveryMethod.ReliableSequenced;
                channel = 2;
            }
            else if (type == typeof(ProgressionCommand))
            {
                method = DeliveryMethod.ReliableSequenced;
                channel = 3;
            }
            else if (type == typeof(WorldTransferCommand) || type == typeof(PreconditionsSuccessCommand) ||
                     type == typeof(PreconditionsErrorCommand))
            {
                method = DeliveryMethod.ReliableOrdered;
                channel = WorldChannel;
            }
            else
            {
                method = DeliveryMethod.ReliableOrdered;
                channel = EditChannel;
            }
        }
    }
}
