namespace CS2M.Commands.ApiServer.Handler
{
    public class ServerUnregisterHandler : ApiCommandHandler<ServerUnregisterCommand>
    {
        protected override void Handle(ServerUnregisterCommand command)
        {
            // Do nothing, this is a packet for the api server
        }
    }
}
