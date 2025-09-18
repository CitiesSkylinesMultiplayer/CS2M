namespace CS2M.Commands.Data.Internal
{
    public class PreconditionsCheckCommand : PreconditionsDataCommand
    {
        /// <summary>
        ///     The username this user will be playing as, important
        ///     as the server will keep track of this user.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        ///     An optional password if the server is set up to
        ///     require a password.
        /// </summary>
        public string Password { get; set; }
    }
}
