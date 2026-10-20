using System.Windows;
using Student_Desktop_App.Services.Backup;
using Student_Desktop_App.Services.Lockdown;

namespace Student_Desktop_App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Module 4 crash recovery: if a previous exam was killed before it could restore the
        // Task Manager policy, undo it now — before any window opens.
        TaskManagerRegistryGuard.RecoverAtStartup();

        // Module 6: answer backups of submitted exams are kept 7 days as evidence, abandoned ones 30 days.
        try
        {
            DpapiKeyProtector.CreateDefaultStore().Cleanup(TimeSpan.FromDays(7), TimeSpan.FromDays(30));
        }
        catch (Exception)
        {
            // housekeeping only — never block the app from starting
        }

        base.OnStartup(e);
    }
}
