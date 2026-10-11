using System.Threading;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The messages the old forms showed in a message box: <c>Main.ShowMessage</c> leaves one in <c>scheduleShowMessage</c> (from any
    /// thread) and the window shows it
    /// </summary>
    class LiveMessageSource(Main main) : IMessageSource
    {
        public string TakeMessage() => Interlocked.Exchange(ref main.scheduleShowMessage, null);
    }
}
