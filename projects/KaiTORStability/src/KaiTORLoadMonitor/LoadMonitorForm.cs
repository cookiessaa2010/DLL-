using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        private sealed class PrelaunchResult
        {
            public ShaderSourcePrestageResult ShaderSources { get; set; }
            public FirstLaunchWarmupResult Warmup { get; set; }
            public bool CacheDirectoryReady { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters counters);

        private readonly string _gameRoot;
        private readonly ShaderCacheInfo _shaderCache;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer = new Timer();
        private readonly Label _phase = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _percent = new Label();
        private readonly Label _time = new Label();
        private readonly Label _eta = new Label();
        private readonly Label _resource = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _tip = new Label();
        private readonly List<ShaderSample> _shaderSamples = new List<ShaderSample>();
        private readonly bool _likelyFirstShaderRun;

        private TimeSpan _lastCpu;
        private DateTime _lastCpuSampleUtc;
        private IoCounters _lastIo;
        private DateTime _lastIoSampleUtc;
        private DateTime _lastCacheScanUtc;
        private long _cacheBytes;
        private double _ioReadMb;
        private double _ioWriteMb;
        private double? _historicalSeconds;
        private bool _moduleLoaded;
        private bool _optimizationActive;
        private bool _ready;
        private bool _loadCompleteWritten;
        private bool _prelaunchPreparing;
        private bool _fullCacheSeen;
        private bool _processWasSeen;
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private long _stabilityLogOffset;
        private long _cleaveLogOffset;
        private int? _shaderRemaining;
        private int _shaderWavePeak;
        private int? _boostedProcessId;
        private ProcessPriorityClass? _originalPriority;
        private bool _startupPriorityBoostActive;
        private string _prestageSummary = string.Empty;
        private string _rosterSummary = string.Empty;
        private string _cleaveStatus = "KaiCleave: событий этой сессии пока нет";

        private static readonly string StateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string StabilityLog = Path.Combine(StateRoot, "KaiTORStability.log");
        private static readonly string HistoryFile = Path.Combine(StateRoot, "load-history.txt");

        public LoadMonitorForm(string gameRoot)
        {
            _gameRoot = gameRoot;
            _shaderCache = ShaderCacheLocator.Inspect(gameRoot);
            _likelyFirstShaderRun = IsLikelyFirstShaderRun(_shaderCache.Path);

            Text = "KaiTOR Stability 0.4.2 — TOR Monitor";
            Width = 820;
            Height = 500;
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
                Text = "KaiTOR Stability 0.4.2 — The Old Realms",
                Left = 28,
                Top = 18,
                Width = 740,
                Height = 34,
                Font = new Font("Segoe UI Semibold", 17f),
                ForeColor = Color.White
            };

            _phase.SetBounds(30, 62, 740, 28);
            _phase.Font = new Font("Segoe UI Semibold", 11f);
            _detail.SetBounds(30, 95, 740, 55);
            _detail.ForeColor = Color.Silver;
            _progress.SetBounds(30, 158, 740, 24);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _percent.SetBounds(30, 188, 740, 24);
            _percent.TextAlign = ContentAlignment.TopCenter;
            _percent.Font = new Font("Segoe UI Semibold", 10f);
            _time.SetBounds(30, 218, 350, 25);
            _eta.SetBounds(410, 218, 360, 25);
            _eta.TextAlign = ContentAlignment.TopRight;
            _resource.SetBounds(30, 250, 740, 52);
            _resource.ForeColor = Color.LightGray;
            _tip.SetBounds(30, 310, 740, 125);
            _tip.ForeColor = Color.DarkGray;
            _tip.Text = _shaderCache.Describe() + Environment.NewLine +
                        (_likelyFirstShaderRun
                            ? "Первый запуск: предварительно прогреваем TOR shader sources и критичные небольшие файлы модулей. "
                            : "Кэш уже существует: первый запуск не симулируется, используем прогретый путь. ") +
                        "Монитор остаётся активным после главного меню и отслеживает Build Shader Cache, RAM и KaiCleave.";

            Controls.Add(title);
            Controls.Add(_phase);
            Controls.Add(_detail);
            Controls.Add(_progress);
            Controls.Add(_percent);
            Controls.Add(_time);
            Controls.Add(_eta);
            Controls.Add(_resource);
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
            _cleaveLogOffset = GetLengthSafe(Path.Combine(_gameRoot, "Modules", "KaiCleave", "KaiCleave.log"));
            _timer.Start();

            var existing = FindBannerlordProcess();
            if (existing != null)
            {
                _processWasSeen = true;
                SetIndeterminatePhase("Этап 3 из 6: Bannerlord уже запущен", "Подключаем монитор к текущему процессу.");
                return;
            }

            StartPrelaunchPreparation();
        }

        private void StartPrelaunchPreparation()
        {
            _prelaunchPreparing = true;
            SetIndeterminatePhase(
                "Этап 1 из 6: подготовка запуска",
                _likelyFirstShaderRun
                    ? "Проверяем redirect cache, TOR shader sources и прогреваем критичные файлы первого запуска."
                    : "Проверяем TOR shader sources перед запуском.");

            ExternalEvent("PRELAUNCH_START",
                "firstShaderRun=" + _likelyFirstShaderRun + "; cacheMode=" + _shaderCache.Mode + "; cachePath=" + _shaderCache.Path);

            Task.Run(() =>
            {
                var result = new PrelaunchResult();
                if (_likelyFirstShaderRun && _shaderCache.IsKaiRedirect && !string.IsNullOrWhiteSpace(_shaderCache.Path))
                {
                    try
                    {
                        Directory.CreateDirectory(_shaderCache.Path);
                        result.CacheDirectoryReady = Directory.Exists(_shaderCache.Path);
                    }
                    catch { result.CacheDirectoryReady = false; }
                }

                result.ShaderSources = ShaderSourcePrestage.Prepare(_gameRoot, ReportPrestageProgress);
                if (_likelyFirstShaderRun)
                    result.Warmup = FirstLaunchWarmup.Prepare(_gameRoot);
                return result;
            }).ContinueWith(task => SafeBeginInvoke(() => CompletePrelaunchPreparation(task)));
        }

        private void ReportPrestageProgress(ShaderSourcePrestageProgress progress)
        {
            if (progress == null) return;
            SafeBeginInvoke(() =>
            {
                var text = progress.Total <= 0 ? "проверка" : progress.Completed + "/" + progress.Total;
                SetIndeterminatePhase("Этап 1 из 6: подготовка TOR shader sources", "Файлы: " + text +
                    (string.IsNullOrWhiteSpace(progress.FileName) ? string.Empty : " · " + progress.FileName));
            });
        }

        private void CompletePrelaunchPreparation(Task<PrelaunchResult> task)
        {
            _prelaunchPreparing = false;
            if (task == null || task.IsCanceled || task.IsFaulted)
            {
                _prestageSummary = "Предварительная подготовка завершилась с fallback; TOR продолжит штатным путём.";
                ExternalEvent("FIRST_LAUNCH_PREP", "fallback=true");
            }
            else
            {
                var result = task.Result;
                var shader = result != null ? result.ShaderSources : null;
                var warm = result != null ? result.Warmup : null;
                if (shader != null)
                {
                    _prestageSummary = "Shader sources: " + shader.CopiedFiles + " обновлено, " + shader.SkippedFiles + " актуальны, " + shader.WarmedFiles + " прогрето.";
                    ExternalEvent("SHADER_SOURCE_PRESTAGE",
                        "sourceFound=" + shader.SourceFound + "; targetReady=" + shader.TargetReady +
                        "; total=" + shader.TotalFiles + "; copied=" + shader.CopiedFiles +
                        "; skipped=" + shader.SkippedFiles + "; warmed=" + shader.WarmedFiles +
                        "; errors=" + shader.Errors + "; ms=" + (long)shader.Elapsed.TotalMilliseconds);
                }
                if (warm != null)
                {
                    _prestageSummary += " First-launch warmup: " + warm.Files + " файлов / " + FormatBytes(warm.Bytes) + ".";
                    ExternalEvent("FIRST_LAUNCH_WARMUP",
                        "files=" + warm.Files + "; bytes=" + warm.Bytes + "; errors=" + warm.Errors + "; ms=" + (long)warm.Elapsed.TotalMilliseconds +
                        "; cacheDirectoryReady=" + result.CacheDirectoryReady);
                }
            }

            SetIndeterminatePhase("Этап 2 из 6: запускаем Bannerlord", _prestageSummary);
            TryLaunch();
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (action == null || IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        private void TryLaunch()
        {
            try
            {
                if (FindBannerlordProcess() != null) return;
                var launcher = Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.Launcher.exe");
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
            _time.Text = "Сессия: " + FormatDuration(elapsed);

            ReadStabilityEvents();
            ReadCleaveEvents();

            if (_prelaunchPreparing)
            {
                _eta.Text = "ETA: подготовка первого запуска";
                return;
            }

            var process = FindBannerlordProcess();
            if (process != null)
            {
                _processWasSeen = true;
                if (!_ready) TryApplyStartupPriority(process); else RestoreStartupPriority();
                UpdateProcessAndCache(process);
            }
            else if (_processWasSeen)
            {
                RestoreStartupPriority();
                SetDeterminatePhase("Bannerlord закрыт", "Игровой процесс завершён. Монитор можно закрыть.", 100, "Готово");
                _eta.Text = "ETA: —";
                return;
            }

            if (_shaderRemaining.HasValue && _shaderRemaining.Value > 0)
            {
                ShowShaderPhase(process);
                return;
            }

            if (_ready)
            {
                if (!_loadCompleteWritten)
                {
                    _loadCompleteWritten = true;
                    SaveHistory(elapsed.TotalSeconds);
                    ExternalEvent("LOAD_COMPLETE", "seconds=" + elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                }

                var detail = "Главный экран готов. Монитор остаётся активным для Build Shader Cache и runtime-компиляции.";
                if (_optimizationActive) detail += " Battle optimization активна.";
                if (_fullCacheSeen && !string.IsNullOrWhiteSpace(_rosterSummary)) detail += " Последний roster: " + Shorten(_rosterSummary, 120);
                SetDeterminatePhase("Главное меню готово — монитор активен", detail, 100, "Прогресс запуска: 100%");
                _eta.Text = "ETA запуска: готово";
                return;
            }

            if (process == null)
            {
                SetIndeterminatePhase("Этап 2 из 6: ожидаем Bannerlord", "Launcher запущен. Выберите TOR и нажмите Play.");
                _eta.Text = "ETA: ожидаем запуск";
                return;
            }

            if (_moduleLoaded)
            {
                SetIndeterminatePhase("Этап 5 из 6: TOR_Core и KaiTOR Stability загружены", "Инициализируется главное меню. " + ActivityText());
            }
            else
            {
                SetIndeterminatePhase("Этап 3 из 6: загрузка Bannerlord / TOR", "Процесс работает. Точный процент появится только при реальном shader-counter. " + ActivityText());
            }

            UpdateStartupEta(elapsed);
        }

        private void ShowShaderPhase(Process process)
        {
            var remaining = _shaderRemaining.Value;
            var percent = GetShaderWavePercent(remaining);
            double rate;
            var hasRate = TryGetShaderRate(out rate);
            var eta = hasRate && rate > 0.01 ? FormatDuration(TimeSpan.FromSeconds(remaining / rate)) : "измеряем";
            var mode = _fullCacheSeen ? "Полная компиляция TOR" : "Runtime-компиляция";
            var detail = mode + ": осталось " + remaining + " shader jobs";
            if (hasRate) detail += " · " + rate.ToString("0.0", CultureInfo.InvariantCulture) + "/с";
            if (!string.IsNullOrWhiteSpace(_rosterSummary)) detail += " · " + Shorten(_rosterSummary, 95);
            SetDeterminatePhase("Этап 6 из 6: " + mode, detail, percent, "Компиляция текущей волны: " + percent + "%");
            _eta.Text = "ETA шейдеров: " + eta;
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
                        while ((line = reader.ReadLine()) != null) HandleStabilityLine(line);
                        _stabilityLogOffset = stream.Position;
                    }
                }
            }
            catch { }
        }

        private void HandleStabilityLine(string line)
        {
            var parts = line.Split(new[] { '|' }, 3);
            DateTime timestamp;
            if (parts.Length < 2 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp)) return;
            timestamp = timestamp.ToUniversalTime();
            if (timestamp < _startedUtc.AddSeconds(-5)) return;

            var eventName = parts[1];
            var message = parts.Length >= 3 ? parts[2] : string.Empty;
            if (eventName == "INITIAL_SCREEN_READY")
            {
                if (!_ready) { _ready = true; _lastActivityUtc = DateTime.UtcNow; }
                return;
            }
            if (eventName == "MODULE_LOAD") { _moduleLoaded = true; _lastActivityUtc = DateTime.UtcNow; }
            if (eventName == "OPTIMIZATION_ACTIVE") _optimizationActive = true;
            if (eventName == "SHADER_CACHE_ROSTER")
            {
                _fullCacheSeen = true;
                _rosterSummary = message;
                _lastActivityUtc = DateTime.UtcNow;
            }

            if (eventName == "SHADER_START" || eventName == "SHADER_PROGRESS")
            {
                var remaining = ParseRemaining(message);
                if (remaining >= 0) AddShaderSample(timestamp, remaining);
            }
            else if (eventName == "SHADER_COMPLETE")
            {
                _shaderRemaining = 0;
                _shaderSamples.Clear();
                _lastActivityUtc = DateTime.UtcNow;
            }
        }

        private void ReadCleaveEvents()
        {
            var path = Path.Combine(_gameRoot, "Modules", "KaiCleave", "KaiCleave.log");
            if (!File.Exists(path)) return;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (_cleaveLogOffset > stream.Length) _cleaveLogOffset = 0;
                    stream.Seek(_cleaveLogOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.IndexOf("CLEAVE_TIMING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("session started", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("TOR final-reaction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("TOR patch failed", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _cleaveStatus = "KaiCleave: " + Shorten(line, 150);
                            }
                        }
                        _cleaveLogOffset = stream.Position;
                    }
                }
            }
            catch { }
        }

        private void AddShaderSample(DateTime timestamp, int remaining)
        {
            if (_shaderRemaining.HasValue && remaining > _shaderRemaining.Value)
            {
                _shaderSamples.Clear();
                _shaderWavePeak = remaining;
            }
            _shaderRemaining = remaining;
            _shaderWavePeak = Math.Max(_shaderWavePeak, remaining);
            _shaderSamples.Add(new ShaderSample { Utc = timestamp, Remaining = remaining });
            var cutoff = timestamp.AddSeconds(-30);
            _shaderSamples.RemoveAll(x => x.Utc < cutoff);
            _lastActivityUtc = DateTime.UtcNow;
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
            return int.TryParse(message.Substring(index, end - index), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
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

        private void UpdateProcessAndCache(Process process)
        {
            try
            {
                process.Refresh();
                var now = DateTime.UtcNow;
                var cpu = process.TotalProcessorTime;
                double cpuPercent = 0;
                if (_lastCpuSampleUtc != default(DateTime))
                {
                    var wall = (now - _lastCpuSampleUtc).TotalMilliseconds;
                    var cpuMs = (cpu - _lastCpu).TotalMilliseconds;
                    if (wall > 0) cpuPercent = cpuMs / wall / Environment.ProcessorCount * 100d;
                }
                _lastCpu = cpu;
                _lastCpuSampleUtc = now;

                IoCounters io;
                if (GetProcessIoCounters(process.Handle, out io))
                {
                    if (_lastIoSampleUtc != default(DateTime))
                    {
                        var seconds = (now - _lastIoSampleUtc).TotalSeconds;
                        if (seconds > 0)
                        {
                            _ioReadMb = Math.Max(0, (double)(io.ReadTransferCount - _lastIo.ReadTransferCount) / 1024d / 1024d / seconds);
                            _ioWriteMb = Math.Max(0, (double)(io.WriteTransferCount - _lastIo.WriteTransferCount) / 1024d / 1024d / seconds);
                        }
                    }
                    _lastIo = io;
                    _lastIoSampleUtc = now;
                }

                if ((now - _lastCacheScanUtc).TotalSeconds >= 2d)
                {
                    _cacheBytes = GetDirectorySizeSafe(_shaderCache.Path);
                    _lastCacheScanUtc = now;
                }

                var ramGb = process.WorkingSet64 / 1024d / 1024d / 1024d;
                var memory = ramGb >= 32d ? "КРИТИЧЕСКАЯ RAM" : ramGb >= 24d ? "высокая RAM" : "RAM";
                _resource.Text = string.Format(CultureInfo.InvariantCulture,
                    "CPU {0:0}% · {1} {2:0.0} GB · I/O R {3:0.0} / W {4:0.0} MB/s · Shader cache {5}\r\n{6}",
                    Math.Max(0, cpuPercent), memory, ramGb, _ioReadMb, _ioWriteMb, FormatBytes(_cacheBytes), _cleaveStatus);

                if (cpuPercent > 0.5d || _ioReadMb > 0.1d || _ioWriteMb > 0.1d) _lastActivityUtc = now;
            }
            catch
            {
                _resource.Text = "Bannerlord.exe активен · метрики процесса временно недоступны.\r\n" + _cleaveStatus;
            }
        }

        private string ActivityText()
        {
            var age = Math.Max(0, (DateTime.UtcNow - _lastActivityUtc).TotalSeconds);
            if (age < 5) return "Активность: сейчас.";
            if (age < 30) return "Последняя активность: " + (int)age + " сек назад.";
            return "Последняя заметная активность: " + (int)age + " сек назад; проверяйте CPU/I/O перед завершением процесса.";
        }

        private void TryApplyStartupPriority(Process process)
        {
            if (process == null) return;
            try
            {
                if (_boostedProcessId.HasValue && _boostedProcessId.Value == process.Id) return;
                RestoreStartupPriority();
                _boostedProcessId = process.Id;
                _originalPriority = process.PriorityClass;
                if (process.PriorityClass == ProcessPriorityClass.Idle || process.PriorityClass == ProcessPriorityClass.BelowNormal || process.PriorityClass == ProcessPriorityClass.Normal)
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    _startupPriorityBoostActive = true;
                }
            }
            catch { _startupPriorityBoostActive = false; }
        }

        private void RestoreStartupPriority()
        {
            if (!_boostedProcessId.HasValue) return;
            try
            {
                if (_startupPriorityBoostActive && _originalPriority.HasValue)
                {
                    using (var process = Process.GetProcessById(_boostedProcessId.Value))
                    {
                        if (!process.HasExited && process.PriorityClass == ProcessPriorityClass.AboveNormal)
                            process.PriorityClass = _originalPriority.Value;
                    }
                }
            }
            catch { }
            finally
            {
                _boostedProcessId = null;
                _originalPriority = null;
                _startupPriorityBoostActive = false;
            }
        }

        private int GetShaderWavePercent(int remaining)
        {
            if (_shaderWavePeak <= 0) return 0;
            var ratio = 1d - Math.Min(1d, remaining / (double)_shaderWavePeak);
            return Math.Max(0, Math.Min(100, (int)Math.Round(ratio * 100d)));
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

        private void UpdateStartupEta(TimeSpan elapsed)
        {
            if (!_historicalSeconds.HasValue)
            {
                _eta.Text = _likelyFirstShaderRun ? "ETA запуска: калибруем первый запуск" : "ETA запуска: калибровка";
                return;
            }
            var remaining = Math.Max(0, _historicalSeconds.Value - elapsed.TotalSeconds);
            _eta.Text = "ETA запуска по истории: ~" + FormatDuration(TimeSpan.FromSeconds(remaining));
        }

        private void SetIndeterminatePhase(string phase, string detail)
        {
            _phase.Text = phase;
            _detail.Text = detail;
            if (_progress.Style != ProgressBarStyle.Marquee)
            {
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 28;
            }
            _percent.Text = "Точный процент недоступен — показываем реальную стадию и активность процесса";
        }

        private void SetDeterminatePhase(string phase, string detail, int progress, string progressText)
        {
            _phase.Text = phase;
            _detail.Text = detail;
            if (_progress.Style != ProgressBarStyle.Continuous) _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Max(0, Math.Min(100, progress));
            _percent.Text = progressText;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1) return string.Format("{0:0}ч {1:00}м", Math.Floor(value.TotalHours), value.Minutes);
            if (value.TotalMinutes >= 1) return string.Format("{0:0}м {1:00}с", Math.Floor(value.TotalMinutes), value.Seconds);
            return Math.Max(0, value.Seconds) + "с";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        private static bool IsLikelyFirstShaderRun(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return true;
                return !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(1).Any();
            }
            catch { return false; }
        }

        private static long GetDirectorySizeSafe(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        private static long GetLengthSafe(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
            return text.Substring(0, Math.Max(0, max - 3)) + "...";
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
                    if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 1) all.Add(parsed);
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

        private static void ExternalEvent(string eventName, string message)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                File.AppendAllText(StabilityLog,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" + eventName + "|" + (message ?? string.Empty) + Environment.NewLine);
            }
            catch { }
        }
    }
}
