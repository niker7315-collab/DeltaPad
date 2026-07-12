using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DeltaPad
{
    public class PadData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Volume { get; set; } = 80;
        public int Repeat { get; set; } = 1;
        public bool Infinite { get; set; } = false;
        public string Ext { get; set; } = ".wav";
        public string Hotkey { get; set; } = "";
    }

    internal class PlayState
    {
        public bool IsPlaying;
        public int Remaining;
        public bool ManualStop;
        public WasapiOut? Output;
        public IDisposable? Source;
    }

    /// <summary>
    /// Diagnostic wrapper: measures the peak absolute sample value passing through,
    /// so we can tell whether a decoder is producing real audio or silence.
    /// </summary>
    public class PeakTrackingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        public float Peak { get; private set; }
        public WaveFormat WaveFormat => source.WaveFormat;
        public PeakTrackingSampleProvider(ISampleProvider source) { this.source = source; }
        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                var abs = Math.Abs(buffer[offset + i]);
                if (abs > Peak) Peak = abs;
            }
            return read;
        }
    }
    public class NLayerMp3SampleProvider : ISampleProvider, IDisposable
    {
        private readonly NLayer.MpegFile mpegFile;
        public WaveFormat WaveFormat { get; }

        public NLayerMp3SampleProvider(string path)
        {
            mpegFile = new NLayer.MpegFile(path);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mpegFile.SampleRate, mpegFile.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return mpegFile.ReadSamples(buffer, offset, count);
        }

        public void Dispose() => mpegFile.Dispose();
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class PadTile : Panel
    {
        public PadData Pad;
        public event EventHandler? PlayRequested;
        public event EventHandler? SettingsRequested;
        private readonly Label nameLabel;
        private readonly Label metaLabel;

        private static readonly Color NormalBg = Color.FromArgb(0x22, 0x1E, 0x29);
        private static readonly Color PlayingBg = Color.FromArgb(0x1B, 0x30, 0x2C);
        private static readonly Color Brass = Color.FromArgb(0xD4, 0xA8, 0x57);
        private static readonly Color Teal = Color.FromArgb(0x45, 0xC9, 0xB0);

        public PadTile(PadData pad)
        {
            Pad = pad;
            Size = new Size(160, 150);
            BackColor = NormalBg;
            Margin = new Padding(8);
            Cursor = Cursors.Hand;
            BorderStyle = BorderStyle.FixedSingle;

            var hotkeyLabel = new Label
            {
                Text = pad.Hotkey, ForeColor = Color.Gray, AutoSize = true,
                Location = new Point(8, 6), Font = new Font("Consolas", 9)
            };

            var gearBtn = new Button
            {
                Text = "⚙", FlatStyle = FlatStyle.Flat, Size = new Size(26, 24),
                Location = new Point(Width - 36, 4), BackColor = NormalBg, ForeColor = Color.Gray,
                Cursor = Cursors.Hand
            };
            gearBtn.FlatAppearance.BorderSize = 0;
            gearBtn.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

            nameLabel = new Label
            {
                Text = pad.Name, ForeColor = Color.White, Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(10, 44), Size = new Size(140, 50), Cursor = Cursors.Hand
            };

            metaLabel = new Label
            {
                Text = BuildMeta(), ForeColor = Color.Gray, Font = new Font("Consolas", 8),
                Location = new Point(10, 108), Size = new Size(140, 34),
                TextAlign = ContentAlignment.BottomRight, Cursor = Cursors.Hand
            };

            Controls.Add(hotkeyLabel);
            Controls.Add(gearBtn);
            Controls.Add(nameLabel);
            Controls.Add(metaLabel);

            Click += (s, e) => PlayRequested?.Invoke(this, EventArgs.Empty);
            nameLabel.Click += (s, e) => PlayRequested?.Invoke(this, EventArgs.Empty);
            metaLabel.Click += (s, e) => PlayRequested?.Invoke(this, EventArgs.Empty);
        }

        private string BuildMeta() => $"VOL {Pad.Volume}%\nx{(Pad.Infinite ? "∞" : Pad.Repeat.ToString())}";

        public void SetPlayingState(bool playing)
        {
            BackColor = playing ? PlayingBg : NormalBg;
            foreach (Control c in Controls)
                if (c is Label lb && lb != null) { /* keep label backcolors transparent-ish */ }
        }

        public void RefreshMeta()
        {
            nameLabel.Text = Pad.Name;
            metaLabel.Text = BuildMeta();
        }
    }

    public class PadSettingsForm : Form
    {
        public string PadName;
        public int Volume;
        public int Repeat;
        public bool Infinite;
        public string? ReplacementFilePath;

        public PadSettingsForm(PadData pad)
        {
            PadName = pad.Name; Volume = pad.Volume; Repeat = pad.Repeat; Infinite = pad.Infinite;

            Text = "Настройки пада";
            ClientSize = new Size(320, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(0x1A, 0x17, 0x20);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;

            var nameLbl = new Label { Text = "Название:", Location = new Point(20, 18), AutoSize = true };
            var nameBox = new TextBox { Text = pad.Name, Location = new Point(20, 40), Width = 270 };

            var volLbl = new Label { Text = "Громкость:", Location = new Point(20, 76), AutoSize = true };
            var volumeTrack = new TrackBar
            {
                Minimum = 0, Maximum = 100, Value = pad.Volume, Location = new Point(20, 96),
                Width = 210, TickStyle = TickStyle.None
            };
            var volumeLabel = new Label { Text = $"{pad.Volume}%", Location = new Point(240, 102), AutoSize = true };
            volumeTrack.Scroll += (s, e) => volumeLabel.Text = $"{volumeTrack.Value}%";

            var repLbl = new Label { Text = "Повторов:", Location = new Point(20, 146), AutoSize = true };
            var repeatUpDown = new NumericUpDown
            {
                Minimum = 1, Maximum = 50, Value = pad.Repeat, Location = new Point(20, 168), Width = 80
            };
            var infiniteCheck = new CheckBox
            {
                Text = "∞ без остановки", Location = new Point(115, 170), AutoSize = true, Checked = pad.Infinite
            };

            var replaceBtn = new Button
            {
                Text = "Заменить звук", Location = new Point(20, 206), Width = 135,
                BackColor = Color.FromArgb(0x22, 0x1E, 0x29), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            replaceBtn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Аудио файлы (*.wav;*.mp3)|*.wav;*.mp3" };
                if (ofd.ShowDialog() == DialogResult.OK) ReplacementFilePath = ofd.FileName;
            };

            var deleteBtn = new Button
            {
                Text = "Удалить пад", Location = new Point(165, 206), Width = 125,
                BackColor = Color.FromArgb(0x22, 0x1E, 0x29), ForeColor = Color.FromArgb(0xE1, 0x57, 0x3D),
                FlatStyle = FlatStyle.Flat
            };
            deleteBtn.Click += (s, e) => { DialogResult = DialogResult.Abort; Close(); };

            var saveBtn = new Button
            {
                Text = "Сохранить", Location = new Point(20, 256), Width = 135,
                BackColor = Color.FromArgb(0xD4, 0xA8, 0x57), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat
            };
            saveBtn.Click += (s, e) =>
            {
                PadName = string.IsNullOrWhiteSpace(nameBox.Text) ? "Без названия" : nameBox.Text.Trim();
                Volume = volumeTrack.Value;
                Repeat = (int)repeatUpDown.Value;
                Infinite = infiniteCheck.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancelBtn = new Button
            {
                Text = "Отмена", Location = new Point(165, 256), Width = 125,
                BackColor = Color.FromArgb(0x22, 0x1E, 0x29), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                nameLbl, nameBox, volLbl, volumeTrack, volumeLabel,
                repLbl, repeatUpDown, infiniteCheck, replaceBtn, deleteBtn, saveBtn, cancelBtn
            });
        }
    }

    public class MainForm : Form
    {
        private readonly string dataDir;
        private readonly string audioDir;
        private readonly string metaPath;

        private List<PadData> pads = new();
        private readonly Dictionary<string, PlayState> playStates = new();
        private readonly MMDeviceEnumerator deviceEnumerator = new();
        private List<MMDevice> outputDevices = new();
        private List<MMDevice> inputDevices = new();

        private WasapiCapture? capture;
        private WaveFileWriter? captureWriter;
        private string? recordingPadId;
        private DateTime recordingStart;
        private int recordCounter = 1;

        private FlowLayoutPanel padsPanel = new();
        private ComboBox outputCombo = new();
        private ComboBox micCombo = new();
        private Button recordBtn = new();
        private Label recTimerLabel = new();
        private readonly System.Windows.Forms.Timer recUiTimer = new();

        private static readonly string[] Hotkeys =
            "1234567890QWERTYUIASDFGHJKZXCVBNM".ToCharArray().Select(c => c.ToString()).ToArray();

        public MainForm()
        {
            dataDir = Path.Combine(AppContext.BaseDirectory, "deltapad-data");
            audioDir = Path.Combine(dataDir, "audio");
            metaPath = Path.Combine(dataDir, "pads.json");
            Directory.CreateDirectory(audioDir);

            BuildUI();
            PopulateDevices();
            LoadMeta();
            foreach (var p in pads) playStates[p.Id] = new PlayState();
            RefreshPadsUI();
        }

        private void BuildUI()
        {
            Text = "DeltaPad";
            BackColor = Color.FromArgb(0x10, 0x0E, 0x14);
            ForeColor = Color.White;
            ClientSize = new Size(900, 640);
            KeyPreview = true;
            Font = new Font("Segoe UI", 9);

            var header = new Label
            {
                Text = "DELTAPAD", Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(0xD4, 0xA8, 0x57), AutoSize = true, Location = new Point(20, 15)
            };
            Controls.Add(header);

            var addPadBtn = new Button
            {
                Text = "+ Добавить пад", Location = new Point(720, 24), Size = new Size(150, 32),
                BackColor = Color.FromArgb(0x1A, 0x17, 0x20), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            addPadBtn.Click += (s, e) => PromptAddPadFromFile();
            Controls.Add(addPadBtn);

            padsPanel = new FlowLayoutPanel
            {
                Location = new Point(20, 65), Size = new Size(860, 400),
                AutoScroll = true, BackColor = BackColor
            };
            Controls.Add(padsPanel);

            var micLabel = new Label { Text = "🎙 Микрофон:", Location = new Point(20, 485), AutoSize = true, ForeColor = Color.Gray };
            Controls.Add(micLabel);
            micCombo = new ComboBox { Location = new Point(135, 481), Size = new Size(260, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(micCombo);

            recordBtn = new Button
            {
                Text = "● Записать", Location = new Point(405, 479), Size = new Size(120, 30),
                BackColor = Color.FromArgb(0x22, 0x1E, 0x29), ForeColor = Color.FromArgb(0xE1, 0x57, 0x3D),
                FlatStyle = FlatStyle.Flat
            };
            recordBtn.Click += RecordBtn_Click;
            Controls.Add(recordBtn);

            recTimerLabel = new Label
            {
                Text = "00:00", Location = new Point(535, 485), AutoSize = true,
                ForeColor = Color.Gray, Font = new Font("Consolas", 10)
            };
            Controls.Add(recTimerLabel);

            var outLabel = new Label { Text = "🔊 Вывод:", Location = new Point(20, 525), AutoSize = true, ForeColor = Color.Gray };
            Controls.Add(outLabel);
            outputCombo = new ComboBox { Location = new Point(135, 521), Size = new Size(260, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(outputCombo);

            var testToneBtn = new Button
            {
                Text = "🔔 Тестовый тон", Location = new Point(405, 519), Size = new Size(140, 30),
                BackColor = Color.FromArgb(0x22, 0x1E, 0x29), ForeColor = Color.FromArgb(0x45, 0xC9, 0xB0),
                FlatStyle = FlatStyle.Flat
            };
            testToneBtn.Click += TestToneBtn_Click;
            Controls.Add(testToneBtn);

            var footer = new Label
            {
                Text = "Клик — играть · шестерёнка — настройки · клавиши 1-9,0,Q...M — быстрый доступ",
                Location = new Point(20, 565), AutoSize = true,
                ForeColor = Color.FromArgb(0x5F, 0x5A, 0x67), Font = new Font("Consolas", 8)
            };
            Controls.Add(footer);

            recUiTimer.Interval = 1000;
            recUiTimer.Tick += (s, e) =>
            {
                if (recordingPadId != null)
                {
                    var elapsed = DateTime.Now - recordingStart;
                    recTimerLabel.Text = elapsed.ToString(@"mm\:ss");
                }
            };

            KeyDown += MainForm_KeyDown;
        }

        private void PopulateDevices()
        {
            outputDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            outputCombo.Items.Clear();
            foreach (var d in outputDevices) outputCombo.Items.Add(d.FriendlyName);

            // Select the OS-level default playback device, not just the first in the list —
            // otherwise sound can silently go to an inactive/disconnected device.
            int defaultIndex = 0;
            try
            {
                var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var idx = outputDevices.FindIndex(d => d.ID == defaultDevice.ID);
                if (idx >= 0) defaultIndex = idx;
            }
            catch { /* fall back to index 0 */ }
            if (outputCombo.Items.Count > 0) outputCombo.SelectedIndex = defaultIndex;

            inputDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            micCombo.Items.Clear();
            foreach (var d in inputDevices) micCombo.Items.Add(d.FriendlyName);

            int defaultMicIndex = 0;
            try
            {
                var defaultMic = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                var idx = inputDevices.FindIndex(d => d.ID == defaultMic.ID);
                if (idx >= 0) defaultMicIndex = idx;
            }
            catch { /* fall back to index 0 */ }
            if (micCombo.Items.Count > 0) micCombo.SelectedIndex = defaultMicIndex;
        }

        private MMDevice GetSelectedOutputDevice()
        {
            if (outputCombo.SelectedIndex >= 0 && outputCombo.SelectedIndex < outputDevices.Count)
                return outputDevices[outputCombo.SelectedIndex];
            return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        private void LoadMeta()
        {
            pads.Clear();
            if (File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    var loaded = JsonSerializer.Deserialize<List<PadData>>(json);
                    if (loaded != null) pads = loaded;
                }
                catch { /* ignore corrupt file, start fresh */ }
            }
            AssignHotkeys();
        }

        private void SaveMeta()
        {
            try
            {
                var json = JsonSerializer.Serialize(pads);
                File.WriteAllText(metaPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка сохранения: " + ex.Message);
            }
        }

        private void AssignHotkeys()
        {
            for (int i = 0; i < pads.Count; i++) pads[i].Hotkey = i < Hotkeys.Length ? Hotkeys[i] : "";
        }

        private void RefreshPadsUI()
        {
            AssignHotkeys();
            padsPanel.Controls.Clear();
            foreach (var pad in pads)
            {
                var tile = new PadTile(pad);
                if (playStates.TryGetValue(pad.Id, out var st) && st.IsPlaying) tile.SetPlayingState(true);
                tile.PlayRequested += (s, e) => TriggerPad(pad.Id);
                tile.SettingsRequested += (s, e) => OpenSettings(pad.Id);
                padsPanel.Controls.Add(tile);
            }

            var addTile = new Panel
            {
                Size = new Size(160, 150), BackColor = Color.FromArgb(0x1A, 0x17, 0x20),
                Margin = new Padding(8), Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle
            };
            var plusLabel = new Label
            {
                Text = "+", Font = new Font("Segoe UI", 26), ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill
            };
            addTile.Controls.Add(plusLabel);
            addTile.Click += (s, e) => PromptAddPadFromFile();
            plusLabel.Click += (s, e) => PromptAddPadFromFile();
            padsPanel.Controls.Add(addTile);
        }

        private void PromptAddPadFromFile()
        {
            using var ofd = new OpenFileDialog { Filter = "Аудио файлы (*.wav;*.mp3)|*.wav;*.mp3" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            var id = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
            var destPath = Path.Combine(audioDir, id + ext);
            try { File.Copy(ofd.FileName, destPath); }
            catch (Exception ex) { MessageBox.Show("Не удалось скопировать файл: " + ex.Message); return; }

            var pad = new PadData
            {
                Id = id, Name = Path.GetFileNameWithoutExtension(ofd.FileName),
                Volume = 80, Repeat = 1, Infinite = false, Ext = ext
            };
            pads.Add(pad);
            playStates[id] = new PlayState();
            SaveMeta();
            RefreshPadsUI();
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(dataDir, "debug.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }

        private void DumpMp3ForDebug(string mp3Path)
        {
            try
            {
                var dumpPath = Path.Combine(dataDir, "debug_dump.wav");
                using var mpeg = new NLayer.MpegFile(mp3Path);
                var wf = WaveFormat.CreateIeeeFloatWaveFormat(mpeg.SampleRate, mpeg.Channels);
                using var writer = new WaveFileWriter(dumpPath, wf);
                var buffer = new float[mpeg.SampleRate * mpeg.Channels * 5]; // ~5 seconds
                int read = mpeg.ReadSamples(buffer, 0, buffer.Length);
                writer.WriteSamples(buffer, 0, read);
                Log($"DumpMp3ForDebug: wrote {read} samples (~{(double)read / (mpeg.SampleRate * mpeg.Channels):F2}s) to {dumpPath}");
            }
            catch (Exception ex)
            {
                Log($"DumpMp3ForDebug FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private (ISampleProvider sampleProvider, IDisposable source) OpenSampleSource(PadData pad)
        {
            var path = Path.Combine(audioDir, pad.Id + pad.Ext);
            Log($"OpenSampleSource: '{pad.Name}' ext={pad.Ext} path={path} exists={File.Exists(path)} size={(File.Exists(path) ? new FileInfo(path).Length : -1)}");

            if (pad.Ext == ".mp3")
            {
                DumpMp3ForDebug(path);
                var mp3 = new NLayerMp3SampleProvider(path);
                Log($"OpenSampleSource OK (NLayer mp3): format={mp3.WaveFormat}");
                return (mp3, mp3);
            }
            else
            {
                var waveReader = new WaveFileReader(path);
                Log($"OpenSampleSource OK (wav): format={waveReader.WaveFormat} totalTime={waveReader.TotalTime}");
                return (waveReader.ToSampleProvider(), waveReader);
            }
        }

        private void TriggerPad(string id)
        {
            var pad = pads.FirstOrDefault(p => p.Id == id);
            if (pad == null) return;
            if (!playStates.TryGetValue(id, out var state))
            {
                state = new PlayState();
                playStates[id] = state;
            }

            if (state.IsPlaying)
            {
                state.ManualStop = true;
                try { state.Output?.Stop(); } catch { /* ignore */ }
                return;
            }

            var audioPath = Path.Combine(audioDir, pad.Id + pad.Ext);
            if (!File.Exists(audioPath)) { MessageBox.Show("Файл звука не найден для \"" + pad.Name + "\""); return; }

            state.IsPlaying = true;
            state.Remaining = pad.Infinite ? int.MaxValue : pad.Repeat;
            state.ManualStop = false;
            RefreshPadsUI();

            PlayOnce(pad, state);
        }

        private void PlayOnce(PadData pad, PlayState state)
        {
            if (!state.IsPlaying || state.Remaining <= 0)
            {
                state.IsPlaying = false;
                RefreshPadsUI();
                return;
            }

            ISampleProvider sampleProvider;
            IDisposable source;
            try { (sampleProvider, source) = OpenSampleSource(pad); }
            catch (Exception ex)
            {
                Log($"OpenSampleSource FAILED: '{pad.Name}' {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show($"Не удалось открыть звук \"{pad.Name}\": {ex.Message}");
                state.IsPlaying = false; RefreshPadsUI();
                return;
            }

            var peakTracker = new PeakTrackingSampleProvider(sampleProvider);
            var volumeProvider = new VolumeSampleProvider(peakTracker) { Volume = pad.Volume / 100f };
            var waveProvider = volumeProvider.ToWaveProvider();

            var device = GetSelectedOutputDevice();
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            state.Source = source;
            state.Output = output;
            if (!pad.Infinite) state.Remaining -= 1;

            output.PlaybackStopped += (s, e) =>
            {
                Log($"PlaybackStopped: '{pad.Name}' peak={peakTracker.Peak:F5} exception={(e.Exception == null ? "none" : e.Exception.GetType().Name + ": " + e.Exception.Message)}");
                source.Dispose();
                output.Dispose();
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (e.Exception != null)
                        {
                            MessageBox.Show(
                                "Ошибка воспроизведения (\"" + pad.Name + "\"):\n\n" + e.Exception.GetType().Name + "\n" + e.Exception.Message,
                                "DeltaPad — ошибка звука");
                            state.IsPlaying = false;
                            RefreshPadsUI();
                            return;
                        }
                        if (state.ManualStop)
                        {
                            state.IsPlaying = false;
                            RefreshPadsUI();
                            return;
                        }
                        if (state.IsPlaying && (pad.Infinite || state.Remaining > 0))
                            PlayOnce(pad, state);
                        else
                        {
                            state.IsPlaying = false;
                            RefreshPadsUI();
                        }
                    }));
                }
            };

            try { output.Init(waveProvider); output.Play(); Log($"output.Play() called OK for '{pad.Name}'"); }
            catch (Exception ex)
            {
                Log($"output.Init/Play FAILED: '{pad.Name}' {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show($"Ошибка воспроизведения \"{pad.Name}\": {ex.Message}");
                state.IsPlaying = false; RefreshPadsUI();
            }
        }

        private void OpenSettings(string id)
        {
            var pad = pads.FirstOrDefault(p => p.Id == id);
            if (pad == null) return;
            using var dlg = new PadSettingsForm(pad);
            var result = dlg.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                if (dlg.ReplacementFilePath != null)
                {
                    var oldPath = Path.Combine(audioDir, pad.Id + pad.Ext);
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                    var newExt = Path.GetExtension(dlg.ReplacementFilePath).ToLowerInvariant();
                    var newPath = Path.Combine(audioDir, pad.Id + newExt);
                    File.Copy(dlg.ReplacementFilePath, newPath, true);
                    pad.Ext = newExt;
                }
                pad.Name = dlg.PadName;
                pad.Volume = dlg.Volume;
                pad.Repeat = dlg.Repeat;
                pad.Infinite = dlg.Infinite;
                SaveMeta();
                RefreshPadsUI();
            }
            else if (result == DialogResult.Abort)
            {
                var path = Path.Combine(audioDir, pad.Id + pad.Ext);
                if (File.Exists(path)) File.Delete(path);
                pads.Remove(pad);
                playStates.Remove(id);
                SaveMeta();
                RefreshPadsUI();
            }
        }

        private void RecordBtn_Click(object? sender, EventArgs e)
        {
            if (recordingPadId != null)
            {
                StopRecording();
                return;
            }
            if (micCombo.SelectedIndex < 0 || micCombo.SelectedIndex >= inputDevices.Count)
            {
                MessageBox.Show("Выбери микрофон из списка");
                return;
            }
            var device = inputDevices[micCombo.SelectedIndex];
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(audioDir, id + ".wav");

            try
            {
                capture = new WasapiCapture(device);
                captureWriter = new WaveFileWriter(path, capture.WaveFormat);
                capture.DataAvailable += (s, a) => captureWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                capture.RecordingStopped += (s, a) =>
                {
                    captureWriter?.Dispose();
                    captureWriter = null;
                    capture?.Dispose();
                    capture = null;

                    if (IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            var pad = new PadData
                            {
                                Id = id, Name = "Запись " + recordCounter++,
                                Volume = 80, Repeat = 1, Infinite = false, Ext = ".wav"
                            };
                            pads.Add(pad);
                            playStates[id] = new PlayState();
                            SaveMeta();
                            RefreshPadsUI();
                        }));
                    }
                };

                recordingPadId = id;
                recordingStart = DateTime.Now;
                recUiTimer.Start();
                recordBtn.Text = "■ Стоп";
                capture.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось начать запись: " + ex.Message);
                recordingPadId = null;
            }
        }

        private void StopRecording()
        {
            recUiTimer.Stop();
            recTimerLabel.Text = "00:00";
            recordBtn.Text = "● Записать";
            recordingPadId = null;
            try { capture?.StopRecording(); } catch { /* ignore */ }
        }

        private void TestToneBtn_Click(object? sender, EventArgs e)
        {
            try
            {
                var device = GetSelectedOutputDevice();
                var signal = new SignalGenerator(44100, 1) { Type = SignalGeneratorType.Sin, Frequency = 440, Gain = 0.4 };
                var waveProvider = signal.Take(TimeSpan.FromSeconds(1)).ToWaveProvider();
                var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                output.PlaybackStopped += (s, ev) =>
                {
                    output.Dispose();
                    if (ev.Exception != null && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                            MessageBox.Show("Тестовый тон — ошибка:\n\n" + ev.Exception.GetType().Name + "\n" + ev.Exception.Message,
                                "DeltaPad — ошибка звука")));
                    }
                };
                output.Init(waveProvider);
                output.Play();
                MessageBox.Show("Тон должен проиграться прямо сейчас (1 секунда, 440 Гц) через устройство:\n" + device.FriendlyName,
                    "DeltaPad — тест");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запустить тестовый тон:\n\n" + ex.GetType().Name + "\n" + ex.Message, "DeltaPad — ошибка");
            }
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            var keyStr = e.KeyCode.ToString();
            if (keyStr.Length == 2 && keyStr[0] == 'D' && char.IsDigit(keyStr[1])) keyStr = keyStr[1].ToString();
            var pad = pads.FirstOrDefault(p => p.Hotkey == keyStr);
            if (pad != null) TriggerPad(pad.Id);
        }
    }
}
