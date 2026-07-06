using System.Diagnostics;

namespace FreeVoiceStudio;

public sealed class MainForm : Form
{
    private readonly ServerClient _client = new();
    private readonly ServerSupervisor _supervisor = new();
    private readonly AudioPlayer _player = new();
    private StateDto _state = new();
    private string _lastJobsJson = "", _lastVoicesJson = "", _lastOutputsJson = "";
    private bool _connected;

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
    private GradientButton _generate = null!;
    private Panel _jobsPanel = null!;

    // Voices controls
    private TextBox _voiceName = null!, _voiceTranscript = null!;
    private Label _voiceFileLabel = null!;
    private string? _voiceFile;
    private Panel _voiceList = null!;

    // Library
    private Panel _outputList = null!;

    public MainForm()
    {
        Text = "FreeVoice Studio";
        Size = new Size(1120, 780);
        MinimumSize = new Size(980, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Back;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { }

        BuildSidebar();
        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(30, 24, 30, 24);
        Controls.Add(_content);
        Controls.Add(_sidebar);

        AddPage("Studio", BuildStudio());
        AddPage("Voices", BuildVoices());
        AddPage("Library", BuildLibrary());
        AddPage("About", BuildAbout());

        Theme.Apply(this);
        ShowPage("Studio");
        Theme.DarkTitleBar(this);

        var poll = new System.Windows.Forms.Timer { Interval = 1200 };
        poll.Tick += async (_, _) => await RefreshState();
        poll.Start();

        _player.PlaybackChanged += () => { if (!IsDisposed) BeginInvoke(RefreshLists); };

        Shown += async (_, _) =>
        {
            SetStatus("starting engine server…");
            bool ok = await _supervisor.EnsureRunningAsync(_client);
            SetStatus(ok ? "" : _supervisor.LastError ?? "engine offline");
            if (!ok)
                MessageBox.Show(_supervisor.LastError, "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshState();
        };
        FormClosed += (_, _) => { _player.Dispose(); _supervisor.Dispose(); };
    }

    #region layout scaffolding

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 210;
        _sidebar.BackColor = Theme.Panel;

        var logo = new PictureBox
        {
            Size = new Size(42, 42),
            Location = new Point(20, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        try { logo.Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "logo64.png")); } catch { }
        _sidebar.Controls.Add(logo);
        _sidebar.Controls.Add(new Label
        {
            Text = "FreeVoice",
            Font = new Font("Segoe UI Semibold", 14.5f),
            ForeColor = Theme.Text,
            Location = new Point(70, 22),
            AutoSize = true,
        });
        _sidebar.Controls.Add(new Label
        {
            Text = "local tts studio",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.Sub,
            Location = new Point(72, 47),
            AutoSize = true,
        });

        _sideStatus = new Label
        {
            Location = new Point(20, 0),
            Size = new Size(174, 90),
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Text = "all local · free forever",
        };
        _sidebar.Controls.Add(_sideStatus);
        _sidebar.Resize += (_, _) => _sideStatus.Top = _sidebar.Height - 100;
    }

    private void AddPage(string name, Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        page.AutoScroll = true;
        _pages[name] = page;
        _content.Controls.Add(page);

        var b = new Button
        {
            Text = "    " + name,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(186, 38),
            Location = new Point(12, 96 + _nav.Count * 42),
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
        Font = new Font("Segoe UI Semibold", 15f),
        ForeColor = Theme.Text,
        Location = new Point(0, y),
        AutoSize = true,
    };

    private static Label Note(string text, int y, int w = 700) => new()
    {
        Text = text,
        ForeColor = Theme.Sub,
        Location = new Point(0, y),
        MaximumSize = new Size(w, 0),
        AutoSize = true,
    };

    private static Label SmallLabel(string text, int x, int y) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
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
        p.Controls.Add(Header("Studio", y)); y += 30;
        p.Controls.Add(Note("Write it. Pick a voice. Walk away. Switch speakers with [VoiceName] at the start of a line.", y)); y += 32;

        p.Controls.Add(SmallLabel("Script", 0, y)); y += 20;
        _script = new TextBox
        {
            Multiline = true,
            Location = new Point(0, y),
            Size = new Size(800, 170),
            Font = new Font("Segoe UI", 10.5f),
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        _script.TextChanged += (_, _) => UpdateWordInfo();
        p.Controls.Add(_script); y += 180;

        _wordInfo = new Label { Location = new Point(2, y), AutoSize = true, ForeColor = Theme.Dim, Font = new Font("Segoe UI", 8.75f) };
        p.Controls.Add(_wordInfo);
        _title = new TextBox { Location = new Point(560, y - 3), Width = 240, Font = new Font("Segoe UI", 9f), PlaceholderText = "output name (optional)" };
        p.Controls.Add(_title); y += 34;

        p.Controls.Add(SmallLabel("Engine", 0, y)); y += 20;
        _engineRow = new FlowLayoutPanel { Location = new Point(0, y), Size = new Size(810, 126), BackColor = Theme.Back };
        p.Controls.Add(_engineRow); y += 132;

        p.Controls.Add(SmallLabel("Voice", 0, y));
        p.Controls.Add(SmallLabel("Effect", 300, y));
        _kokoroLabel = SmallLabel("Kokoro preset", 150, y);
        p.Controls.Add(_kokoroLabel);
        p.Controls.Add(SmallLabel("Format", 470, y));
        p.Controls.Add(SmallLabel("Clean audio", 620, y));
        y += 20;

        _voice = new ComboBox { Location = new Point(0, y), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        _kokoroVoice = new ComboBox { Location = new Point(150, y), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        _effect = new ComboBox { Location = new Point(300, y), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _format = new Segmented(new[] { "MP3", "WAV" }) { Location = new Point(470, y) };
        _clean = new FvToggle("denoise + normalize", true) { Location = new Point(620, y + 2), Width = 190 };
        p.Controls.Add(_voice);
        p.Controls.Add(_kokoroVoice);
        p.Controls.Add(_effect);
        p.Controls.Add(_format);
        p.Controls.Add(_clean);
        y += 44;

        p.Controls.Add(SmallLabel("Delivery", 0, y)); y += 20;
        _exaggeration = new FvSlider("Emotion (Chatterbox)", 0.25, 1.0, 0.5, 0.05) { Location = new Point(0, y), Width = 385 };
        _cfg = new FvSlider("Pace — lower = slower read", 0.2, 0.8, 0.5, 0.05) { Location = new Point(415, y), Width = 385 };
        y += 50;
        _speed = new FvSlider("Speed (Kokoro / F5)", 0.7, 1.4, 1.0, 0.05) { Location = new Point(0, y), Width = 385 };
        _nfe = new FvSlider("Quality steps (F5)", 16, 64, 32, 4, v => ((int)v).ToString()) { Location = new Point(415, y), Width = 385 };
        p.Controls.Add(_exaggeration);
        p.Controls.Add(_cfg);
        p.Controls.Add(_speed);
        p.Controls.Add(_nfe);
        y += 58;

        _generate = new GradientButton { Text = "Generate", Location = new Point(0, y), Size = new Size(800, 46) };
        _generate.Click += async (_, _) => await OnGenerate();
        p.Controls.Add(_generate); y += 58;

        _jobsPanel = new Panel { Location = new Point(0, y), Size = new Size(810, 400), BackColor = Theme.Back, AutoSize = true };
        p.Controls.Add(_jobsPanel);

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
            eta = s < 90 ? $" · ≈{Math.Ceiling(s):0}s render" : $" · ≈{s / 60:0.0} min render";
        }
        _wordInfo.Text = $"{words} words{eta}";
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
            },
            effect = _effect.SelectedItem as string ?? "None",
            clean = _clean.Checked,
            format = _format.Value.ToLowerInvariant(),
        };
        var (ok, msg) = await _client.GenerateAsync(payload);
        if (!ok)
            MessageBox.Show(msg, "FreeVoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        await RefreshState();
    }

    private void RenderJobs()
    {
        _jobsPanel.SuspendLayout();
        _jobsPanel.Controls.Clear();
        int y = 0;
        foreach (var j in _state.Jobs)
        {
            var row = BuildJobRow(j);
            row.Location = new Point(0, y);
            _jobsPanel.Controls.Add(row);
            y += row.Height + 10;
        }
        _jobsPanel.Height = Math.Max(10, y);
        _jobsPanel.ResumeLayout();
    }

    private Panel BuildJobRow(JobDto j)
    {
        bool running = j.State == "running";
        var row = new Panel { Size = new Size(800, running ? 74 : 62), BackColor = Theme.Card2 };
        row.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Border);
            using var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10);
            e.Graphics.Clear(Theme.Back);
            using var bg = new SolidBrush(Theme.Card2);
            e.Graphics.FillPath(bg, path);
            e.Graphics.DrawPath(pen, path);

            var stateColor = j.State switch
            {
                "done" => Theme.Ok,
                "running" => Theme.A2,
                "queued" => Theme.Sub,
                _ => Theme.Err,
            };
            using var badgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var badgeBrush = new SolidBrush(stateColor);
            e.Graphics.DrawString(j.State.ToUpperInvariant(), badgeFont, badgeBrush, 14, 10);

            using var titleFont = new Font("Segoe UI Semibold", 9.75f);
            using var textBrush = new SolidBrush(Theme.Text);
            string title = string.IsNullOrEmpty(j.Title) ? $"{j.Words} words" : j.Title;
            e.Graphics.DrawString(title, titleFont, textBrush, 80, 8);

            using var subFont = new Font("Segoe UI", 8.75f);
            using var subBrush = new SolidBrush(Theme.Sub);
            string status = j.StatusText;
            if (running && j.EtaSeconds is int eta)
                status += eta < 90 ? $" · ~{eta}s left" : $" · ~{eta / 60.0:0.0} min left";
            e.Graphics.DrawString(status, subFont, subBrush, 80, 30);

            if (running && j.Total > 0)
            {
                var track = new RectangleF(14, row.Height - 14, row.Width - 28, 5);
                using var trackBg = new SolidBrush(Color.FromArgb(42, 42, 56));
                using var trackPath = Theme.Rounded(track, 2.5f);
                e.Graphics.FillPath(trackBg, trackPath);
                float t = (float)j.Done / j.Total;
                var fill = new RectangleF(track.X, track.Y, Math.Max(4, track.Width * t), track.Height);
                using var fillBrush = new System.Drawing.Drawing2D.LinearGradientBrush(fill, Theme.A1, Theme.A2, 0f);
                using var fillPath = Theme.Rounded(fill, 2.5f);
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        };

        if (j.State == "done" && j.Result != null && _supervisor.BackendDir != null)
        {
            string path = Path.Combine(_supervisor.BackendDir, "output", j.Result);
            var play = MakeMiniButton(_player.Playing == path ? "■ stop" : "► play", new Point(620, 16));
            play.Click += (_, _) => _player.Toggle(path);
            row.Controls.Add(play);
        }
        else if (j.State is "queued" or "running")
        {
            var cancel = MakeMiniButton("cancel", new Point(620, 16));
            cancel.Click += async (_, _) => { await _client.CancelJobAsync(j.Id); await RefreshState(); };
            row.Controls.Add(cancel);
        }

        var close = MakeMiniButton("✕", new Point(740, 16));
        close.Width = 40;
        close.Click += async (_, _) =>
        {
            if (j.State is "queued" or "running") await _client.CancelJobAsync(j.Id);
            else await _client.RemoveJobAsync(j.Id);
            await RefreshState();
        };
        if (j.State is not ("queued" or "running"))
            row.Controls.Add(close);

        return row;
    }

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
        p.Controls.Add(Note("Clone anyone from 7–20 seconds of clean speech (no music, no noise). Works with Chatterbox and F5-TTS.", y)); y += 36;

        var pick = new Button { Text = "Choose audio file…", Location = new Point(0, y), Size = new Size(160, 34) };
        _voiceFileLabel = new Label { Location = new Point(172, y + 7), AutoSize = true, ForeColor = Theme.Sub };
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
        p.Controls.Add(_voiceFileLabel); y += 44;

        _voiceName = new TextBox { Location = new Point(0, y), Width = 200, PlaceholderText = "voice name" };
        _voiceTranscript = new TextBox { Location = new Point(210, y), Width = 380, PlaceholderText = "what is said in the sample (optional, helps F5)" };
        var save = new GradientButton { Text = "Save voice", Location = new Point(600, y - 3), Size = new Size(130, 34) };
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
            await RefreshState();
        };
        p.Controls.Add(_voiceName);
        p.Controls.Add(_voiceTranscript);
        p.Controls.Add(save); y += 56;

        _voiceList = new Panel { Location = new Point(0, y), Size = new Size(810, 420), BackColor = Theme.Back, AutoSize = true };
        p.Controls.Add(_voiceList);
        return p;
    }

    private void RenderVoices()
    {
        _voiceList.SuspendLayout();
        _voiceList.Controls.Clear();
        int y = 0;
        foreach (var v in _state.Voices)
        {
            var row = new Panel { Size = new Size(760, 58), Location = new Point(0, y), BackColor = Theme.Card2 };
            var voice = v;
            row.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.Clear(Theme.Back);
                using var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10);
                using var bg = new SolidBrush(Theme.Card2);
                using var pen = new Pen(Theme.Border);
                e.Graphics.FillPath(bg, path);
                e.Graphics.DrawPath(pen, path);

                var av = new RectangleF(12, 11, 36, 36);
                using var avBrush = new System.Drawing.Drawing2D.LinearGradientBrush(av, Theme.A1, Theme.A2, 45f);
                e.Graphics.FillEllipse(avBrush, av);
                using var avFont = new Font("Segoe UI Semibold", 13f);
                e.Graphics.DrawString(voice.Name[..1].ToUpperInvariant(), avFont, Brushes.White, av.X + 8, av.Y + 5);

                using var nameFont = new Font("Segoe UI Semibold", 10.5f);
                using var textBrush = new SolidBrush(Theme.Text);
                e.Graphics.DrawString(voice.Name, nameFont, textBrush, 60, 9);
                using var subFont = new Font("Segoe UI", 8.5f);
                using var subBrush = new SolidBrush(Theme.Dim);
                e.Graphics.DrawString($"{voice.Seconds:0.#}s sample", subFont, subBrush, 60, 31);
            };

            if (_supervisor.BackendDir != null)
            {
                string path = Path.Combine(_supervisor.BackendDir, "voices", v.File);
                var play = MakeMiniButton(_player.Playing == path ? "■ stop" : "► play", new Point(560, 14));
                play.Click += (_, _) => _player.Toggle(path);
                row.Controls.Add(play);
            }
            var del = MakeMiniButton("delete", new Point(668, 14));
            del.Width = 80;
            del.Click += async (_, _) =>
            {
                if (MessageBox.Show($"Delete voice \"{voice.Name}\"?", "FreeVoice",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    await _client.DeleteVoiceAsync(voice.Name);
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
        _voiceList.Height = Math.Max(40, y);
        _voiceList.ResumeLayout();
    }

    #endregion

    #region library page

    private Panel BuildLibrary()
    {
        var p = new Panel();
        int y = 0;
        p.Controls.Add(Header("Library", y)); y += 30;
        var note = Note("Everything you've generated.", y);
        p.Controls.Add(note);
        var open = new Button { Text = "Open folder", Location = new Point(200, y - 6), Size = new Size(110, 30) };
        open.Click += async (_, _) => await _client.OpenFolderAsync();
        p.Controls.Add(open); y += 40;

        _outputList = new Panel { Location = new Point(0, y), Size = new Size(810, 480), BackColor = Theme.Back, AutoSize = true };
        p.Controls.Add(_outputList);
        return p;
    }

    private void RenderOutputs()
    {
        _outputList.SuspendLayout();
        _outputList.Controls.Clear();
        int y = 0;
        foreach (var o in _state.Outputs)
        {
            var output = o;
            var row = new Panel { Size = new Size(760, 54), Location = new Point(0, y), BackColor = Theme.Card2 };
            row.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.Clear(Theme.Back);
                using var path = Theme.Rounded(new RectangleF(0, 0, row.Width - 1, row.Height - 1), 10);
                using var bg = new SolidBrush(Theme.Card2);
                using var pen = new Pen(Theme.Border);
                e.Graphics.FillPath(bg, path);
                e.Graphics.DrawPath(pen, path);

                using var nameFont = new Font("Segoe UI Semibold", 9.5f);
                using var textBrush = new SolidBrush(Theme.Text);
                e.Graphics.DrawString(output.File, nameFont, textBrush, new RectangleF(14, 8, 520, 20));
                using var subFont = new Font("Segoe UI", 8.25f);
                using var subBrush = new SolidBrush(Theme.Dim);
                var when = DateTimeOffset.FromUnixTimeSeconds((long)output.Mtime).LocalDateTime;
                e.Graphics.DrawString($"{when:yyyy-MM-dd HH:mm} · {output.SizeKb / 1024.0:0.0} MB", subFont, subBrush, 14, 30);
            };

            if (_supervisor.BackendDir != null)
            {
                string path = Path.Combine(_supervisor.BackendDir, "output", o.File);
                var play = MakeMiniButton(_player.Playing == path ? "■ stop" : "► play", new Point(560, 12));
                play.Click += (_, _) => _player.Toggle(path);
                row.Controls.Add(play);
            }
            var del = MakeMiniButton("✕", new Point(668, 12));
            del.Width = 44;
            del.Click += async (_, _) =>
            {
                await _client.DeleteOutputAsync(output.File);
                await RefreshState();
            };
            row.Controls.Add(del);

            _outputList.Controls.Add(row);
            y += 62;
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
        _outputList.Height = Math.Max(40, y);
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
            "TIER 3 — F5-TTS: research-grade cloning specialist.\n" +
            "TIER 2 — Kokoro: near-instant drafts, 50+ preset voices.\n\n" +
            "Multi-voice scripts: start a line with [VoiceName]. Effects ported from VoiceBox (Deep Voice, Radio, Echo, Robotic).\n" +
            "Clean audio: spectral denoise + YouTube-standard loudness (-16 LUFS).\n\n" +
            "Engine server: Python, supervised by this app, http://127.0.0.1:7899 — POST /api/generate for automation.\n" +
            "github.com/The-Berin/FreeVoice",
            36, 720));
        return p;
    }

    #region state polling

    private async Task RefreshState()
    {
        var s = await _client.GetStateAsync();
        if (s == null)
        {
            if (_connected) SetStatus("engine offline…");
            _connected = false;
            return;
        }
        bool first = !_connected;
        _connected = true;
        _state = s;
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

    private void RefreshLists()
    {
        string jobsJson = System.Text.Json.JsonSerializer.Serialize(_state.Jobs);
        string voicesJson = System.Text.Json.JsonSerializer.Serialize(_state.Voices);
        string outputsJson = System.Text.Json.JsonSerializer.Serialize(_state.Outputs);
        string playKey = _player.Playing ?? "";

        if (jobsJson + playKey != _lastJobsJson)
        {
            _lastJobsJson = jobsJson + playKey;
            RenderJobs();
        }
        if (voicesJson + playKey != _lastVoicesJson)
        {
            _lastVoicesJson = voicesJson + playKey;
            RenderVoices();
            var cur = _voice.SelectedItem as string;
            _voice.Items.Clear();
            _voice.Items.Add("Default narrator");
            foreach (var v in _state.Voices) _voice.Items.Add(v.Name);
            int idx = cur != null ? _voice.Items.IndexOf(cur) : -1;
            _voice.SelectedIndex = idx >= 0 ? idx : 0;
        }
        if (outputsJson + playKey != _lastOutputsJson)
        {
            _lastOutputsJson = outputsJson + playKey;
            RenderOutputs();
        }
    }

    #endregion
}
