namespace SongPrismVR.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (AutoUpdateRunner.IsRequested(args))
        {
            Environment.ExitCode = AutoUpdateRunner.Run(args);
            return;
        }
        InstallerText.Initialize();
        if (args.Contains("--verify-localization", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        if (args.Contains("--verify-package", StringComparer.OrdinalIgnoreCase))
        {
            if (SongPrismVR.Management.InstallationEngine.FindPackageRoot() is null)
            {
                Environment.ExitCode = 2;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}
