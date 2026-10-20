using System.Windows;
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
        base.OnStartup(e);
    }
}
