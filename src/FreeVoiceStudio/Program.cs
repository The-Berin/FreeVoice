namespace FreeVoiceStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "FreeVoiceStudio_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("FreeVoice Studio is already running.", "FreeVoice",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeVoice");
        Directory.CreateDirectory(logDir);
        string logPath = Path.Combine(logDir, "studio-log.txt");
        Application.ThreadException += (_, e) =>
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now}: {e.Exception}\n"); } catch { }
            MessageBox.Show($"FreeVoice hit an unexpected error (logged):\n{e.Exception.Message}",
                "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now} FATAL: {e.ExceptionObject}\n"); } catch { }
        };

        Application.Run(new MainForm());
    }
}
