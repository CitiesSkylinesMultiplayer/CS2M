namespace CS2M.Commands.ApiServer.Handler
{
    public class ServerRegistrationHandler : ApiCommandHandler<ServerRegistrationCommand>
    {
        protected override void Handle(ServerRegistrationCommand command)
        {
            // Do nothing, this is a packet for the global server
        }
    }
}
