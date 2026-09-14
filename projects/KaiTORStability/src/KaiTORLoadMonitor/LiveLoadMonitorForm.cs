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
    /// <summary>
    /// 0.4.3 live monitor hotfix.
    /// Design rule: the WinForms UI thread never scans the shader cache and never changes
    /// Bannerlord process priority. Loading evidence is based on the real process PID,
    /// CPU/RAM/I/O counters, runtime telemetry and background cache scans.
    /// </summary>
    internal sealed class LiveLoadMonitorForm : Form
    {
        private sealed class ShaderSample
        {
            public DateTime Utc;
            public int Remaining;
        }

        private sealed class PrelaunchResult
        {
            public ShaderSourcePrestageResult ShaderSources;
            public FirstLaunchWarmupResult Warmup;
            public bool CacheDirectoryReady;
        }

        private sealed class CacheSnapshot
        {
            public long Bytes;
            public int Files;
            public DateTime Utc;
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
        private readonly bool _likelyFirstShaderRun;
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Timer _timer = new Timer();
        private readonly Label _phase = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _progressText = new Label();
        private readonly Label _time = new Label();
        private readonly Label _eta = new Label();
        private readonly Label _resources = new Label();
        private readonly Label _activity = new Label();
        private readonly Label _tip = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly List<ShaderSample> _shaderSamples = new List<ShaderSample>();

        private long _stabilityOffset;
        private long _cleaveOffset;
        private bool _prelaunch;
        private bool _sessionStarted;
        private bool _moduleLoaded;
        private bool _ready;
        private bool _loadCompleteWritten;
        private bool _fullCacheSeen;
        private bool _optimizationActive;
        private int? _runtimePid;
        private Process _trackedProcess;
        private DateTime _lastDiscoveryUtc;
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private DateTime? _processDetectedUtc;
        private DateTime? _sessionStartedUtc;
        private DateTime? _moduleLoadedUtc;
        private DateTime? _readyUtc;
        private long _heartbeat;
        private int _activeSamples;
        private double _peakRamGb;

        private TimeSpan _lastCpu;
        private DateTime _lastCpuUtc;
        private double _cpuPercent;
        private IoCounters _lastIo;
        private bool _haveIo;
        private DateTime _lastIoUtc;
        private double _readRateMb;
        private double _writeRateMb;
        private ulong _baseReadBytes;
        private ulong _baseWriteBytes;
        private double _readSinceAttachMb;
        private double _writeSinceAttachMb;

        private bool _cacheScanRunning;
        private DateTime _lastCacheScanRequestUtc;
        private CacheSnapshot _cache = new CacheSnapshot();
        private long _lastCacheBytes;

        private int? _shaderRemaining;
        private int _shaderWavePeak;
        private string _rosterSummary = string.Empty;
        private string _cleaveStatus = "KaiCleave: событий текущей сессии пока нет";
        private string _prelaunchSummary = string.Empty;
        private double? _historicalSeconds;

        private static readonly string StateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string StabilityLog = Path.Combine(StateRoot, "KaiTORStability.log");
        private static readonly string HistoryFile = Path.Combine(StateRoot, "load-history.txt");

        public LiveLoadMonitorForm(string gameRoot)
        {
            _gameRoot = gameRoot;
            _shaderCache = ShaderCacheLocator.Inspect(gameRoot);
            _likelyFirstShaderRun = IsLikelyFirstShaderRun(_shaderCache.Path);
            _historicalSeconds = LoadMedianHistory();

            Text = "KaiTOR Stability 0.4.3 — Live TOR Monitor";
            Width = 900;
            Height = 570;
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
                Text = "KaiTOR Stability 0.4.3 — Live Loading Monitor",
                Left = 28,
                Top = 18,
                Width = 820,
                Height = 34,
                Font = new Font("Segoe UI Semibold", 17f),
                ForeColor = Color.White
            };

            _phase.SetBounds(30, 62, 820, 28);
            _phase.Font = new Font("Segoe UI Semibold", 11f);
            _detail.SetBounds(30, 95, 820, 58);
            _detail.ForeColor = Color.Silver;
            _progress.SetBounds(30, 160, 820, 24);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progressText.SetBounds(30, 190, 820, 24);
            _progressText.TextAlign = ContentAlignment.TopCenter;
            _progressText.Font = new Font("Segoe UI Semibold", 10f);
            _time.SetBounds(30, 220, 390, 25);
            _eta.SetBounds(450, 220, 400, 25);
            _eta.TextAlign = ContentAlignment.TopRight;
            _resources.SetBounds(30, 252, 820, 55);
            _resources.ForeColor = Color.LightGray;
            _activity.SetBounds(30, 312, 820, 64);
            _activity.ForeColor = Color.LightGray;
            _tip.SetBounds(30, 385, 820, 115);
            _tip.ForeColor = Color.DarkGray;
            _tip.Text = _shaderCache.Describe() + Environment.NewLine +
                        "Монитор 0.4.3 не повышает приоритет Bannerlord и не сканирует shader cache на UI-потоке. " +
                        "Даже без точного процента он показывает доказательства реальной загрузки: PID, CPU, RAM, I/O, cache activity и runtime milestones.";

            Controls.Add(title);
            Controls.Add(_phase);
            Controls.Add(_detail);
            Controls.Add(_progress);
            Controls.Add(_progressText);
            Controls.Add(_time);
            Controls.Add(_eta);
            Controls.Add(_resources);
            Controls.Add(_activity);
            Controls.Add(_tip);

            Shown += OnShown;
            FormClosed += (s, e) =>
            {
                _timer.Stop();
                DisposeTrackedProcess();
            };

            _timer.Interval = 250;
            _timer.Tick += OnTick;
        }

        private void OnShown(object sender, EventArgs e)
        {
            Directory.CreateDirectory(StateRoot);
            // Ignore historical logs completely. Only this monitor session is relevant.
            _stabilityOffset = GetLengthSafe(StabilityLog);
            _cleaveOffset = GetLengthSafe(Path.Combine(_gameRoot, "Modules", "KaiCleave", "KaiCleave.log"));
            _timer.Start();
            StartPrelaunchPreparation();
        }

        private void StartPrelaunchPreparation()
        {
            _prelaunch = true;
            SetIndeterminate(
                "Этап 1 из 6: подготовка запуска",
                _likelyFirstShaderRun
                    ? "Первый запуск: shader sources + ограниченный warmup TOR-файлов."
                    : "Проверяем shader sources; существующий кэш сохраняем.");

            ExternalEvent("PRELAUNCH_START",
                "monitor=0.4.3; firstShaderRun=" + _likelyFirstShaderRun +
                "; cacheMode=" + _shaderCache.Mode + "; cachePath=" + _shaderCache.Path);

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

                result.ShaderSources = ShaderSourcePrestage.Prepare(_gameRoot, progress =>
                {
                    if (progress == null) return;
                    SafeUi(() =>
                    {
                        var count = progress.Total > 0 ? progress.Completed + "/" + progress.Total : "проверка";
                        SetIndeterminate("Этап 1 из 6: shader sources", "Проверено: " + count +
                            (string.IsNullOrWhiteSpace(progress.FileName) ? string.Empty : " · " + progress.FileName));
                    });
                });

                if (_likelyFirstShaderRun)
                    result.Warmup = FirstLaunchWarmup.Prepare(_gameRoot);
                return result;
            }).ContinueWith(task => SafeUi(() => CompletePrelaunch(task)));
        }

        private void CompletePrelaunch(Task<PrelaunchResult> task)
        {
            _prelaunch = false;
            if (task == null || task.IsCanceled || task.IsFaulted)
            {
                _prelaunchSummary = "Подготовка: fallback; TOR продолжит штатно.";
                ExternalEvent("FIRST_LAUNCH_PREP", "monitor=0.4.3; fallback=true");
            }
            else
            {
                var result = task.Result;
                var shader = result != null ? result.ShaderSources : null;
                var warm = result != null ? result.Warmup : null;
                if (shader != null)
                {
                    _prelaunchSummary = "Shader sources: " + shader.CopiedFiles + " обновлено, " +
                                        shader.SkippedFiles + " актуальны, " + shader.WarmedFiles + " прогрето.";
                    ExternalEvent("SHADER_SOURCE_PRESTAGE",
                        "monitor=0.4.3; total=" + shader.TotalFiles + "; copied=" + shader.CopiedFiles +
                        "; skipped=" + shader.SkippedFiles + "; warmed=" + shader.WarmedFiles +
                        "; errors=" + shader.Errors + "; ms=" + (long)shader.Elapsed.TotalMilliseconds);
                }
                if (warm != null)
                {
                    _prelaunchSummary += " First-launch warmup: " + warm.Files + " файлов / " + FormatBytes(warm.Bytes) + ".";
                    ExternalEvent("FIRST_LAUNCH_WARMUP",
                        "monitor=0.4.3; files=" + warm.Files + "; bytes=" + warm.Bytes +
                        "; errors=" + warm.Errors + "; ms=" + (long)warm.Elapsed.TotalMilliseconds +
                        "; cacheDirectoryReady=" + result.CacheDirectoryReady);
                }
            }

            SetIndeterminate("Этап 2 из 6: ожидаем запуск игры", _prelaunchSummary + " Выберите TOR и нажмите Play.");
            TryLaunchLauncher();
        }

        private void TryLaunchLauncher()
        {
            try
            {
                if (FindGameProcess(false) != null) return;
                if (Process.GetProcessesByName("TaleWorlds.MountAndBlade.Launcher").Any()) return;

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
                _detail.Text = "Launcher не запущен автоматически: " + ex.Message;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            _heartbeat++;
            var elapsed = DateTime.UtcNow - _startedUtc;
            _time.Text = "Сессия: " + FormatDuration(elapsed) + " · heartbeat #" + _heartbeat;

            ReadStabilityEvents();
            ReadCleaveEvents();

            if (_prelaunch)
            {
                _eta.Text = "ETA: подготовка";
                _activity.Text = "Monitor heartbeat активен. UI-поток не выполняет тяжёлое сканирование кэша.";
                return;
            }

            var process = FindGameProcess(true);
            if (process != null)
            {
                if (!_processDetectedUtc.HasValue)
                {
                    _processDetectedUtc = DateTime.UtcNow;
                    _lastActivityUtc = DateTime.UtcNow;
                }
                SampleProcess(process);
                RequestCacheSnapshot();
            }
            else
            {
                _cpuPercent = 0;
                _readRateMb = 0;
                _writeRateMb = 0;
            }

            if (_shaderRemaining.HasValue && _shaderRemaining.Value > 0)
            {
                ShowShaderPhase();
                return;
            }

            if (_ready)
            {
                ShowReady(elapsed);
                return;
            }

            if (_moduleLoaded)
            {
                SetIndeterminate("Этап 5 из 6: TOR_Core + KaiTOR Stability загружены",
                    "Инициализируется главное меню. " + EvidenceSummary());
                UpdateStartupEta(elapsed);
                return;
            }

            if (_sessionStarted)
            {
                SetIndeterminate("Этап 4 из 6: игровой runtime запущен",
                    "KaiTOR runtime подтвердил PID " + (_runtimePid.HasValue ? _runtimePid.Value.ToString() : "?") + ". " + EvidenceSummary());
                UpdateStartupEta(elapsed);
                return;
            }

            if (process != null)
            {
                SetIndeterminate("Этап 3 из 6: Bannerlord загружается",
                    "Процесс игры обнаружен до KaiTOR runtime. " + EvidenceSummary());
                UpdateStartupEta(elapsed);
                return;
            }

            SetIndeterminate("Этап 2 из 6: ожидаем Bannerlord",
                "Launcher может быть открыт. После Play монитор ищет процесс по имени, пути, окну и затем по PID из SESSION_START.");
            _eta.Text = "ETA: ждём игровой процесс";
            UpdateResourceLabels(null);
        }

        private void ShowReady(TimeSpan elapsed)
        {
            if (!_loadCompleteWritten)
            {
                _loadCompleteWritten = true;
                SaveHistory(elapsed.TotalSeconds);
                ExternalEvent("LOAD_COMPLETE",
                    "monitor=0.4.3; seconds=" + elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) +
                    "; activeSamples=" + _activeSamples +
                    "; readMB=" + _readSinceAttachMb.ToString("0.0", CultureInfo.InvariantCulture) +
                    "; writeMB=" + _writeSinceAttachMb.ToString("0.0", CultureInfo.InvariantCulture) +
                    "; peakRamGB=" + _peakRamGb.ToString("0.0", CultureInfo.InvariantCulture));
            }

            var duration = _readyUtc.HasValue ? _readyUtc.Value - _startedUtc : elapsed;
            SetDeterminate("Главное меню готово — монитор остаётся активным",
                "Загрузка подтверждена реальной активностью процесса. " + EvidenceSummary(), 100,
                "Запуск завершён: 100% · активных выборок: " + _activeSamples);
            _eta.Text = "Фактический запуск: " + FormatDuration(duration);
            _activity.Text = TimelineText() + Environment.NewLine +
                             "После меню продолжаем отслеживать runtime shaders, RAM и KaiCleave.";
        }

        private void ShowShaderPhase()
        {
            var remaining = _shaderRemaining.Value;
            var percent = ShaderPercent(remaining);
            double rate;
            var hasRate = TryGetShaderRate(out rate);
            var mode = _fullCacheSeen ? "Build Shader Cache TOR" : "runtime shader compile";
            var detail = mode + ": осталось " + remaining + " задач";
            if (hasRate) detail += " · " + rate.ToString("0.0", CultureInfo.InvariantCulture) + "/с";
            if (!string.IsNullOrWhiteSpace(_rosterSummary)) detail += " · " + Shorten(_rosterSummary, 120);
            SetDeterminate("Шейдеры: " + mode, detail, percent,
                "Текущая shader-wave: " + percent + "% · remaining " + remaining);
            _eta.Text = hasRate && rate > 0.01
                ? "ETA шейдеров: ~" + FormatDuration(TimeSpan.FromSeconds(remaining / rate))
                : "ETA шейдеров: измеряем";
        }

        private Process FindGameProcess(bool allowDiscovery)
        {
            try
            {
                if (_trackedProcess != null)
                {
                    try
                    {
                        if (!_trackedProcess.HasExited) return _trackedProcess;
                    }
                    catch { }
                    DisposeTrackedProcess();
                }

                if (_runtimePid.HasValue)
                {
                    try
                    {
                        var byPid = Process.GetProcessById(_runtimePid.Value);
                        if (!byPid.HasExited)
                        {
                            _trackedProcess = byPid;
                            ResetProcessCounters();
                            return _trackedProcess;
                        }
                    }
                    catch { }
                }

                if (!allowDiscovery) return null;
                if ((DateTime.UtcNow - _lastDiscoveryUtc).TotalSeconds < 1.5) return null;
                _lastDiscoveryUtc = DateTime.UtcNow;

                foreach (var name in new[] { "Bannerlord", "Bannerlord.Native", "Bannerlord_BE" })
                {
                    Process[] found;
                    try { found = Process.GetProcessesByName(name); }
                    catch { continue; }
                    foreach (var p in found)
                    {
                        if (IsUsableGameProcess(p))
                        {
                            _trackedProcess = p;
                            ResetProcessCounters();
                            return _trackedProcess;
                        }
                        p.Dispose();
                    }
                }

                foreach (var p in Process.GetProcesses())
                {
                    if (p.Id == Process.GetCurrentProcess().Id) { p.Dispose(); continue; }
                    try
                    {
                        var name = p.ProcessName ?? string.Empty;
                        if (name.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) { p.Dispose(); continue; }
                        if (name.IndexOf("Bannerlord", StringComparison.OrdinalIgnoreCase) >= 0 || IsGamePath(p) || IsBannerlordWindow(p))
                        {
                            if (!p.HasExited)
                            {
                                _trackedProcess = p;
                                ResetProcessCounters();
                                return _trackedProcess;
                            }
                        }
                    }
                    catch { }
                    p.Dispose();
                }
            }
            catch { }
            return null;
        }

        private bool IsUsableGameProcess(Process process)
        {
            if (process == null) return false;
            try
            {
                if (process.HasExited) return false;
                if ((process.ProcessName ?? string.Empty).IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return true;
            }
            catch { return false; }
        }

        private bool IsGamePath(Process process)
        {
            try
            {
                var file = process.MainModule != null ? process.MainModule.FileName : null;
                if (string.IsNullOrWhiteSpace(file)) return false;
                var binRoot = Path.Combine(_gameRoot, "bin", "Win64_Shipping_Client");
                return file.StartsWith(binRoot, StringComparison.OrdinalIgnoreCase) &&
                       file.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        private static bool IsBannerlordWindow(Process process)
        {
            try
            {
                var title = process.MainWindowTitle ?? string.Empty;
                return title.IndexOf("Bannerlord", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       title.IndexOf("Mount & Blade II", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void SampleProcess(Process process)
        {
            try
            {
                process.Refresh();
                var now = DateTime.UtcNow;
                var cpu = process.TotalProcessorTime;
                if (_lastCpuUtc != default(DateTime))
                {
                    var wallMs = (now - _lastCpuUtc).TotalMilliseconds;
                    var cpuMs = (cpu - _lastCpu).TotalMilliseconds;
                    if (wallMs > 0) _cpuPercent = Math.Max(0, cpuMs / wallMs / Environment.ProcessorCount * 100d);
                }
                _lastCpu = cpu;
                _lastCpuUtc = now;

                IoCounters io;
                if (GetProcessIoCounters(process.Handle, out io))
                {
                    if (!_haveIo)
                    {
                        _haveIo = true;
                        _baseReadBytes = io.ReadTransferCount;
                        _baseWriteBytes = io.WriteTransferCount;
                    }
                    if (_lastIoUtc != default(DateTime))
                    {
                        var seconds = (now - _lastIoUtc).TotalSeconds;
                        if (seconds > 0)
                        {
                            _readRateMb = SafeDelta(io.ReadTransferCount, _lastIo.ReadTransferCount) / 1024d / 1024d / seconds;
                            _writeRateMb = SafeDelta(io.WriteTransferCount, _lastIo.WriteTransferCount) / 1024d / 1024d / seconds;
                        }
                    }
                    _readSinceAttachMb = SafeDelta(io.ReadTransferCount, _baseReadBytes) / 1024d / 1024d;
                    _writeSinceAttachMb = SafeDelta(io.WriteTransferCount, _baseWriteBytes) / 1024d / 1024d;
                    _lastIo = io;
                    _lastIoUtc = now;
                }

                var ramGb = process.WorkingSet64 / 1024d / 1024d / 1024d;
                _peakRamGb = Math.Max(_peakRamGb, ramGb);
                if (_cpuPercent > 0.5 || _readRateMb > 0.05 || _writeRateMb > 0.05)
                {
                    _lastActivityUtc = now;
                    _activeSamples++;
                }

                UpdateResourceLabels(process, ramGb);
            }
            catch
            {
                _resources.Text = "Процесс обнаружен, но часть счётчиков Windows временно недоступна. PID=" + SafePid(process);
            }
        }

        private void UpdateResourceLabels(Process process, double? ramGb = null)
        {
            var ram = ramGb ?? 0;
            var ramLabel = ram >= 32 ? "КРИТИЧЕСКАЯ RAM" : ram >= 24 ? "высокая RAM" : "RAM";
            _resources.Text = string.Format(CultureInfo.InvariantCulture,
                "PID {0} · CPU {1:0.0}% · {2} {3:0.0} GB · I/O R {4:0.0} / W {5:0.0} MB/s\r\n" +
                "С момента захвата: read {6:0} MB · write {7:0} MB · peak RAM {8:0.0} GB · {9}",
                process != null ? SafePid(process) : 0,
                _cpuPercent, ramLabel, ram, _readRateMb, _writeRateMb,
                _readSinceAttachMb, _writeSinceAttachMb, _peakRamGb, CacheText());

            var age = Math.Max(0, (DateTime.UtcNow - _lastActivityUtc).TotalSeconds);
            _activity.Text = "Реальная активность: " + (age < 2 ? "СЕЙЧАС" : ((int)age + " сек назад")) +
                             " · active samples=" + _activeSamples +
                             " · cache scan выполняется в фоне=" + _cacheScanRunning + Environment.NewLine +
                             _cleaveStatus;
        }

        private string EvidenceSummary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "CPU {0:0.0}% · I/O R {1:0.0}/W {2:0.0} MB/s · накоплено read {3:0} MB · active samples {4}.",
                _cpuPercent, _readRateMb, _writeRateMb, _readSinceAttachMb, _activeSamples);
        }

        private void RequestCacheSnapshot()
        {
            if (_cacheScanRunning) return;
            var now = DateTime.UtcNow;
            if ((now - _lastCacheScanRequestUtc).TotalSeconds < 8) return;
            _lastCacheScanRequestUtc = now;
            _cacheScanRunning = true;
            var path = _shaderCache.Path;

            Task.Run(() => ScanCache(path)).ContinueWith(task => SafeUi(() =>
            {
                _cacheScanRunning = false;
                if (task.IsCanceled || task.IsFaulted || task.Result == null) return;
                var snapshot = task.Result;
                if (snapshot.Bytes != _lastCacheBytes)
                {
                    _lastActivityUtc = DateTime.UtcNow;
                    _lastCacheBytes = snapshot.Bytes;
                }
                _cache = snapshot;
            }));
        }

        private static CacheSnapshot ScanCache(string path)
        {
            var result = new CacheSnapshot { Utc = DateTime.UtcNow };
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return result;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        result.Bytes += new FileInfo(file).Length;
                        result.Files++;
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private string CacheText()
        {
            if (_cache.Utc == default(DateTime)) return "shader cache: ожидаем фоновую выборку";
            return "shader cache " + FormatBytes(_cache.Bytes) + " / " + _cache.Files + " файлов";
        }

        private void ReadStabilityEvents()
        {
            if (!File.Exists(StabilityLog)) return;
            try
            {
                using (var stream = new FileStream(StabilityLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (_stabilityOffset > stream.Length) _stabilityOffset = 0;
                    stream.Seek(_stabilityOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) HandleStabilityLine(line);
                        _stabilityOffset = stream.Position;
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
            if (timestamp < _startedUtc.AddSeconds(-3)) return;

            var name = parts[1];
            var message = parts.Length >= 3 ? parts[2] : string.Empty;
            _lastActivityUtc = DateTime.UtcNow;

            if (name == "SESSION_START")
            {
                _sessionStarted = true;
                _sessionStartedUtc = timestamp;
                var pid = ParseIntField(message, "pid=");
                if (pid > 0) _runtimePid = pid;
            }
            else if (name == "MODULE_LOAD")
            {
                _moduleLoaded = true;
                _moduleLoadedUtc = timestamp;
            }
            else if (name == "INITIAL_SCREEN_READY")
            {
                if (!_ready)
                {
                    _ready = true;
                    _readyUtc = timestamp;
                }
            }
            else if (name == "OPTIMIZATION_ACTIVE")
            {
                _optimizationActive = true;
            }
            else if (name == "SHADER_CACHE_ROSTER")
            {
                _fullCacheSeen = true;
                _rosterSummary = message;
            }
            else if (name == "SHADER_START" || name == "SHADER_PROGRESS")
            {
                var remaining = ParseIntField(message, "remaining=");
                if (remaining >= 0) AddShaderSample(timestamp, remaining);
            }
            else if (name == "SHADER_COMPLETE")
            {
                _shaderRemaining = 0;
                _shaderSamples.Clear();
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
                    if (_cleaveOffset > stream.Length) _cleaveOffset = 0;
                    stream.Seek(_cleaveOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.IndexOf("CLEAVE_TIMING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("session started", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("TOR final-reaction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("TOR patch", StringComparison.OrdinalIgnoreCase) >= 0)
                                _cleaveStatus = "KaiCleave: " + Shorten(line, 165);
                        }
                        _cleaveOffset = stream.Position;
                    }
                }
            }
            catch { }
        }

        private void AddShaderSample(DateTime utc, int remaining)
        {
            if (_shaderRemaining.HasValue && remaining > _shaderRemaining.Value)
            {
                _shaderSamples.Clear();
                _shaderWavePeak = remaining;
            }
            _shaderRemaining = remaining;
            _shaderWavePeak = Math.Max(_shaderWavePeak, remaining);
            _shaderSamples.Add(new ShaderSample { Utc = utc, Remaining = remaining });
            var cutoff = utc.AddSeconds(-30);
            _shaderSamples.RemoveAll(x => x.Utc < cutoff);
        }

        private int ShaderPercent(int remaining)
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
            if (seconds < 2 || compiled <= 0) return false;
            shadersPerSecond = compiled / seconds;
            return true;
        }

        private string TimelineText()
        {
            var parts = new List<string>();
            if (_processDetectedUtc.HasValue) parts.Add("process " + FormatDuration(_processDetectedUtc.Value - _startedUtc));
            if (_sessionStartedUtc.HasValue) parts.Add("runtime " + FormatDuration(_sessionStartedUtc.Value - _startedUtc));
            if (_moduleLoadedUtc.HasValue) parts.Add("module " + FormatDuration(_moduleLoadedUtc.Value - _startedUtc));
            if (_readyUtc.HasValue) parts.Add("menu " + FormatDuration(_readyUtc.Value - _startedUtc));
            if (_optimizationActive) parts.Add("battle optimization active");
            return "Timeline: " + (parts.Count > 0 ? string.Join(" → ", parts) : "milestones пока нет");
        }

        private void UpdateStartupEta(TimeSpan elapsed)
        {
            if (!_historicalSeconds.HasValue)
            {
                _eta.Text = "ETA: калибровка по этому запуску";
                return;
            }
            var remaining = Math.Max(0, _historicalSeconds.Value - elapsed.TotalSeconds);
            _eta.Text = "ETA по истории: ~" + FormatDuration(TimeSpan.FromSeconds(remaining));
        }

        private void SetIndeterminate(string phase, string detail)
        {
            _phase.Text = phase;
            _detail.Text = detail;
            if (_progress.Style != ProgressBarStyle.Marquee)
            {
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 22;
            }
            _progressText.Text = "Точного процента нет — ниже показываются реальные признаки загрузки";
        }

        private void SetDeterminate(string phase, string detail, int value, string text)
        {
            _phase.Text = phase;
            _detail.Text = detail;
            if (_progress.Style != ProgressBarStyle.Continuous) _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Max(0, Math.Min(100, value));
            _progressText.Text = text;
        }

        private void SafeUi(Action action)
        {
            try
            {
                if (action == null || IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        private void ResetProcessCounters()
        {
            _lastCpuUtc = default(DateTime);
            _lastIoUtc = default(DateTime);
            _haveIo = false;
            _cpuPercent = 0;
            _readRateMb = 0;
            _writeRateMb = 0;
        }

        private void DisposeTrackedProcess()
        {
            try { if (_trackedProcess != null) _trackedProcess.Dispose(); } catch { }
            _trackedProcess = null;
            ResetProcessCounters();
        }

        private static ulong SafeDelta(ulong current, ulong previous)
        {
            return current >= previous ? current - previous : 0;
        }

        private static int SafePid(Process process)
        {
            try { return process != null ? process.Id : 0; } catch { return 0; }
        }

        private static int ParseIntField(string message, string prefix)
        {
            if (string.IsNullOrWhiteSpace(message)) return -1;
            var index = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;
            index += prefix.Length;
            var end = index;
            while (end < message.Length && char.IsDigit(message[end])) end++;
            int value;
            return end > index && int.TryParse(message.Substring(index, end - index), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : -1;
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

        private static long GetLengthSafe(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; }
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1) return string.Format("{0:0}ч {1:00}м", Math.Floor(value.TotalHours), value.Minutes);
            if (value.TotalMinutes >= 1) return string.Format("{0:0}м {1:00}с", Math.Floor(value.TotalMinutes), value.Seconds);
            return Math.Max(0, (int)value.TotalSeconds) + "с";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
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
