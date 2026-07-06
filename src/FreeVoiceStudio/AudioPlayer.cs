using NAudio.Wave;

namespace FreeVoiceStudio;

/// <summary>
/// One shared player with pause/resume and seeking — starting a new file stops
/// the previous one, so only one thing ever plays at a time.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private AudioFileReader? _reader;
    private bool _stopping;

    /// <summary>Path currently loaded (playing or paused), or null.</summary>
    public string? Current { get; private set; }

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader != null && value >= TimeSpan.Zero && value <= _reader.TotalTime)
                _reader.CurrentTime = value;
        }
    }

    public event Action? PlaybackChanged;

    /// <summary>Play a file; if it's already current, toggle pause/resume.</summary>
    public void Toggle(string path)
    {
        if (Current == path && _out != null)
        {
            if (IsPlaying) _out.Pause();
            else _out.Play();
            PlaybackChanged?.Invoke();
            return;
        }
        Play(path);
    }

    public void Play(string path)
    {
        Stop();
        try
        {
            _reader = new AudioFileReader(path);
            _out = new WaveOutEvent();
            _out.Init(_reader);
            _out.PlaybackStopped += (_, _) =>
            {
                if (_stopping) return;
                Current = null;
                PlaybackChanged?.Invoke(); // natural end of track
            };
            _out.Play();
            Current = path;
        }
        catch
        {
            Current = null;
        }
        PlaybackChanged?.Invoke();
    }

    public void Stop()
    {
        _stopping = true;
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _reader?.Dispose();
        _out = null;
        _reader = null;
        Current = null;
        _stopping = false;
        PlaybackChanged?.Invoke();
    }

    public void Dispose()
    {
        _stopping = true;
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _reader?.Dispose();
    }
}
