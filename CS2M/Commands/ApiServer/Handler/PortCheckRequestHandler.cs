namespace CS2M.Commands.ApiServer.Handler
{
    public class PortCheckRequestHandler : ApiCommandHandler<PortCheckRequestCommand>
    {
        protected override void Handle(PortCheckRequestCommand command)
        {
            // Do nothing, this is a packet for the global server
        }
    }
}
