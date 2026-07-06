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
        Application.Run(new MainForm());
    }
}
