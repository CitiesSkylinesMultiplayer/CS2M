namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     What the server browser shows about a session beyond <see cref="SessionInfoCommand" />: the city,
    ///     who is playing and which mods the host runs. Sent right after it with every keep-alive.
    ///     Must fit one unconnected packet, so the host caps the lists and strings (see ApiServer).
    /// </summary>
    /// Sent by:
    /// - Server
    public class SessionDetailsCommand : ApiCommandBase
    {
        /// <summary>
        ///     The server token this belongs to, as in <see cref="SessionInfoCommand.Token" />.
        /// </summary>
        public string Token { get; set; }

        public int Population { get; set; }

        /// <summary>
        ///     Index of the achieved milestone, 0 for none. The browser shows its localized name.
        /// </summary>
        public int Milestone { get; set; }

        /// <summary>
        ///     In-game date: year and month (0-11), as the game's save list shows it.
        /// </summary>
        public int Year { get; set; }

        public int Month { get; set; }

        /// <summary>
        ///     Players in the session, host first, and their cursor colours ("#RRGGBB"), same order.
        /// </summary>
        public string[] PlayerNames { get; set; }

        public string[] PlayerColors { get; set; }

        /// <summary>
        ///     The host's enabled mods besides CS2M: required ones first, then client side ones.
        ///     Ids are Paradox Mods ids, or "local:" plus the assembly name for local code mods.
        ///     ModIds, ModNames, ModVersions and ModClientSide are the same length.
        /// </summary>
        public string[] ModIds { get; set; }

        public string[] ModNames { get; set; }

        public string[] ModVersions { get; set; }

        public bool[] ModClientSide { get; set; }

        /// <summary>
        ///     Where in the host's mod list this packet's mods start. A long list goes in several packets:
        ///     the first (offset 0) has everything, the next ones only Token, ModCount, ModOffset and mods.
        /// </summary>
        public int ModOffset { get; set; }

        /// <summary>
        ///     How many mods the host has enabled in total; more than the lists hold when they were cut short.
        /// </summary>
        public int ModCount { get; set; }
    }
}
