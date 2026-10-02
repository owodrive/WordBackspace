using System.Windows.Forms;

namespace WordBackspace;

static class Program
{
    [STAThread]
    static void Main()
    {
        bool createdNew;
        using var mutex = new Mutex(true, @"Local\WordBackspace.SingleInstance", out createdNew);
        if (!createdNew)
        {
            Logger.Info("another instance is already running; exiting");
            return;
        }

        Application.ThreadException += (_, e) =>
        {
            Logger.Error($"UI thread exception: {e.Exception}");
            MessageBox.Show(e.Exception.Message, "WordBackspace", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error($"unhandled exception: {e.ExceptionObject}");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Theme.ApplyAtStartup();
        Application.Run(new TrayApp());
        Logger.Info("application exited");
    }
}