using JoinFS.UI;

namespace JoinFS.UI.Dev;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => UiHost.RunOnFakeServices(args);
}
