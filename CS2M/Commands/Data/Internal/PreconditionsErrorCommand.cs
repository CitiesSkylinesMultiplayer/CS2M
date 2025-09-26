using Colossal.UI.Binding;
using CS2M.Util;

namespace CS2M.Commands.Data.Internal
{
    public class PreconditionsErrorCommand : PreconditionsDataCommand
    {
        /// <summary>
        ///     A bitfield of errors from preconditions checking (can be multiple)
        /// </summary>
        public PreconditionsUtil.Errors Errors { get; set; }
    }
}
