using OpenMonitorManager.Services;
using OpenMonitorManager.UI;

namespace OpenMonitorManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\OpenMonitorManager.9B320821-133C-4E61-8B78-B41550275CC8",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Open Monitor Manager is already running in the notification area.",
                "Open Monitor Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var monitorService = new MonitorService();
        var profileStore = new ProfileStore();
        using var recovery = new RecoveryCoordinator(monitorService);
        using var mainForm = new MainForm(monitorService, profileStore, recovery);
        Application.Run(mainForm);
    }
}
