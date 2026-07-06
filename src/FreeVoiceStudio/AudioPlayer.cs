using NAudio.Wave;

namespace FreeVoiceStudio;

/// <summary>One shared player — starting a new file stops the previous one.</summary>
public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private AudioFileReader? _reader;

    /// <summary>Path currently playing, or null.</summary>
    public string? Playing { get; private set; }

    public event Action? PlaybackChanged;

    public void Toggle(string path)
    {
        if (Playing == path)
        {
            Stop();
            return;
        }
        Stop();
        try
        {
            _reader = new AudioFileReader(path);
            _out = new WaveOutEvent();
            _out.Init(_reader);
            _out.PlaybackStopped += (_, _) =>
            {
                Playing = null;
                PlaybackChanged?.Invoke();
            };
            _out.Play();
            Playing = path;
        }
        catch
        {
            Playing = null;
        }
        PlaybackChanged?.Invoke();
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _reader?.Dispose();
        _out = null;
        _reader = null;
        Playing = null;
        PlaybackChanged?.Invoke();
    }

    public void Dispose() => Stop();
}
