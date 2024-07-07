using System;

namespace CS2M.Commands.ApiServer.Handler
{
    public abstract class ApiCommandHandler
    {
        public abstract Type GetDataType();

        public abstract void Parse(ApiCommandBase message);
    }

    public abstract class ApiCommandHandler<C> : ApiCommandHandler where C : ApiCommandBase
    {
        protected abstract void Handle(C command);

        public override Type GetDataType()
        {
            return typeof(C);
        }

        public override void Parse(ApiCommandBase command)
        {
            Handle((C) command);
        }
    }
}
