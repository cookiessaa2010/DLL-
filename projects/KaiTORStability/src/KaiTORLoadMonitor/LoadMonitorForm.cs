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
        private sealed class ShaderSample
        {
            public DateTime Utc { get; set; }
            public int Remaining { get; set; }
        }

        private readonly string _gameRoot;
        private readonly ShaderCacheInfo _shaderCache;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer = new Timer();
        private readonly Label _phase = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _time = new Label();
        private readonly Label _eta = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _tip = new Label();
        private readonly List<ShaderSample> _shaderSamples = new List<ShaderSample>();
        private TimeSpan _lastCpu;
        private DateTime _lastCpuSampleUtc;
        private double? _historicalSeconds;
        private bool _moduleLoaded;
        private bool _optimizationActive;
        private bool _ready;
        private DateTime? _readyAtUtc;
        private int _maxProgress;
        private long _stabilityLogOffset;
        private int? _shaderRemaining;
        private int _shaderWavePeak;
        private int? _boostedProcessId;
        private ProcessPriorityClass? _originalPriority;
        private bool _startupPriorityBoostActive;

        private static readonly string StateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string StabilityLog = Path.Combine(StateRoot, "KaiTORStability.log");
        private static readonly string HistoryFile = Path.Combine(StateRoot, "load-history.txt");

        public LoadMonitorForm(string gameRoot)
        {
            _gameRoot = gameRoot;
            _shaderCache = ShaderCacheLocator.Inspect(gameRoot);

            Text = "KaiTOR — загрузка The Old Realms";
            Width = 760;
            Height = 370;
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
                Width = 680,
                Height = 35,
                Font = new Font("Segoe UI Semibold", 18f),
                ForeColor = Color.White
            };

            _phase.SetBounds(30, 70, 680, 28);
            _phase.Font = new Font("Segoe UI Semibold", 11f);
            _detail.SetBounds(30, 103, 680, 50);
            _detail.ForeColor = Color.Silver;
            _progress.SetBounds(30, 160, 680, 24);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _time.SetBounds(30, 197, 320, 25);
            _eta.SetBounds(370, 197, 340, 25);
            _eta.TextAlign = ContentAlignment.TopRight;
            _tip.SetBounds(30, 235, 680, 78);
            _tip.ForeColor = Color.DarkGray;
            _tip.Text = _shaderCache.Describe() + Environment.NewLine +
                        "Если Bannerlord показывает «Не отвечает», не закрывайте его автоматически: монитор отдельно проверяет процесс и реальные события TOR.";

            Controls.Add(title);
            Controls.Add(_phase);
            Controls.Add(_detail);
            Controls.Add(_progress);
            Controls.Add(_time);
            Controls.Add(_eta);
            Controls.Add(_tip);

            _historicalSeconds = LoadMedianHistory();
            Shown += OnShown;
            FormClosed += (s, e) =>
            {
                RestoreStartupPriority();
                _timer.Stop();
            };

            _timer.Interval = 500;
            _timer.Tick += OnTick;
        }

        private void OnShown(object sender, EventArgs e)
        {
            Directory.CreateDirectory(StateRoot);
            SetPhase("Запускаем Bannerlord", "Подготовка обычного TaleWorlds launcher. " + ShortCacheMode(), 5);
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
            ReadStabilityEvents();
            UpdateEta(elapsed);

            var process = FindBannerlordProcess();
            if (process != null && !_ready)
            {
                TryApplyStartupPriority(process);
            }
            else if (_ready)
            {
                RestoreStartupPriority();
            }

            var health = process == null ? "" : DescribeProcess(process);

            if (_shaderRemaining.HasValue && _shaderRemaining.Value > 0)
            {
                var progress = EstimateShaderProgress(_shaderRemaining.Value);
                SetPhase(
                    "Компиляция шейдеров — осталось " + _shaderRemaining.Value,
                    "Bannerlord сообщает реальное число активных компиляций. " + ShortCacheMode() + " " + health,
                    progress);
                return;
            }

            if (_ready)
            {
                SetPhase(
                    "Готово",
                    _optimizationActive
                        ? "Главный экран готов. Оптимизация боя активирована. " + ShortCacheMode()
                        : "Главный экран готов. Проверьте лог KaiTOR Stability для статуса оптимизации. " + ShortCacheMode(),
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

            if (process == null)
            {
                SetPhase("Ожидаем Bannerlord", "Launcher запущен. Выберите TOR и нажмите Play.", 10);
                return;
            }

            if (_moduleLoaded)
            {
                SetPhase(
                    "TOR_Core загружен",
                    "Инициализируется интерфейс и стартовый экран. " + ShortCacheMode() + " " + health,
                    82);
                return;
            }

            var progressBeforeModule = EstimatePreModuleProgress(elapsed);
            var longLoad = elapsed.TotalSeconds >= Math.Max(90, (_historicalSeconds ?? 180) * 0.55);
            SetPhase(
                longLoad ? "TOR всё ещё загружается" : "Загрузка TOR и ресурсов",
                (longLoad
                    ? "Долгая стадия может быть инициализацией TOR или компиляцией ресурсов. Не закрывайте игру только из-за статуса «Не отвечает». "
                    : "Модули Bannerlord и The Old Realms инициализируются. ") + health,
                progressBeforeModule);
        }

        private void ReadStabilityEvents()
        {
            if (!File.Exists(StabilityLog)) return;

            try
            {
                using (var stream = new FileStream(StabilityLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (_stabilityLogOffset > stream.Length) _stabilityLogOffset = 0;
                    stream.Seek(_stabilityLogOffset, SeekOrigin.Begin);

                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            HandleStabilityLine(line);
                        }
                        _stabilityLogOffset = stream.Position;
                    }
                }
            }
            catch
            {
                // The game may rotate/write the file while we read it. Retry on the next tick.
            }
        }

        private void HandleStabilityLine(string line)
        {
            var parts = line.Split(new[] { '|' }, 3);
            DateTime timestamp;
            if (parts.Length < 2 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            {
                return;
            }
            timestamp = timestamp.ToUniversalTime();
            if (timestamp < _startedUtc.AddSeconds(-5)) return;

            var eventName = parts[1];
            var message = parts.Length >= 3 ? parts[2] : string.Empty;

            if (eventName == "INITIAL_SCREEN_READY") _ready = true;
            if (eventName == "MODULE_LOAD") _moduleLoaded = true;
            if (eventName == "OPTIMIZATION_ACTIVE") _optimizationActive = true;

            if (eventName == "SHADER_START" || eventName == "SHADER_PROGRESS")
            {
                var remaining = ParseRemaining(message);
                if (remaining >= 0) AddShaderSample(timestamp, remaining);
            }
            else if (eventName == "SHADER_COMPLETE")
            {
                _shaderRemaining = 0;
                _shaderSamples.Clear();
            }
        }

        private void AddShaderSample(DateTime timestamp, int remaining)
        {
            if (_shaderRemaining.HasValue && remaining > _shaderRemaining.Value)
            {
                // The engine has queued a new compilation wave. Do not mix rates from different waves.
                _shaderSamples.Clear();
                _shaderWavePeak = remaining;
            }

            _shaderRemaining = remaining;
            _shaderWavePeak = Math.Max(_shaderWavePeak, remaining);
            _shaderSamples.Add(new ShaderSample { Utc = timestamp, Remaining = remaining });

            var cutoff = timestamp.AddSeconds(-30);
            _shaderSamples.RemoveAll(x => x.Utc < cutoff);
        }

        private static int ParseRemaining(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return -1;
            const string prefix = "remaining=";
            var index = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;
            index += prefix.Length;

            var end = index;
            while (end < message.Length && char.IsDigit(message[end])) end++;
            int value;
            return int.TryParse(message.Substring(index, end - index), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : -1;
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

        private void TryApplyStartupPriority(Process process)
        {
            if (process == null) return;

            try
            {
                if (_boostedProcessId.HasValue && _boostedProcessId.Value == process.Id)
                {
                    return;
                }

                RestoreStartupPriority();
                _boostedProcessId = process.Id;
                _originalPriority = process.PriorityClass;

                // AboveNormal is deliberately conservative. It does not change affinity,
                // shader compiler thread count or engine internals, and is restored once
                // the initial module screen is ready.
                if (process.PriorityClass == ProcessPriorityClass.Idle ||
                    process.PriorityClass == ProcessPriorityClass.BelowNormal ||
                    process.PriorityClass == ProcessPriorityClass.Normal)
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    _startupPriorityBoostActive = true;
                }
            }
            catch
            {
                _startupPriorityBoostActive = false;
            }
        }

        private void RestoreStartupPriority()
        {
            if (!_boostedProcessId.HasValue)
            {
                return;
            }

            try
            {
                if (_startupPriorityBoostActive && _originalPriority.HasValue)
                {
                    using (var process = Process.GetProcessById(_boostedProcessId.Value))
                    {
                        if (!process.HasExited && process.PriorityClass == ProcessPriorityClass.AboveNormal)
                        {
                            process.PriorityClass = _originalPriority.Value;
                        }
                    }
                }
            }
            catch
            {
                // Startup boost is best-effort and must never block game launch/exit.
            }
            finally
            {
                _boostedProcessId = null;
                _originalPriority = null;
                _startupPriorityBoostActive = false;
            }
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
                    "CPU ~{0:0}% · RAM {1:0.0} GB · окно {2}{3}.",
                    Math.Max(0, cpuPercent),
                    ramGb,
                    process.Responding ? "отвечает" : "может выглядеть зависшим",
                    _startupPriorityBoostActive ? " · стартовый CPU boost" : string.Empty);
            }
            catch
            {
                return "Процесс Bannerlord активен.";
            }
        }

        private int EstimatePreModuleProgress(TimeSpan elapsed)
        {
            var basis = _historicalSeconds ?? 240d;
            var ratio = Math.Min(1d, elapsed.TotalSeconds / Math.Max(30d, basis * 0.8d));
            return 22 + (int)(ratio * 43d);
        }

        private int EstimateShaderProgress(int remaining)
        {
            if (_shaderWavePeak <= 0) return 55;
            var ratio = 1d - Math.Min(1d, remaining / (double)_shaderWavePeak);
            return 42 + (int)(ratio * 38d);
        }

        private void UpdateEta(TimeSpan elapsed)
        {
            if (_shaderRemaining.HasValue && _shaderRemaining.Value > 0)
            {
                double shadersPerSecond;
                if (TryGetShaderRate(out shadersPerSecond) && shadersPerSecond > 0.01d)
                {
                    var seconds = _shaderRemaining.Value / shadersPerSecond;
                    _eta.Text = "ETA шейдеров: ~" + FormatDuration(TimeSpan.FromSeconds(seconds));
                }
                else
                {
                    _eta.Text = "ETA шейдеров: измеряем скорость";
                }
                return;
            }

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
            _eta.Text = "ETA запуска: ~" + FormatDuration(TimeSpan.FromSeconds(remaining));
        }

        private bool TryGetShaderRate(out double shadersPerSecond)
        {
            shadersPerSecond = 0;
            if (_shaderSamples.Count < 2) return false;

            var first = _shaderSamples[0];
            var last = _shaderSamples[_shaderSamples.Count - 1];
            var seconds = (last.Utc - first.Utc).TotalSeconds;
            var compiled = first.Remaining - last.Remaining;
            if (seconds < 2d || compiled <= 0) return false;

            shadersPerSecond = compiled / seconds;
            return true;
        }

        private string ShortCacheMode()
        {
            return _shaderCache.IsKaiRedirect ? "Кэш: Kai redirect." : "Кэш: стандартный путь.";
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
            return Math.Max(0, value.Seconds) + "с";
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
