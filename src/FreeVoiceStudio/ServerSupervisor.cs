using System.Diagnostics;

namespace FreeVoiceStudio;

/// <summary>
/// Finds and babysits the Python engine server. If one is already listening on
/// the port it adopts it; otherwise it starts its own (hidden) and kills it on exit.
/// </summary>
public sealed class ServerSupervisor : IDisposable
{
    private Process? _child;

    /// <summary>Directory containing server.py — also where voices/ and output/ live.</summary>
    public string? BackendDir { get; private set; }

    public string? LastError { get; private set; }

    public ServerSupervisor()
    {
        BackendDir = FindBackendDir();
    }

    private static string? FindBackendDir()
    {
        string exeDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "backend"),                                  // installed layout
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..")), // dev: src/FreeVoiceStudio/bin/Release/net8.0-windows -> repo root
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..")),
        };
        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "server.py")))
                return dir;
        }
        return null;
    }

    public async Task<bool> EnsureRunningAsync(ServerClient client)
    {
        if (await client.GetStateAsync() != null)
            return true; // adopt the already-running server

        if (BackendDir == null)
        {
            LastError = "server.py not found — reinstall or check the backend folder.";
            return false;
        }

        try
        {
            _child = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "server.py",
                WorkingDirectory = BackendDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            LastError = $"Couldn't start Python: {ex.Message}. Is Python installed and on PATH?";
            return false;
        }

        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(700);
            if (_child != null && _child.HasExited)
            {
                LastError = "Engine server exited immediately — run 'python server.py' manually to see why (missing pip packages?).";
                return false;
            }
            if (await client.GetStateAsync() != null)
                return true;
        }
        LastError = "Engine server didn't come up in time.";
        return false;
    }

    /// <summary>Let the server outlive the app (jobs still rendering).</summary>
    public void Detach()
    {
        _child?.Dispose();
        _child = null;
    }

    public void Dispose()
    {
        // only kill a server we started ourselves
        if (_child != null && !_child.HasExited)
        {
            try { _child.Kill(entireProcessTree: true); } catch { }
        }
        _child?.Dispose();
    }
}
