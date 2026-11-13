using System.Threading;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The messages the old forms showed in a message box: <c>Main.ShowMessage</c> leaves one in <c>scheduleShowMessage</c> (from any
    /// thread) and the window shows it. A status is the same for what the strip says for a few seconds.
    /// </summary>
    class LiveMessageSource(Main main) : IMessageSource
    {
        public string TakeMessage() => Interlocked.Exchange(ref main.scheduleShowMessage, null);

        public string TakeStatus() => Interlocked.Exchange(ref main.scheduleShowStatus, null);
    }
}
