namespace SongPrismVR.Configurator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        StartupResult startup = StartupTasks.Run(args);
        UiText.Initialize();
        if (args.Contains("--verify-localization", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startup));
    }
}
