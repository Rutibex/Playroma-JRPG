using Playroma.Sound.WinForms.Application;

namespace Playroma.Sound.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        EditorApplicationPaths paths = EditorApplicationPaths.CreateDefault();
        var settingsStore = new EditorSettingsStore(paths.SettingsFilePath);
        EditorSettings settings = settingsStore.Load();
        System.Windows.Forms.Application.Run(new MainForm(paths, settingsStore, settings, args));
    }
}
