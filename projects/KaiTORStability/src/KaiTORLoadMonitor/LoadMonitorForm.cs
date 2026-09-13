using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KaiTORLoadMonitor
{
    internal sealed class LoadMonitorForm : Form
    {
        private readonly string _gameRoot;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer = new Timer();
        private readonly Label _phase = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _time = new Label();
        private readonly Label _eta = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _tip = new Label();
        private Process _bannerlord;
        private TimeSpan _lastCpu;
        private DateTime _lastCpuSampleUtc;
        private double? _historicalSeconds;
        private bool _moduleLoaded;
        private bool _optimizationActive;
        private bool _ready;
        private DateTime? _readyAtUtc;
        private int _maxProgress;

        private static readonly string StateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string StabilityLog = Path.Combine(StateRoot, "KaiTORStability.log");
        private static readonly string HistoryFile = Path.Combine(StateRoot, "load-history.txt");

        public LoadMonitorForm(string gameRoot)
        {
            _gameRoot = gameRoot;
            Text = "KaiTOR — загрузка The Old Realms";
            Width = 720;
            Height = 330;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            TopMost = true;
            BackColor = Color.FromArgb(24, 24, 28);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 10f);

            var title = new Label
            {
                Text = "The Old Realms — загрузка",
                Left = 28,
                Top = 22,
                Width = 640,
                Height = 35,
                Font = new Font("Segoe UI Semibold", 18f),
                ForeColor = Color.White
            };

            _phase.SetBounds(30, 70, 640, 28);
            _phase.Font = new Font("Segoe UI Semibold", 11f);
            _detail.SetBounds(30, 103, 640, 42);
            _detail.ForeColor = Color.Silver;
            _progress.SetBounds(30, 155, 640, 24);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _time.SetBounds(30, 192, 300, 25);
            _eta.SetBounds(370, 192, 300, 25);
            _eta.TextAlign = ContentAlignment.TopRight;
            _tip.SetBounds(30, 230, 640, 48);
            _tip.ForeColor = Color.DarkGray;
            _tip.Text = "Если окно Bannerlord показывает «Не отвечает», не закрывайте его автоматически: монитор отдельно проверяет, работает ли сам процесс.";

            Controls.Add(title);
            Controls.Add(_phase);
            Controls.Add(_detail);
            Controls.Add(_progress);
            Controls.Add(_time);
            Controls.Add(_eta);
            Controls.Add(_tip);

            _historicalSeconds = LoadMedianHistory();
            Shown += OnShown;
            FormClosed += (s, e) => _timer.Stop();

            _timer.Interval = 500;
            _timer.Tick += OnTick;
        }

        private void OnShown(object sender, EventArgs e)
        {
            Directory.CreateDirectory(StateRoot);
            SetPhase("Запускаем Bannerlord", "Подготовка обычного TaleWorlds launcher.", 5);
            TryLaunch();
            _timer.Start();
        }

        private void TryLaunch()
        {
            try
            {
                if (FindBannerlordProcess() != null) return;

                var launcher = Path.Combine(
                    _gameRoot,
                    "bin",
                    "Win64_Shipping_Client",
                    "TaleWorlds.MountAndBlade.Launcher.exe");

                Process.Start(new ProcessStartInfo
                {
                    FileName = launcher,
                    WorkingDirectory = Path.GetDirectoryName(launcher),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _detail.Text = "Не удалось запустить launcher: " + ex.Message;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            var elapsed = DateTime.UtcNow - _startedUtc;
            _time.Text = "Прошло: " + FormatDuration(elapsed);
            UpdateEta(elapsed);
            ReadStabilityEvents();

            if (_ready)
            {
                SetPhase(
                    "Готово",
                    _optimizationActive
                        ? "Главный экран готов. Оптимизация боя активирована."
                        : "Главный экран готов. Проверьте лог KaiTOR Stability для статуса оптимизации.",
                    100);

                if (_readyAtUtc == null)
                {
                    _readyAtUtc = DateTime.UtcNow;
                    SaveHistory(elapsed.TotalSeconds);
                }
                else if ((DateTime.UtcNow - _readyAtUtc.Value).TotalSeconds >= 4)
                {
                    Close();
                }
                return;
            }

            var process = FindBannerlordProcess();
            if (process == null)
            {
                SetPhase("Ожидаем Bannerlord", "Launcher запущен. Выберите TOR и нажмите Play.", 10);
                return;
            }

            _bannerlord = process;
            var health = DescribeProcess(process);

            if (_moduleLoaded)
            {
                SetPhase(
                    "TOR_Core загружен",
                    "Инициализируется интерфейс и стартовый экран. " + health,
                    82);
                return;
            }

            var progress = EstimatePreModuleProgress(elapsed);
            var longLoad = elapsed.TotalSeconds >= Math.Max(90, (_historicalSeconds ?? 180) * 0.55);
            SetPhase(
                longLoad ? "TOR всё ещё загружается" : "Загрузка TOR и ресурсов",
                (longLoad
                    ? "Долгая фаза может быть компиляцией шейдеров/ресурсов. Не закрывайте игру только из-за статуса «Не отвечает». "
                    : "Модули Bannerlord и The Old Realms инициализируются. ") + health,
                progress);
        }

        private void ReadStabilityEvents()
        {
            if (!File.Exists(StabilityLog)) return;

            try
            {
                var lines = File.ReadAllLines(StabilityLog);
                var start = Math.Max(0, lines.Length - 200);
                for (var i = lines.Length - 1; i >= start; i--)
                {
                    var parts = lines[i].Split(new[] { '|' }, 3);
                    DateTime timestamp;
                    if (parts.Length < 2 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                    {
                        continue;
                    }
                    if (timestamp.ToUniversalTime() < _startedUtc.AddSeconds(-5)) continue;

                    if (parts[1] == "INITIAL_SCREEN_READY") _ready = true;
                    if (parts[1] == "MODULE_LOAD") _moduleLoaded = true;
                    if (parts[1] == "OPTIMIZATION_ACTIVE") _optimizationActive = true;
                }
            }
            catch
            {
                // The game may be writing the file while we read it. Retry on the next tick.
            }
        }

        private Process FindBannerlordProcess()
        {
            try
            {
                var process = Process.GetProcessesByName("Bannerlord").FirstOrDefault();
                if (process != null && !process.HasExited) return process;

                process = Process.GetProcessesByName("Bannerlord.Native").FirstOrDefault();
                if (process != null && !process.HasExited) return process;
            }
            catch { }
            return null;
        }

        private string DescribeProcess(Process process)
        {
            try
            {
                process.Refresh();
                var ramGb = process.WorkingSet64 / 1024d / 1024d / 1024d;
                var now = DateTime.UtcNow;
                var cpu = process.TotalProcessorTime;
                double cpuPercent = 0;

                if (_lastCpuSampleUtc != default(DateTime))
                {
                    var wall = (now - _lastCpuSampleUtc).TotalMilliseconds;
                    var cpuMs = (cpu - _lastCpu).TotalMilliseconds;
                    if (wall > 0)
                    {
                        cpuPercent = cpuMs / wall / Environment.ProcessorCount * 100d;
                    }
                }

                _lastCpu = cpu;
                _lastCpuSampleUtc = now;

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "CPU ~{0:0}% · RAM {1:0.0} GB · окно {2}.",
                    Math.Max(0, cpuPercent),
                    ramGb,
                    process.Responding ? "отвечает" : "может выглядеть зависшим");
            }
            catch
            {
                return "Процесс Bannerlord активен.";
            }
        }

        private int EstimatePreModuleProgress(TimeSpan elapsed)
        {
            double basis = _historicalSeconds ?? 240d;
            var ratio = Math.Min(1d, elapsed.TotalSeconds / Math.Max(30d, basis * 0.8d));
            return 22 + (int)(ratio * 43d);
        }

        private void UpdateEta(TimeSpan elapsed)
        {
            if (_ready)
            {
                _eta.Text = "ETA: готово";
                return;
            }

            if (!_historicalSeconds.HasValue)
            {
                _eta.Text = "ETA: калибровка первого запуска";
                return;
            }

            var remaining = Math.Max(0, _historicalSeconds.Value - elapsed.TotalSeconds);
            _eta.Text = "ETA: ~" + FormatDuration(TimeSpan.FromSeconds(remaining));
        }

        private void SetPhase(string phase, string detail, int progress)
        {
            _phase.Text = phase;
            _detail.Text = detail;
            _maxProgress = Math.Max(_maxProgress, Math.Max(0, Math.Min(100, progress)));
            _progress.Value = _maxProgress;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1) return string.Format("{0:0}ч {1:00}м", Math.Floor(value.TotalHours), value.Minutes);
            if (value.TotalMinutes >= 1) return string.Format("{0:0}м {1:00}с", Math.Floor(value.TotalMinutes), value.Seconds);
            return value.Seconds + "с";
        }

        private static double? LoadMedianHistory()
        {
            try
            {
                if (!File.Exists(HistoryFile)) return null;
                var all = new List<double>();
                foreach (var line in File.ReadAllLines(HistoryFile))
                {
                    double parsed;
                    if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 1)
                    {
                        all.Add(parsed);
                    }
                }

                if (all.Count == 0) return null;
                var recent = all.Skip(Math.Max(0, all.Count - 5)).OrderBy(x => x).ToArray();
                return recent[recent.Length / 2];
            }
            catch { return null; }
        }

        private static void SaveHistory(double seconds)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                File.AppendAllText(HistoryFile, seconds.ToString("0.###", CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch { }
        }
    }
}
