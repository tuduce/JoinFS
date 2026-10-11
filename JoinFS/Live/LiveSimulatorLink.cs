using System.Threading;
using System.Threading.Tasks;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The simulator button of the new UI, on the real simulator link. The state is the sim thread's own snapshot, and a request goes to
    /// the sim thread the way the old button's click did, so nothing here touches the connection from the UI thread.
    /// </summary>
    class LiveSimulatorLink : ISimulatorLink
    {
        readonly Main main;

        public LiveSimulatorLink(Main main)
        {
            this.main = main;
        }

        public bool ReportsState => true;

        public void Poll()
        {
        }

        public ConnectionState State
        {
            get
            {
                SimSnapshot view = main.sim?.View;
                if (view == null)
                {
                    return ConnectionState.Disconnected;
                }
                if (view.Connected)
                {
                    return ConnectionState.Connected;
                }
                return view.Connecting ? ConnectionState.Connecting : ConnectionState.Disconnected;
            }
        }

        // ToggleSimulator() decides on the sim thread: connect when idle, close when connected or connecting. Asking only when it
        // would do the wanted thing keeps a late click from undoing what the sim has done since.
        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (State == ConnectionState.Disconnected)
            {
                main.ToggleSimulator();
            }
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            if (State != ConnectionState.Disconnected)
            {
                main.ToggleSimulator();
            }
            return Task.CompletedTask;
        }
    }
}
