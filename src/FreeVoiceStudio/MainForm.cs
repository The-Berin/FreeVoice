using System.Runtime.InteropServices;

namespace FreeVoiceStudio;

public sealed class MainForm : Form
{
    private readonly ServerClient _client = new();
    private readonly ServerSupervisor _supervisor = new();
    private readonly AudioPlayer _player = new();
    private StateDto _state = new();
    private string _lastVoicesJson = "", _lastOutputsJson = "", _jobsStructureKey = "";
    private bool _connected;
    private int _offlineTicks;
    private bool _reviving;
    private NotifyIcon? _tray;
    private readonly Dictionary<string, string> _knownJobStates = new();

    private readonly Panel _sidebar = new();
    private readonly Panel _content = new();
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, Button> _nav = new();
    private Label _sideStatus = null!;

    // Studio controls
    private TextBox _script = null!, _title = null!;
    private Label _wordInfo = null!;
    private FlowLayoutPanel _engineRow = null!;
    private readonly List<EngineCard> _engineCards = new();
    private string _engine = "chatterbox";
    private ComboBox _voice = null!, _kokoroVoice = null!, _effect = null!;
    private Label _kokoroLabel = null!;
    private Segmented _format = null!;
    private FvToggle _clean = null!;
    private FvSlider _exaggeration = null!, _cfg = null!, _speed = null!, _nfe = null!;
    private NumericUpDown _seed = null!;
    private GradientButton _generate = null!;
    private Panel _jobsPanel = null!;
    private sealed class JobHolder { public JobDto Job = new(); }
    private readonly Dictionary<string, (Panel Row, JobHolder Holder)> _jobRows = new();
    // play buttons update their glyph in place — lists never rebuild just because playback changed
    private readonly List<(Button Btn, string Path)> _playButtons = new();

    // Voices
    private TextBox _voiceName = null!, _voiceTranscript = null!;
    private Label _voiceFileLabel = null!;
    private string? _voiceFile;
    private Panel _voiceList = null!;

    // Library
    private Panel _outputList = null!;
    private Label _playerTrack = null!, _playerTime = null!;
    private Button _playerToggle = null!;
    private SeekBar _seekBar = null!;

    private const int ContentWidth = 750;

    public MainForm()
    {
        // Real native title bar — dark via DWM. The green accent came from the Windows
        // "accent color on title bars" setting, which is disabled machine-side instead.
        Text = "FreeVoice Studio";
        Size = new Size(1080, 700);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Back;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { }
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);

        BuildSidebar();
        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(28, 18, 24, 18);
        Controls.Add(_content);
        Controls.Add(_sidebar);

        AddPage("Studio", BuildStudio());
        AddPage("Voices", BuildVoices());
        AddPage("Library", BuildLibrary());
        AddPage("About", BuildAbout());

        Theme.Apply(this);
        ShowPage("Studio");

        _tray = new NotifyIcon { Visible = true, Text = "FreeVoice Studio" };
        try { _tray.Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); }
        catch { _tray.Icon = SystemIcons.Application; }
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        var poll = new System.Windows.Forms.Timer { Interval = 1000 };
        poll.Tick += async (_, _) => await RefreshState();
        poll.Start();

        var playerTick = new System.Windows.Forms.Timer { Interval = 100 };
        playerTick.Tick += (_, _) => UpdatePlayerBar();
        playerTick.Start();

        // 60fps repaint of running job rows — the time-based progress bar glides
        var anim = new System.Windows.Forms.Timer { Interval = 16 };
        anim.Tick += (_, _) =>
        {
            foreach (var (row, holder) in _jobRows.Values)
                if (holder.Job.State == "running")
                    row.Invalidate();
        };
        anim.Start();

        _player.PlaybackChanged += () => { if (!IsDisposed) BeginInvoke(() => { UpdatePlayButtons(); UpdatePlayerBar(); }); };


        Shown += async (_, _) =>
        {
            SetStatus("starting engine…");
            bool ok = await _supervisor.EnsureRunningAsync(_client);
            SetStatus(ok ? "" : "engine offline — retrying…");
            await RefreshState();
        };
        FormClosed += (_, _) =>
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _player.Dispose();
            if (_state.Jobs.Any(j => j.State is "queued" or "running"))
                _supervisor.Detach(); // let renders finish in the background
            _supervisor.Dispose();
        };
    }

    #region layout scaffolding

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 206;
        _sidebar.BackColor = Theme.Panel;

        var logo = new PictureBox
        {
            Size = new Size(40, 40),
            Location = new Point(20, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        try { logo.Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "logo64.png")); } catch { }
        _sidebar.Controls.Add(logo);
        _sidebar.Controls.Add(new Label
        {
            Text = "FreeVoice",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Theme.Text,
            Location = new Point(68, 17),
            AutoSize = true,
        });
        _sidebar.Controls.Add(new Label
        {
            Text = "local tts studio",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.Sub,
            Location = new Point(70, 41),
            AutoSize = true,
        });

        _sideStatus = new Label
        {
            Location = new Point(20, 0),
            Size = new Size(174, 76),
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Text = "all local · free forever",
        };
        _sidebar.Controls.Add(_sideStatus);
        _sidebar.Resize += (_, _) => _sideStatus.Top = _sidebar.Height - 86;
    }

    private void AddPage(string name, Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[name] = page;
        _content.Controls.Add(page);

        var b = new Button
        {
            Text = "    " + name,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(182, 38),
            Location = new Point(12, 84 + _nav.Count * 42),
            Font = new Font("Segoe UI", 10f),
            ForeColor = Theme.Text,
            BackColor = Theme.Panel,
            Tag = "custom",
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Theme.Card;
        b.Click += (_, _) => ShowPage(name);
        _nav[name] = b;
        _sidebar.Controls.Add(b);
    }

    private void ShowPage(string name)
    {
        foreach (var (k, p) in _pages) p.Visible = k == name;
        foreach (var (k, b) in _nav)
        {
            b.BackColor = k == name ? Theme.A1 : Theme.Panel;
            b.ForeColor = k == name ? Color.White : Theme.Text;
        }
    }

    private static Label Header(string text, int y) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 14f),
        ForeColor = Theme.Text,
        Location = new Point(0, y),
        AutoSize = true,
    };

    private static Label Note(string text, int y, int w = 700) => new()
    {
        Text = text,
        ForeColor = Theme.Sub,
        Font = new Font("Segoe UI", 8.75f),
        Location = new Point(0, y),
        MaximumSize = new Size(w, 0),
        AutoSize = true,
    };

    private static Label SmallLabel(string text, int x, int y) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
        ForeColor = Theme.Dim,
        Location = new Point(x, y),
        AutoSize = true,
    };

    private void SetStatus(string s)
        => _sideStatus.Text = "all local · free forever" + (s.Length > 0 ? "\n" + s : "");

    #endregion

    #region studio page

    private Panel BuildStudio()
    {
        var p = new Panel();
        int y = 0;
        p.Controls.Add(Header("Studio", y));
        p.Controls.Add(new Label
        {
            Text = "switch speakers with [VoiceName] at the start of a line",
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(120, y + 9),
            AutoSize = true,
        });
        y += 36;

        p.Controls.Add(SmallLabel("Script", 0, y)); y += 17;
        _script = new TextBox
        {
            Multiline = true,
            Location = new Point(0, y),
            Size = new Size(ContentWidth, 112),
            Font = new Font("Segoe UI", 10.25f),
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        _script.TextChanged += (_, _) => UpdateWordInfo();
        p.Controls.Add(_script); y += 118;

        _wordInfo = new Label { Location = new Point(2, y + 3), AutoSize = true, ForeColor = Theme.Dim, Font = new Font("Segoe UI", 8.5f) };
        p.Controls.Add(_wordInfo);
        _title = new TextBox { Location = new Point(ContentWidth - 230, y), Width = 230, Font = new Font("Segoe UI", 8.75f), PlaceholderText = "output name (optional)" };
        p.Controls.Add(_title); y += 32;

        p.Controls.Add(SmallLabel("Engine", 0, y)); y += 17;
        _engineRow = new FlowLayoutPanel { Location = new Point(0, y), Size = new Size(ContentWidth + 20, 108), BackColor = Theme.Back };
        p.Controls.Add(_engineRow); y += 112;

        p.Controls.Add(SmallLabel("Voice", 0, y));
        _kokoroLabel = SmallLabel("Kokoro preset", 155, y);
        p.Controls.Add(_kokoroLabel);
        p.Controls.Add(SmallLabel("Effect", 310, y));
        p.Controls.Add(SmallLabel("Format", 470, y));
        p.Controls.Add(SmallLabel("Clean audio", 615, y));
        y += 17;

        _voice = new ComboBox { Location = new Point(0, y), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
        _kokoroVoice = new ComboBox { Location = new Point(155, y), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
        _effect = new ComboBox { Location = new Point(310, y), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
        _format = new Segmented(new[] { "MP3", "WAV" }) { Location = new Point(470, y - 2), Size = new Size(126, 30) };
        _clean = new FvToggle("denoise", true) { Location = new Point(615, y), Width = 135 };
        p.Controls.Add(_voice);
        p.Controls.Add(_kokoroVoice);
        p.Controls.Add(_effect);
        p.Controls.Add(_format);
        p.Controls.Add(_clean);
        y += 38;

        p.Controls.Add(SmallLabel("Delivery", 0, y)); y += 17;
        _exaggeration = new FvSlider("Emotion (Chatterbox)", 0.25, 1.0, 0.5, 0.05) { Location = new Point(0, y), Width = 360 };
        _cfg = new FvSlider("Pace — lower = slower", 0.2, 0.8, 0.5, 0.05) { Location = new Point(390, y), Width = 360 };
        y += 46;
        _speed = new FvSlider("Speed (Kokoro / F5)", 0.7, 1.4, 1.0, 0.05) { Location = new Point(0, y), Width = 360 };
        _nfe = new FvSlider("Quality steps (F5)", 16, 64, 32, 4, v => ((int)v).ToString()) { Location = new Point(390, y), Width = 360 };
        p.Controls.Add(_exaggeration);
        p.Controls.Add(_cfg);
        p.Controls.Add(_speed);
        p.Controls.Add(_nfe);
        y += 50;

        p.Controls.Add(SmallLabel("Seed", 0, y + 8));
        _seed = new NumericUpDown
        {
            Location = new Point(42, y + 5),
            Width = 82,
            Minimum = 0,
            Maximum = 999999,
            BackColor = Theme.Card2,
            ForeColor = Theme.Text,
        };
        var seedHint = new ToolTip();
        seedHint.SetToolTip(_seed, "Same seed + same script repeats a take exactly. 0 = random.");
        p.Controls.Add(_seed);

        _generate = new GradientButton { Text = "Generate", Location = new Point(140, y), Size = new Size(ContentWidth - 140, 42) };
        _generate.Click += async (_, _) => await OnGenerate();
        p.Controls.Add(_generate); y += 52;

        _jobsPanel = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(ContentWidth + 22, 200),
            BackColor = Theme.Back,
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
        };
        p.Controls.Add(_jobsPanel);
        p.Resize += (_, _) => _jobsPanel.Height = Math.Max(80, p.Height - _jobsPanel.Top);

        UpdateWordInfo();
        return p;
    }

    private void UpdateWordInfo()
    {
        int words = _script.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var eng = _state.Engines.FirstOrDefault(e => e.Id == _engine);
        string eta = "";
        if (eng != null && words > 0)
        {
            double s = words * eng.SecPerWord;
            eta = $" · ≈{FmtTime(s)} render";
        }
        _wordInfo.Text = $"{words} {(words == 1 ? "word" : "words")}{eta}";
    }

    private void RenderEngines()
    {
        if (_engineCards.Count > 0) return;
        foreach (var e in _state.Engines)
        {
            var card = new EngineCard(e.Id, e.Name, e.Tier, e.Desc,
                e.SecPerWord < 1 ? "near-instant" : $"~{e.SecPerWord:0.#}s per word on this CPU")
            {
                Margin = new Padding(0, 0, 12, 0),
                IsSelected = e.Id == _engine,
            };
            card.Selected += id =>
            {
                _engine = id;
                foreach (var c in _engineCards) c.IsSelected = c.EngineId == id;
                bool kokoro = id == "kokoro";
                _kokoroVoice.Visible = kokoro;
                _kokoroLabel.Visible = kokoro;
                UpdateWordInfo();
            };
            _engineCards.Add(card);
            _engineRow.Controls.Add(card);
        }
        bool k = _engine == "kokoro";
        _kokoroVoice.Visible = k;
        _kokoroLabel.Visible = k;
    }

    private async Task OnGenerate()
    {
        if (!_connected)
        {
            MessageBox.Show("The engine isn't running yet — give it a few seconds (it restarts itself) and try again.",
                "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var payload = new
        {
            script = _script.Text,
            title = _title.Text,
            engine = _engine,
            voice = _voice.SelectedItem as string == "Default narrator" ? "" : _voice.SelectedItem as string ?? "",
            kokoro_voice = _kokoroVoice.SelectedItem as string ?? "am_michael",
            @params = new
            {
                exaggeration = _exaggeration.Value,
                cfg = _cfg.Value,
                speed = _speed.Value,
                nfe = _nfe.Value,
                seed = (int)_seed.Value,
            },
            effect = _effect.SelectedItem as string ?? "None",
            clean = _clean.Checked,
            format = _format.Value.ToLowerInvariant(),
        };
        _generate.Enabled = false;
        _generate.Text = "Queued ✓";
        var (ok, msg) = await _client.GenerateAsync(payload);
        if (!ok)
            MessageBox.Show(msg, "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        await RefreshState();

        var restore = new System.Windows.Forms.Timer { Interval = 1500 };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            restore.Dispose();
            _generate.Text = "Generate";
            _generate.Enabled = true;
        };
        restore.Start();
    }

    /// <summary>Rebuild rows only when the job set/states change; otherwise just repaint —
    /// so buttons never vanish under the user's cursor mid-click.</summary>
    private void RenderJobs()
    {
        string key = string.Join("|", _state.Jobs.Select(j => j.Id + j.State + (j.Result ?? "")));

        if (key != _jobsStructureKey)
        {
            _jobsStructureKey = key;
            _jobsPanel.SuspendLayout();
            _jobsPanel.Controls.Clear();
            _jobRows.Clear();
            _playButtons.RemoveAll(pb => pb.Btn.IsDisposed || pb.Btn.Parent == null);
            int y = 0;
            foreach (var j in _state.Jobs)
            {
                var holder = new JobHolder { Job = j };
                var row = BuildJobRow(holder);
                row.Location = new Point(0, y);
                _jobsPanel.Controls.Add(row);
                _jobRows[j.Id] = (row, holder);
                y += row.Height + 10;
            }
            _jobsPanel.ResumeLayout();
        }
        else
        {
            foreach (var j in _state.Jobs)
            {
                if (_jobRows.TryGetValue(j.Id, out var entry))
                {
                    entry.Holder.Job = j;
                    if (j.State == "running")
                        entry.Row.Invalidate();
                }
            }
        }
    }

    private Panel BuildJobRow(JobHolder holder)
    {
        var j0 = holder.Job;
        bool running = j0.State == "running";
        var row = new Panel { Size = new Size(ContentWidth, running ? 72 : 58), BackColor = Theme.Card2 };
        row.Paint += (_, e) =>
        {
            var j = holder.Job;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);
            using (var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10))
            {
                using var bg = new SolidBrush(Theme.Card2);
                using var pen = new Pen(Theme.Border);
                g.FillPath(bg, path);
                g.DrawPath(pen, path);
            }

            bool isRunning = j.State == "running";
            float textTop = isRunning ? 8 : (row.Height - 40) / 2f; // vertically centered when there's no bar

            var stateColor = j.State switch
            {
                "done" => Theme.Ok,
                "running" => Theme.A2,
                "queued" => Theme.Sub,
                _ => Theme.Err,
            };
            using (var badgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var badgeBrush = new SolidBrush(stateColor))
                g.DrawString(j.State.ToUpperInvariant(), badgeFont, badgeBrush, 14, textTop + 3);

            using (var titleFont = new Font("Segoe UI Semibold", 9.75f))
            using (var textBrush = new SolidBrush(Theme.Text))
            {
                string title = string.IsNullOrEmpty(j.Title)
                    ? $"{j.Words} {(j.Words == 1 ? "word" : "words")}"
                    : j.Title;
                g.DrawString(title, titleFont, textBrush, 78, textTop);
            }

            double elapsed = j.Started is double st
                ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - st)
                : 0;
            string status = j.StatusText;
            if (isRunning && j.Started != null)
            {
                int remaining = Math.Max(0, (j.EstSeconds ?? 0) - (int)elapsed);
                status += $"  ·  {FmtTime(elapsed)} elapsed";
                if (j.EstSeconds != null)
                    status += remaining > 0 ? $" · ~{FmtTime(remaining)} left" : " · almost done…";
            }
            using (var subFont = new Font("Segoe UI", 8.5f))
            using (var subBrush = new SolidBrush(Theme.Sub))
                g.DrawString(status, subFont, subBrush, new RectangleF(78, textTop + 21, row.Width - 260, 18));

            if (isRunning)
            {
                var track = new RectangleF(14, row.Height - 13, row.Width - 28, 5);
                using var trackBg = new SolidBrush(Color.FromArgb(42, 42, 56));
                using (var trackPath = Theme.Rounded(track, 2.5f))
                    g.FillPath(trackBg, trackPath);

                float chunkFrac = j.Total > 0 ? (float)j.Done / j.Total : 0;
                float timeFrac = j.EstSeconds is int est and > 0 ? (float)(elapsed / est) : 0;
                float t = Math.Clamp(Math.Max(chunkFrac, timeFrac), 0.02f, 0.97f);

                var fill = new RectangleF(track.X, track.Y, Math.Max(4, track.Width * t), track.Height);
                using var fillBrush = new System.Drawing.Drawing2D.LinearGradientBrush(fill, Theme.A1, Theme.A2, 0f);
                using (var fillPath = Theme.Rounded(fill, 2.5f))
                    g.FillPath(fillBrush, fillPath);
            }
        };

        int btnY = running ? 14 : (row.Height - 30) / 2;
        if (j0.State == "done" && j0.Result != null && _supervisor.BackendDir != null)
        {
            string path = Path.Combine(_supervisor.BackendDir, "output", j0.Result);
            var play = MakeMiniButton(PlayGlyph(path), new Point(ContentWidth - 176, btnY));
            play.Click += (_, _) => _player.Toggle(path);
            _playButtons.Add((play, path));
            row.Controls.Add(play);
        }
        else if (j0.State is "queued" or "running")
        {
            var cancel = MakeMiniButton("cancel", new Point(ContentWidth - 176, btnY));
            cancel.Click += async (_, _) => { await _client.CancelJobAsync(j0.Id); await RefreshState(); };
            row.Controls.Add(cancel);
        }

        if (j0.State is not ("queued" or "running"))
        {
            var close = MakeMiniButton("✕", new Point(ContentWidth - 62, btnY));
            close.Width = 44;
            close.Click += async (_, _) =>
            {
                await _client.RemoveJobAsync(j0.Id);
                await RefreshState();
            };
            row.Controls.Add(close);
        }

        return row;
    }

    private static string FmtTime(double s)
        => s < 60 ? $"{s:0}s" : $"{(int)s / 60}:{(int)s % 60:00}";

    private static Button MakeMiniButton(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        Size = new Size(100, 30),
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Card,
        ForeColor = Theme.Sub,
        Font = new Font("Segoe UI", 8.75f),
        Cursor = Cursors.Hand,
        Tag = "custom",
    };

    #endregion

    #region voices page

    private Panel BuildVoices()
    {
        var p = new Panel();
        int y = 0;
        p.Controls.Add(Header("Voices", y)); y += 30;
        p.Controls.Add(Note("Clone anyone from 7–20 seconds of clean speech (no music, no noise). Works with Chatterbox and F5-TTS.", y)); y += 30;

        var pick = new Button { Text = "Choose audio file…", Location = new Point(0, y), Size = new Size(150, 32) };
        _voiceFileLabel = new Label { Location = new Point(162, y + 7), AutoSize = true, ForeColor = Theme.Sub };
        pick.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.flac;*.m4a;*.ogg" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _voiceFile = dlg.FileName;
                _voiceFileLabel.Text = Path.GetFileName(dlg.FileName);
                if (_voiceName.Text.Length == 0)
                    _voiceName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        };
        p.Controls.Add(pick);
        p.Controls.Add(_voiceFileLabel); y += 40;

        _voiceName = new TextBox { Location = new Point(0, y), Width = 190, PlaceholderText = "voice name" };
        _voiceTranscript = new TextBox { Location = new Point(200, y), Width = 400, PlaceholderText = "what is said in the sample (optional, helps F5)" };
        var save = new GradientButton { Text = "Save voice", Location = new Point(612, y - 3), Size = new Size(138, 32) };
        save.Click += async (_, _) =>
        {
            if (_voiceFile == null) { MessageBox.Show("Pick an audio file first."); return; }
            var (ok, msg) = await _client.AddVoiceAsync(_voiceFile, _voiceName.Text, _voiceTranscript.Text);
            if (!ok) MessageBox.Show(msg, "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
            {
                _voiceFile = null;
                _voiceFileLabel.Text = "";
                _voiceName.Text = "";
                _voiceTranscript.Text = "";
            }
            _lastVoicesJson = "";
            await RefreshState();
        };
        p.Controls.Add(_voiceName);
        p.Controls.Add(_voiceTranscript);
        p.Controls.Add(save); y += 48;

        _voiceList = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(ContentWidth + 22, 300),
            BackColor = Theme.Back,
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
        };
        p.Controls.Add(_voiceList);
        p.Resize += (_, _) => _voiceList.Height = Math.Max(80, p.Height - _voiceList.Top);
        return p;
    }

    private void RenderVoices()
    {
        _voiceList.SuspendLayout();
        _voiceList.Controls.Clear();
        int y = 0;
        foreach (var v in _state.Voices)
        {
            var voice = v;
            var row = new Panel { Size = new Size(ContentWidth, 58), Location = new Point(0, y), BackColor = Theme.Card2 };
            string? avatarPath = _supervisor.BackendDir != null
                ? Path.Combine(_supervisor.BackendDir, "voices", v.Name + ".png")
                : null;
            row.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Theme.Back);
                using (var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10))
                {
                    using var bg = new SolidBrush(Theme.Card2);
                    using var pen = new Pen(Theme.Border);
                    g.FillPath(bg, path);
                    g.DrawPath(pen, path);
                }

                var av = new RectangleF(12, 11, 36, 36);
                if (avatarPath != null && File.Exists(avatarPath))
                {
                    try
                    {
                        using var img = Image.FromFile(avatarPath);
                        using var clip = new System.Drawing.Drawing2D.GraphicsPath();
                        clip.AddEllipse(av);
                        g.SetClip(clip);
                        g.DrawImage(img, av);
                        g.ResetClip();
                    }
                    catch { DrawInitialAvatar(g, av, voice.Name); }
                }
                else
                {
                    DrawInitialAvatar(g, av, voice.Name);
                }

                using var nameFont = new Font("Segoe UI Semibold", 10.5f);
                using var textBrush = new SolidBrush(Theme.Text);
                g.DrawString(voice.Name, nameFont, textBrush, 60, 9);
                using var subFont = new Font("Segoe UI", 8.5f);
                using var subBrush = new SolidBrush(Theme.Dim);
                string sub = $"{voice.Seconds:0.#}s sample" +
                             (voice.Transcript.Length > 0 ? "  ·  transcript ✓" : "");
                g.DrawString(sub, subFont, subBrush, 60, 31);
            };

            if (_supervisor.BackendDir != null)
            {
                string path = Path.Combine(_supervisor.BackendDir, "voices", v.File);
                var play = MakeMiniButton(PlayGlyph(path), new Point(ContentWidth - 290, 14));
                play.Click += (_, _) => _player.Toggle(path);
                _playButtons.Add((play, path));
                row.Controls.Add(play);
            }
            var edit = MakeMiniButton("edit", new Point(ContentWidth - 182, 14));
            edit.Width = 80;
            edit.Click += (_, _) => ShowEditVoice(voice);
            row.Controls.Add(edit);
            var del = MakeMiniButton("delete", new Point(ContentWidth - 94, 14));
            del.Width = 80;
            del.Click += async (_, _) =>
            {
                if (MessageBox.Show($"Delete voice \"{voice.Name}\"?", "FreeVoice",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    await _client.DeleteVoiceAsync(voice.Name);
                    _lastVoicesJson = "";
                    await RefreshState();
                }
            };
            row.Controls.Add(del);

            _voiceList.Controls.Add(row);
            y += 68;
        }
        if (_state.Voices.Count == 0)
        {
            _voiceList.Controls.Add(new Label
            {
                Text = "No cloned voices yet — add one above.",
                ForeColor = Theme.Dim,
                Location = new Point(4, 8),
                AutoSize = true,
            });
        }
        _voiceList.ResumeLayout();
    }

    private static void DrawInitialAvatar(Graphics g, RectangleF av, string name)
    {
        using var avBrush = new System.Drawing.Drawing2D.LinearGradientBrush(av, Theme.A1, Theme.A2, 45f);
        g.FillEllipse(avBrush, av);
        using var avFont = new Font("Segoe UI Semibold", 13f);
        string initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        g.DrawString(initial, avFont, Brushes.White, av.X + 8, av.Y + 5);
    }

    /// <summary>Edit a voice: rename, transcript, profile picture. Direct file ops — the backend is local.</summary>
    private void ShowEditVoice(VoiceDto v)
    {
        if (_supervisor.BackendDir == null) return;
        string voicesDir = Path.Combine(_supervisor.BackendDir, "voices");

        using var dlg = new Form
        {
            Text = $"Edit voice — {v.Name}",
            Size = new Size(480, 300),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Back,
        };
        Theme.DarkTitleBar(dlg);

        dlg.Controls.Add(SmallLabel("Name", 20, 20));
        var name = new TextBox { Text = v.Name, Location = new Point(20, 42), Width = 420 };
        dlg.Controls.Add(name);

        dlg.Controls.Add(SmallLabel("Transcript of the sample (helps F5 accuracy)", 20, 80));
        var transcript = new TextBox
        {
            Text = v.Transcript,
            Location = new Point(20, 102),
            Size = new Size(420, 52),
            Multiline = true,
        };
        dlg.Controls.Add(transcript);

        dlg.Controls.Add(SmallLabel("Profile picture", 20, 166));
        string? newAvatar = null;
        string avatarPath = Path.Combine(voicesDir, v.Name + ".png");
        var picBtn = new Button
        {
            Text = File.Exists(avatarPath) ? "Change picture…" : "Choose picture…",
            Location = new Point(20, 188),
            Size = new Size(140, 30),
        };
        var picLabel = new Label { Location = new Point(170, 195), AutoSize = true, ForeColor = Theme.Sub };
        picBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (ofd.ShowDialog(dlg) == DialogResult.OK)
            {
                newAvatar = ofd.FileName;
                picLabel.Text = Path.GetFileName(ofd.FileName);
            }
        };
        dlg.Controls.Add(picBtn);
        dlg.Controls.Add(picLabel);

        var save = new GradientButton { Text = "Save", Location = new Point(230, 226), Size = new Size(100, 32), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(340, 226), Size = new Size(100, 32), DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(save);
        dlg.Controls.Add(cancel);
        Theme.Apply(dlg);

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string newName = new string(name.Text.Trim()
                .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray());
            if (newName.Length == 0) newName = v.Name;

            if (!newName.Equals(v.Name, StringComparison.Ordinal))
            {
                string ext = Path.GetExtension(v.File);
                File.Move(Path.Combine(voicesDir, v.File), Path.Combine(voicesDir, newName + ext), overwrite: false);
                foreach (var side in new[] { ".txt", ".png" })
                {
                    string old = Path.Combine(voicesDir, v.Name + side);
                    if (File.Exists(old))
                        File.Move(old, Path.Combine(voicesDir, newName + side), overwrite: true);
                }
            }

            string txtPath = Path.Combine(voicesDir, newName + ".txt");
            if (transcript.Text.Trim().Length > 0)
                File.WriteAllText(txtPath, transcript.Text.Trim());
            else if (File.Exists(txtPath))
                File.Delete(txtPath);

            if (newAvatar != null)
            {
                using var img = Image.FromFile(newAvatar);
                using var square = new Bitmap(128, 128);
                using (var g = Graphics.FromImage(square))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    int side = Math.Min(img.Width, img.Height);
                    g.DrawImage(img, new Rectangle(0, 0, 128, 128),
                        new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side),
                        GraphicsUnit.Pixel);
                }
                square.Save(Path.Combine(voicesDir, newName + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            _lastVoicesJson = "";
            _ = RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save: {ex.Message}", "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    #endregion

    #region library page

    private Panel BuildLibrary()
    {
        var p = new Panel();
        p.Controls.Add(Header("Library", 0));
        var open = new Button { Text = "Open folder", Location = new Point(120, 2), Size = new Size(104, 28) };
        open.Click += async (_, _) => await _client.OpenFolderAsync();
        p.Controls.Add(open);

        // player bar pinned to the bottom
        var bar = new Panel
        {
            Height = 62,
            Dock = DockStyle.Bottom,
            BackColor = Theme.Card,
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        _playerTrack = new Label
        {
            Location = new Point(12, 8),
            AutoSize = false,
            Size = new Size(430, 17),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 8.75f),
            Text = "nothing playing",
        };
        _playerTime = new Label
        {
            Location = new Point(452, 8),
            AutoSize = true,
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
        };
        _seekBar = new SeekBar
        {
            Location = new Point(12, 32),
            Width = ContentWidth - 190,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
        };
        _seekBar.Seeked += frac =>
        {
            if (_player.Duration > TimeSpan.Zero)
                _player.Position = TimeSpan.FromSeconds(_player.Duration.TotalSeconds * frac);
        };
        _playerToggle = MakeMiniButton("► play", new Point(ContentWidth - 160, 26));
        _playerToggle.Width = 72;
        _playerToggle.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _playerToggle.Click += (_, _) =>
        {
            if (_player.Current != null) _player.Toggle(_player.Current);
        };
        var next = MakeMiniButton("next ▸", new Point(ContentWidth - 80, 26));
        next.Width = 68;
        next.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        next.Click += (_, _) => PlayNext();
        bar.Controls.Add(_playerTrack);
        bar.Controls.Add(_playerTime);
        bar.Controls.Add(_seekBar);
        bar.Controls.Add(_playerToggle);
        bar.Controls.Add(next);

        _outputList = new Panel
        {
            Location = new Point(0, 40),
            Size = new Size(ContentWidth + 22, 300),
            BackColor = Theme.Back,
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
        };
        p.Controls.Add(_outputList);
        p.Controls.Add(bar);
        p.Resize += (_, _) => _outputList.Height = Math.Max(60, p.Height - _outputList.Top - bar.Height - 8);
        return p;
    }

    private string OutputPath(string file)
        => Path.Combine(_supervisor.BackendDir ?? "", "output", file);

    private void PlayNext()
    {
        if (_state.Outputs.Count == 0 || _supervisor.BackendDir == null) return;
        int idx = _player.Current == null
            ? -1
            : _state.Outputs.FindIndex(o => OutputPath(o.File) == _player.Current);
        var nextFile = _state.Outputs[(idx + 1) % _state.Outputs.Count];
        _player.Play(OutputPath(nextFile.File));
    }

    private void UpdatePlayerBar()
    {
        if (_playerTrack == null) return;
        if (_player.Current == null)
        {
            _playerTrack.Text = "nothing playing";
            _playerTime.Text = "";
            _seekBar.Fraction = 0;
            _playerToggle.Text = "► play";
            return;
        }
        _playerTrack.Text = Path.GetFileName(_player.Current);
        var pos = _player.Position;
        var dur = _player.Duration;
        _playerTime.Text = $"{FmtTime(pos.TotalSeconds)} / {FmtTime(dur.TotalSeconds)}";
        _seekBar.Fraction = dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0;
        _playerToggle.Text = _player.IsPlaying ? "❚❚ pause" : "► play";
    }

    private void RenderOutputs()
    {
        _outputList.SuspendLayout();
        _outputList.Controls.Clear();
        int y = 0;
        foreach (var o in _state.Outputs)
        {
            var output = o;
            var row = new Panel { Size = new Size(ContentWidth, 52), Location = new Point(0, y), BackColor = Theme.Card2 };
            row.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Theme.Back);
                using (var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10))
                {
                    using var bg = new SolidBrush(Theme.Card2);
                    using var pen = new Pen(Theme.Border);
                    g.FillPath(bg, path);
                    g.DrawPath(pen, path);
                }

                using var nameFont = new Font("Segoe UI Semibold", 9.25f);
                using var textBrush = new SolidBrush(Theme.Text);
                g.DrawString(output.File, nameFont, textBrush, new RectangleF(14, 8, ContentWidth - 250, 18));
                using var subFont = new Font("Segoe UI", 8.25f);
                using var subBrush = new SolidBrush(Theme.Dim);
                var when = DateTimeOffset.FromUnixTimeSeconds((long)output.Mtime).LocalDateTime;
                g.DrawString($"{when:yyyy-MM-dd HH:mm} · {Math.Max(0.1, output.SizeKb / 1024.0):0.0} MB", subFont, subBrush, 14, 28);
            };

            if (_supervisor.BackendDir != null)
            {
                string path = OutputPath(o.File);
                var play = MakeMiniButton(PlayGlyph(path), new Point(ContentWidth - 176, 11));
                play.Click += (_, _) => _player.Toggle(path);
                _playButtons.Add((play, path));
                row.Controls.Add(play);
            }
            var del = MakeMiniButton("✕", new Point(ContentWidth - 62, 11));
            del.Width = 44;
            del.Click += async (_, _) =>
            {
                await _client.DeleteOutputAsync(output.File);
                _lastOutputsJson = "";
                await RefreshState();
            };
            row.Controls.Add(del);

            _outputList.Controls.Add(row);
            y += 60;
        }
        if (_state.Outputs.Count == 0)
        {
            _outputList.Controls.Add(new Label
            {
                Text = "Nothing generated yet.",
                ForeColor = Theme.Dim,
                Location = new Point(4, 8),
                AutoSize = true,
            });
        }
        _outputList.ResumeLayout();
    }

    #endregion

    private Panel BuildAbout()
    {
        var p = new Panel();
        p.Controls.Add(Header("About", 0));
        p.Controls.Add(Note(
            "FreeVoice Studio — every text-to-speech tier in one native app. $0, forever, and nothing leaves this machine.\n\n" +
            "TIER 3 — Chatterbox: beat ElevenLabs 65% to 24% in blind listening tests. Voice cloning, emotion control.\n" +
            "TIER 3 — F5-TTS: research-grade cloning specialist (needs a voice from the Voices tab).\n" +
            "TIER 2 — Kokoro: near-instant drafts, 50+ preset voices.\n\n" +
            "Speaking tips:\n" +
            "• Acronyms 3 letters or shorter (FBI, USA) are spelled out automatically.\n" +
            "• Longer ones: write them with dots — N.A.S.A. — to force spelling; otherwise they're read as words.\n" +
            "• First use of an engine loads a big model (1–2 min). After that, speed is per-word.\n" +
            "• Same seed + same script = the same take. Seed 0 = different every time.\n\n" +
            "Engine server: http://127.0.0.1:7899 — POST /api/generate for automation.\n" +
            "github.com/The-Berin/FreeVoice",
            36, 700));
        return p;
    }

    #region state polling

    private async Task RefreshState()
    {
        var s = await _client.GetStateAsync();
        if (s == null)
        {
            if (_connected)
                SetStatus("engine offline — restarting…");
            _connected = false;
            // self-heal: periodically try to bring the engine back up
            if (++_offlineTicks % 8 == 0 && !_reviving)
            {
                _reviving = true;
                try { await _supervisor.EnsureRunningAsync(_client); }
                finally { _reviving = false; }
            }
            return;
        }
        bool first = !_connected;
        _connected = true;
        _offlineTicks = 0;
        _state = s;

        foreach (var j in s.Jobs)
        {
            _knownJobStates.TryGetValue(j.Id, out var prev);
            if (prev is "running" or "queued" && j.State is "done" or "error")
            {
                string title = string.IsNullOrEmpty(j.Title) ? $"{j.Words} {(j.Words == 1 ? "word" : "words")}" : j.Title;
                _tray?.ShowBalloonTip(4000,
                    j.State == "done" ? "FreeVoice — narration ready" : "FreeVoice — job failed",
                    j.State == "done" ? $"\"{title}\" — {j.StatusText}" : $"\"{title}\": {j.StatusText}",
                    j.State == "done" ? ToolTipIcon.Info : ToolTipIcon.Warning);
                _lastOutputsJson = ""; // library changed
            }
            _knownJobStates[j.Id] = j.State;
        }

        if (first)
        {
            SetStatus("");
            RenderEngines();
            _effect.Items.Clear();
            _effect.Items.AddRange(_state.Effects.Cast<object>().ToArray());
            _effect.SelectedIndex = 0;
            _kokoroVoice.Items.Clear();
            _kokoroVoice.Items.AddRange(_state.KokoroPresets.Cast<object>().ToArray());
            int mi = _state.KokoroPresets.IndexOf("am_michael");
            _kokoroVoice.SelectedIndex = Math.Max(0, mi);
        }
        RefreshLists();
    }

    private string PlayGlyph(string path)
        => _player.Current == path && _player.IsPlaying ? "❚❚" : "► play";

    private void UpdatePlayButtons()
    {
        _playButtons.RemoveAll(pb => pb.Btn.IsDisposed);
        foreach (var (btn, path) in _playButtons)
            btn.Text = PlayGlyph(path);
    }

    private void RefreshLists()
    {
        RenderJobs();

        string voicesJson = System.Text.Json.JsonSerializer.Serialize(_state.Voices);
        if (voicesJson != _lastVoicesJson)
        {
            _lastVoicesJson = voicesJson;
            RenderVoices();
            var cur = _voice.SelectedItem as string;
            _voice.Items.Clear();
            _voice.Items.Add("Default narrator");
            foreach (var v in _state.Voices) _voice.Items.Add(v.Name);
            int idx = cur != null ? _voice.Items.IndexOf(cur) : -1;
            _voice.SelectedIndex = idx >= 0 ? idx : 0;
        }

        string outputsJson = System.Text.Json.JsonSerializer.Serialize(_state.Outputs);
        if (outputsJson != _lastOutputsJson)
        {
            _lastOutputsJson = outputsJson;
            RenderOutputs();
        }
    }

    #endregion
}
