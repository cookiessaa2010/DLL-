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
    /// 0.4.4 observability monitor.
    /// - process-to-menu is measured from Bannerlord Process.StartTime, not monitor launch time;
    /// - rgl_log_<pid>.txt is tailed in a background worker and translated to human-readable stages;
    /// - session peak RAM and TOR full-shader-build RAM are recorded automatically;
    /// - TOR full-cache waves are separated from later runtime/terrain shader waves;
    /// - all expensive directory/log work stays off the WinForms UI thread.
    /// </summary>
    internal sealed class LiveLoadMonitorForm44 : Form
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

        private sealed class RglPollResult
        {
            public string Path;
            public long StartOffset;
            public long NewOffset;
            public long FileLength;
            public DateTime LastWriteUtc;
            public readonly List<string> Lines = new List<string>();
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
        private readonly DateTime _monitorStartedUtc = DateTime.UtcNow;
        private readonly Timer _timer = new Timer();
        private readonly Label _phase = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _progressText = new Label();
        private readonly Label _time = new Label();
        private readonly Label _eta = new Label();
        private readonly Label _resources = new Label();
        private readonly Label _rgl = new Label();
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
        private bool _optimizationActive;
        private int? _runtimePid;
        private Process _trackedProcess;
        private DateTime _lastDiscoveryUtc;
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private DateTime? _processDetectedUtc;
        private DateTime? _processStartUtc;
        private DateTime? _sessionStartedUtc;
        private DateTime? _moduleLoadedUtc;
        private DateTime? _readyUtc;
        private long _heartbeat;
        private int _activeSamples;

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
        private double _currentRamGb;
        private double _peakRamGb;
        private DateTime _lastRamLogUtc;
        private double _lastLoggedPeakRamGb;

        private bool _cacheScanRunning;
        private DateTime _lastCacheScanRequestUtc;
        private CacheSnapshot _cache = new CacheSnapshot();
        private long _lastCacheBytes;

        private int? _shaderRemaining;
        private int _shaderWavePeak;
        private string _shaderMode = "runtime";
        private string _rosterSummary = string.Empty;
        private string _runtimeShaderKind = string.Empty;
        private DateTime _runtimeShaderKindUtc;

        private bool _fullBuildPending;
        private bool _fullBuildSession;
        private bool _fullBuildActive;
        private DateTime? _fullBuildLastCompleteUtc;
        private double _shaderBuildRamBefore;
        private double _shaderBuildPeakRam;
        private double _shaderBuildRamAfter;

        private string _cleaveStatus = "KaiCleave: событий текущей сессии пока нет";
        private string _prelaunchSummary = string.Empty;
        private double? _historicalProcessToMenuSeconds;

        private string _rglPath;
        private long _rglOffset;
        private bool _rglPollRunning;
        private DateTime _lastRglPollRequestUtc;
        private DateTime? _rglLastGrowthUtc;
        private DateTime? _rglLastWriteUtc;
        private long _rglBytesRead;
        private int _rglLinesRead;
        private string _rglStage = "ожидаем rgl_log";
        private string _rglLastLine = string.Empty;
        private double? _engineLoadingSeconds;

        private static readonly string StateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiTORStability");
        private static readonly string StabilityLog = Path.Combine(StateRoot, "KaiTORStability.log");
        private static readonly string MonitorLog = Path.Combine(StateRoot, "KaiTORMonitor.log");
        private static readonly string HistoryV2File = Path.Combine(StateRoot, "load-history-v2.csv");

        public LiveLoadMonitorForm44(string gameRoot)
        {
            _gameRoot = gameRoot;
            _shaderCache = ShaderCacheLocator.Inspect(gameRoot);
            _likelyFirstShaderRun = IsLikelyFirstShaderRun(_shaderCache.Path);
            _historicalProcessToMenuSeconds = LoadMedianProcessHistory();

            Text = "KaiTOR Stability 0.4.4 — RGL / RAM Monitor";
            Width = 930;
            Height = 665;
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
                Text = "KaiTOR Stability 0.4.4 — Live TOR Observability",
                Left = 28,
                Top = 16,
                Width = 860,
                Height = 34,
                Font = new Font("Segoe UI Semibold", 17f),
                ForeColor = Color.White
            };

            _phase.SetBounds(30, 58, 860, 28);
            _phase.Font = new Font("Segoe UI Semibold", 11f);
            _detail.SetBounds(30, 90, 860, 60);
            _detail.ForeColor = Color.Silver;
            _progress.SetBounds(30, 155, 860, 24);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progressText.SetBounds(30, 185, 860, 24);
            _progressText.TextAlign = ContentAlignment.TopCenter;
            _progressText.Font = new Font("Segoe UI Semibold", 10f);
            _time.SetBounds(30, 214, 420, 25);
            _eta.SetBounds(470, 214, 420, 25);
            _eta.TextAlign = ContentAlignment.TopRight;
            _resources.SetBounds(30, 245, 860, 58);
            _resources.ForeColor = Color.LightGray;
            _rgl.SetBounds(30, 308, 860, 70);
            _rgl.ForeColor = Color.LightSteelBlue;
            _activity.SetBounds(30, 383, 860, 72);
            _activity.ForeColor = Color.LightGray;
            _tip.SetBounds(30, 462, 860, 145);
            _tip.ForeColor = Color.DarkGray;
            _tip.Text = _shaderCache.Describe() + Environment.NewLine +
                        "0.4.4: RGL читается фоном по PID, время запуска считается от Process.StartTime, " +
                        "peak RAM и RAM полной компиляции записываются автоматически. " +
                        "First-launch warmup теперь видит TOR и в Steam Workshop.";

            Controls.Add(title);
            Controls.Add(_phase);
            Controls.Add(_detail);
            Controls.Add(_progress);
            Controls.Add(_progressText);
            Controls.Add(_time);
            Controls.Add(_eta);
            Controls.Add(_resources);
            Controls.Add(_rgl);
            Controls.Add(_activity);
            Controls.Add(_tip);

            Shown += OnShown;
            FormClosed += OnFormClosed;

            _timer.Interval = 250;
            _timer.Tick += OnTick;
        }

        private void OnShown(object sender, EventArgs e)
        {
            Directory.CreateDirectory(StateRoot);
            _stabilityOffset = GetLengthSafe(StabilityLog);
            _cleaveOffset = GetLengthSafe(Path.Combine(_gameRoot, "Modules", "KaiCleave", "KaiCleave.log"));
            MonitorEvent("MONITOR_START", "version=0.4.4; gameRoot=" + _gameRoot + "; firstShaderRun=" + _likelyFirstShaderRun);
            _timer.Start();
            StartPrelaunchPreparation();
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            _timer.Stop();
            FinalizeFullBuildSummary(true);
            WriteSessionSummary("window_closed");
            DisposeTrackedProcess();
        }

        private void StartPrelaunchPreparation()
        {
            _prelaunch = true;
            SetIndeterminate(
                "Этап 1 из 7: подготовка запуска",
                _likelyFirstShaderRun
                    ? "Первый запуск: shader sources + bounded warmup TOR_Core / Armory / Environment."
                    : "Проверяем shader sources; существующий compiled cache не трогаем.");

            ExternalEvent("PRELAUNCH_START",
                "monitor=0.4.4; firstShaderRun=" + _likelyFirstShaderRun +
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
                        SetIndeterminate("Этап 1 из 7: shader sources",
                            "Проверено: " + count +
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
                ExternalEvent("FIRST_LAUNCH_PREP", "monitor=0.4.4; fallback=true");
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
                        "monitor=0.4.4; total=" + shader.TotalFiles + "; copied=" + shader.CopiedFiles +
                        "; skipped=" + shader.SkippedFiles + "; warmed=" + shader.WarmedFiles +
                        "; errors=" + shader.Errors + "; ms=" + (long)shader.Elapsed.TotalMilliseconds);
                }
                if (warm != null)
                {
                    _prelaunchSummary += " First-launch warmup: " + warm.Files + " файлов / " + FormatBytes(warm.Bytes) +
                                         " · TOR roots=" + warm.ModuleRoots + ".";
                    ExternalEvent("FIRST_LAUNCH_WARMUP",
                        "monitor=0.4.4; moduleRoots=" + warm.ModuleRoots + "; files=" + warm.Files +
                        "; bytes=" + warm.Bytes + "; errors=" + warm.Errors +
                        "; ms=" + (long)warm.Elapsed.TotalMilliseconds +
                        "; cacheDirectoryReady=" + result.CacheDirectoryReady);
                }
            }

            SetIndeterminate("Этап 2 из 7: ожидаем запуск игры", _prelaunchSummary + " Выберите TOR и нажмите Play.");
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
            _time.Text = "Monitor: " + FormatDuration(DateTime.UtcNow - _monitorStartedUtc) + " · heartbeat #" + _heartbeat;

            ReadStabilityEvents();
            ReadCleaveEvents();
            FinalizeFullBuildSummary(false);

            if (_prelaunch)
            {
                _eta.Text = "ETA: подготовка";
                _activity.Text = "Monitor heartbeat активен. Тяжёлые операции выполняются вне UI-потока.";
                return;
            }

            var process = FindGameProcess(true);
            if (process != null)
            {
                if (!_processDetectedUtc.HasValue)
                    _processDetectedUtc = DateTime.UtcNow;

                CaptureProcessStart(process);
                SampleProcess(process);
                RequestCacheSnapshot();
                EnsureRglPath(process);
                RequestRglPoll();
                LogRamSampleIfDue();
            }
            else
            {
                _cpuPercent = 0;
                _readRateMb = 0;
                _writeRateMb = 0;
            }

            RenderRglStatus();
            RenderActivity(process);

            if (_shaderRemaining.HasValue && _shaderRemaining.Value > 0)
            {
                ShowShaderPhase();
                return;
            }

            if (_ready)
            {
                ShowReady();
                return;
            }

            if (_moduleLoaded)
            {
                SetIndeterminate("Этап 6 из 7: TOR_Core + KaiTOR загружены",
                    "Финализируется главное меню. RGL: " + _rglStage + ". " + EvidenceSummary());
                UpdateStartupEta();
                return;
            }

            if (_sessionStarted)
            {
                SetIndeterminate("Этап 5 из 7: игровой runtime запущен",
                    "KaiTOR runtime подтвердил PID " + (_runtimePid.HasValue ? _runtimePid.Value.ToString() : "?") +
                    ". RGL: " + _rglStage + ". " + EvidenceSummary());
                UpdateStartupEta();
                return;
            }

            if (process != null)
            {
                SetIndeterminate("Этап 3–4 из 7: Bannerlord / TOR загружается",
                    "RGL стадия: " + _rglStage + ". " + EvidenceSummary());
                UpdateStartupEta();
                return;
            }

            SetIndeterminate("Этап 2 из 7: ожидаем Bannerlord",
                "Launcher может быть открыт. После Play монитор привяжется к процессу и затем к rgl_log_<PID>.txt.");
            _eta.Text = "ETA: ждём игровой процесс";
            UpdateResourceLabels(null);
        }

        private void ShowReady()
        {
            if (!_loadCompleteWritten)
            {
                _loadCompleteWritten = true;
                var wait = GetWaitToProcessSeconds();
                var processToMenu = GetProcessToMenuSeconds();
                var moduleToMenu = GetModuleToMenuSeconds();
                SaveStructuredHistory(wait, processToMenu, moduleToMenu, _engineLoadingSeconds, _peakRamGb);
                ExternalEvent("LOAD_COMPLETE",
                    "monitor=0.4.4; waitToProcessSeconds=" + F(wait) +
                    "; processToMenuSeconds=" + F(processToMenu) +
                    "; moduleToMenuSeconds=" + F(moduleToMenu) +
                    "; engineLoadingSeconds=" + F(_engineLoadingSeconds) +
                    "; activeSamples=" + _activeSamples +
                    "; rglLines=" + _rglLinesRead +
                    "; readMB=" + F(_readSinceAttachMb) +
                    "; writeMB=" + F(_writeSinceAttachMb) +
                    "; peakRamGB=" + F(_peakRamGb));
                WriteSessionSummary("menu_ready");
            }

            SetDeterminate("Этап 7 из 7: главное меню готово — монитор остаётся активным",
                "process→menu " + FormatSeconds(GetProcessToMenuSeconds()) +
                " · engine Loading Time " + FormatSeconds(_engineLoadingSeconds) +
                " · peak RAM " + _peakRamGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB", 100,
                "Запуск завершён: 100% · дальше отслеживаем runtime shaders / RAM / KaiCleave");

            _eta.Text = "wait→process " + FormatSeconds(GetWaitToProcessSeconds()) +
                        " · module→menu " + FormatSeconds(GetModuleToMenuSeconds());
        }

        private void ShowShaderPhase()
        {
            var remaining = _shaderRemaining.Value;
            var percent = ShaderPercent(remaining);
            double rate;
            var hasRate = TryGetShaderRate(out rate);
            var mode = GetShaderDisplayMode();
            var detail = mode + ": осталось " + remaining + " задач";
            if (hasRate) detail += " · " + rate.ToString("0.0", CultureInfo.InvariantCulture) + "/с";
            if (_fullBuildActive)
            {
                detail += " · RAM before " + _shaderBuildRamBefore.ToString("0.0", CultureInfo.InvariantCulture) +
                          " / peak " + _shaderBuildPeakRam.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            }
            if (!string.IsNullOrWhiteSpace(_rosterSummary) && (_fullBuildActive || _fullBuildPending))
                detail += " · " + Shorten(_rosterSummary, 120);

            SetDeterminate("Шейдеры: " + mode, detail, percent,
                "Текущая shader-wave: " + percent + "% · remaining " + remaining);
            _eta.Text = hasRate && rate > 0.01
                ? "ETA шейдеров: ~" + FormatDuration(TimeSpan.FromSeconds(remaining / rate))
                : "ETA шейдеров: измеряем";
        }

        private string GetShaderDisplayMode()
        {
            if (_fullBuildActive || _fullBuildPending) return "TOR Build Shader Cache";
            if (!string.IsNullOrWhiteSpace(_runtimeShaderKind) &&
                (DateTime.UtcNow - _runtimeShaderKindUtc).TotalSeconds < 12)
            {
                if (string.Equals(_runtimeShaderKind, "terrain", StringComparison.OrdinalIgnoreCase))
                    return "runtime terrain shaders";
                return "runtime " + _runtimeShaderKind + " shaders";
            }
            return "runtime shaders";
        }

        private Process FindGameProcess(bool allowDiscovery)
        {
            try
            {
                if (_trackedProcess != null)
                {
                    try { if (!_trackedProcess.HasExited) return _trackedProcess; } catch { }
                    DisposeTrackedProcess();
                }

                if (_runtimePid.HasValue)
                {
                    try
                    {
                        var byPid = Process.GetProcessById(_runtimePid.Value);
                        if (!byPid.HasExited)
                        {
                            AttachProcess(byPid);
                            return _trackedProcess;
                        }
                    }
                    catch { }
                }

                if (!allowDiscovery) return null;
                if ((DateTime.UtcNow - _lastDiscoveryUtc).TotalSeconds < 1.0) return null;
                _lastDiscoveryUtc = DateTime.UtcNow;

                foreach (var name in new[] { "Bannerlord", "Bannerlord.Native", "Bannerlord_BE" })
                {
                    Process[] found;
                    try { found = Process.GetProcessesByName(name); } catch { continue; }
                    foreach (var p in found)
                    {
                        if (IsUsableGameProcess(p))
                        {
                            AttachProcess(p);
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
                                AttachProcess(p);
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

        private void AttachProcess(Process process)
        {
            if (process == null) return;
            _trackedProcess = process;
            ResetProcessCounters();
            CaptureProcessStart(process);
            _lastActivityUtc = DateTime.UtcNow;
            MonitorEvent("PROCESS_ATTACH", "pid=" + SafePid(process) + "; startUtc=" +
                (_processStartUtc.HasValue ? _processStartUtc.Value.ToString("o", CultureInfo.InvariantCulture) : "unknown"));
        }

        private void CaptureProcessStart(Process process)
        {
            if (_processStartUtc.HasValue || process == null) return;
            try { _processStartUtc = process.StartTime.ToUniversalTime(); } catch { }
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
                    if (wallMs > 0)
                        _cpuPercent = Math.Max(0, cpuMs / wallMs / Math.Max(1, Environment.ProcessorCount) * 100d);
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

                _currentRamGb = process.WorkingSet64 / 1024d / 1024d / 1024d;
                _peakRamGb = Math.Max(_peakRamGb, _currentRamGb);
                if (_fullBuildActive || _fullBuildPending)
                    _shaderBuildPeakRam = Math.Max(_shaderBuildPeakRam, _currentRamGb);

                if (_cpuPercent > 0.5 || _readRateMb > 0.05 || _writeRateMb > 0.05)
                {
                    _lastActivityUtc = now;
                    _activeSamples++;
                }

                UpdateResourceLabels(process, _currentRamGb);
            }
            catch
            {
                _resources.Text = "Процесс обнаружен, но часть Windows counters недоступна. PID=" + SafePid(process);
            }
        }

        private void UpdateResourceLabels(Process process, double? ramGb = null)
        {
            var ram = ramGb ?? 0;
            var ramLabel = ram >= 32 ? "КРИТИЧЕСКАЯ RAM" : ram >= 24 ? "высокая RAM" : "RAM";
            _resources.Text = string.Format(CultureInfo.InvariantCulture,
                "PID {0} · CPU {1:0.0}% · {2} {3:0.0} GB · I/O R {4:0.0} / W {5:0.0} MB/s\r\n" +
                "read {6:0} MB · write {7:0} MB · session peak {8:0.0} GB · {9}",
                process != null ? SafePid(process) : 0,
                _cpuPercent, ramLabel, ram, _readRateMb, _writeRateMb,
                _readSinceAttachMb, _writeSinceAttachMb, _peakRamGb, CacheText());
        }

        private string EvidenceSummary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "CPU {0:0.0}% · I/O R {1:0.0}/W {2:0.0} MB/s · RAM {3:0.0} GB · peak {4:0.0} GB.",
                _cpuPercent, _readRateMb, _writeRateMb, _currentRamGb, _peakRamGb);
        }

        private void LogRamSampleIfDue()
        {
            var now = DateTime.UtcNow;
            var interval = (_fullBuildActive || _fullBuildPending) ? 5d : 15d;
            var peakJump = _peakRamGb - _lastLoggedPeakRamGb >= 0.5;
            if (!peakJump && (now - _lastRamLogUtc).TotalSeconds < interval) return;

            _lastRamLogUtc = now;
            _lastLoggedPeakRamGb = _peakRamGb;
            MonitorEvent("RAM_SAMPLE",
                "ramGB=" + F(_currentRamGb) + "; peakGB=" + F(_peakRamGb) +
                "; shaderBuild=" + (_fullBuildActive || _fullBuildPending) +
                "; rglStage=" + _rglStage);
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
            if (_cache.Utc == default(DateTime)) return "shader cache: ждём background scan";
            return "shader cache " + FormatBytes(_cache.Bytes) + " / " + _cache.Files + " файлов";
        }

        private void EnsureRglPath(Process process)
        {
            if (process == null) return;
            var pid = SafePid(process);
            if (pid <= 0) return;

            try
            {
                var logsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Mount and Blade II Bannerlord", "logs");
                if (!Directory.Exists(logsRoot)) return;

                var exact = Path.Combine(logsRoot, "rgl_log_" + pid + ".txt");
                if (File.Exists(exact))
                {
                    SwitchRglPath(exact);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_rglPath) && File.Exists(_rglPath)) return;

                var threshold = (_processStartUtc ?? DateTime.UtcNow).AddMinutes(-2);
                var candidate = Directory.EnumerateFiles(logsRoot, "rgl_log_*.txt", SearchOption.TopDirectoryOnly)
                    .Select(x => new FileInfo(x))
                    .Where(x => x.Exists && x.LastWriteTimeUtc >= threshold)
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate != null) SwitchRglPath(candidate.FullName);
            }
            catch { }
        }

        private void SwitchRglPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (string.Equals(_rglPath, path, StringComparison.OrdinalIgnoreCase)) return;
            _rglPath = path;
            _rglOffset = 0;
            _rglStage = "RGL подключён";
            _rglLastLine = string.Empty;
            MonitorEvent("RGL_ATTACH", "path=" + path);
        }

        private void RequestRglPoll()
        {
            if (_rglPollRunning || string.IsNullOrWhiteSpace(_rglPath) || !File.Exists(_rglPath)) return;
            var now = DateTime.UtcNow;
            if ((now - _lastRglPollRequestUtc).TotalMilliseconds < 500) return;
            _lastRglPollRequestUtc = now;
            _rglPollRunning = true;
            var path = _rglPath;
            var offset = _rglOffset;

            Task.Run(() => PollRgl(path, offset)).ContinueWith(task => SafeUi(() =>
            {
                _rglPollRunning = false;
                if (task.IsCanceled || task.IsFaulted || task.Result == null) return;
                ApplyRglPoll(task.Result);
            }));
        }

        private static RglPollResult PollRgl(string path, long offset)
        {
            var result = new RglPollResult { Path = path, StartOffset = Math.Max(0, offset) };
            try
            {
                var info = new FileInfo(path);
                result.FileLength = info.Exists ? info.Length : 0;
                result.LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                if (!info.Exists) return result;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (result.StartOffset > stream.Length) result.StartOffset = 0;
                    stream.Seek(result.StartOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                            result.Lines.Add(line);
                        result.NewOffset = stream.Position;
                    }
                }
            }
            catch { }
            return result;
        }

        private void ApplyRglPoll(RglPollResult poll)
        {
            if (poll == null || !string.Equals(poll.Path, _rglPath, StringComparison.OrdinalIgnoreCase)) return;
            if (poll.NewOffset < 0) return;

            var bytes = Math.Max(0, poll.NewOffset - poll.StartOffset);
            _rglOffset = poll.NewOffset;
            _rglLastWriteUtc = poll.LastWriteUtc == DateTime.MinValue ? (DateTime?)null : poll.LastWriteUtc;
            if (bytes > 0 || poll.Lines.Count > 0)
            {
                _rglBytesRead += bytes;
                _rglLinesRead += poll.Lines.Count;
                _rglLastGrowthUtc = DateTime.UtcNow;
                _lastActivityUtc = DateTime.UtcNow;
            }

            foreach (var line in poll.Lines)
            {
                var signal = RglTelemetry.Analyze(line);
                if (signal.Significant)
                {
                    _rglStage = signal.Stage;
                    _rglLastLine = Shorten(line, 170);
                }
                if (!string.IsNullOrWhiteSpace(signal.ShaderKind))
                {
                    _runtimeShaderKind = signal.ShaderKind;
                    _runtimeShaderKindUtc = DateTime.UtcNow;
                }
                if (signal.EngineLoadingSeconds.HasValue)
                    _engineLoadingSeconds = signal.EngineLoadingSeconds;
            }
        }

        private void RenderRglStatus()
        {
            var name = string.IsNullOrWhiteSpace(_rglPath) ? "не подключён" : Path.GetFileName(_rglPath);
            var age = _rglLastGrowthUtc.HasValue ? (DateTime.UtcNow - _rglLastGrowthUtc.Value).TotalSeconds : double.NaN;
            var ageText = double.IsNaN(age) ? "нет данных" : (age < 2 ? "сейчас" : ((int)age + "с назад"));
            _rgl.Text = "RGL: " + name + " · стадия: " + _rglStage + " · рост: " + ageText +
                        " · прочитано " + FormatBytes(_rglBytesRead) + " / " + _rglLinesRead + " строк" + Environment.NewLine +
                        (string.IsNullOrWhiteSpace(_rglLastLine) ? "Последнее значимое событие RGL: пока нет" : "RGL: " + _rglLastLine);
        }

        private void RenderActivity(Process process)
        {
            var processAge = Math.Max(0, (DateTime.UtcNow - _lastActivityUtc).TotalSeconds);
            var rglAge = _rglLastGrowthUtc.HasValue
                ? Math.Max(0, (DateTime.UtcNow - _rglLastGrowthUtc.Value).TotalSeconds)
                : double.MaxValue;
            var activeNow = _cpuPercent > 0.5 || _readRateMb > 0.05 || _writeRateMb > 0.05 || rglAge < 2;
            var possibleStall = process != null && !_ready && !activeNow && processAge > 20 && rglAge > 20;

            var state = possibleStall
                ? "НИЗКАЯ АКТИВНОСТЬ >20с — возможен stall; сначала смотрим CPU/I/O/RGL, игру сразу не закрываем."
                : activeNow
                    ? "Bannerlord активно работает — это не зависание."
                    : "Процесс жив; сейчас низкая активность. Monitor heartbeat продолжает обновляться.";

            _activity.Text = state + Environment.NewLine +
                "Последняя подтверждённая активность: " + (processAge < 2 ? "сейчас" : ((int)processAge + "с назад")) +
                " · active samples=" + _activeSamples + " · " + _cleaveStatus;
        }

        private void ReadStabilityEvents()
        {
            if (!File.Exists(StabilityLog)) return;
            try
            {
                using (var stream = new FileStream(StabilityLog, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
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
            if (parts.Length < 2 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out timestamp)) return;
            timestamp = timestamp.ToUniversalTime();
            if (timestamp < _monitorStartedUtc.AddSeconds(-3)) return;

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
                _rosterSummary = message;
                _fullBuildPending = true;
                _fullBuildSession = true;
                _fullBuildActive = false;
                _fullBuildLastCompleteUtc = null;
                _shaderBuildRamBefore = _currentRamGb;
                _shaderBuildPeakRam = _currentRamGb;
                _shaderBuildRamAfter = _currentRamGb;
                MonitorEvent("SHADER_BUILD_ARMED",
                    "ramBeforeGB=" + F(_shaderBuildRamBefore) + "; " + message);
            }
            else if (name == "SHADER_START")
            {
                if (_fullBuildPending || IsWithinFullBuildGrace(timestamp))
                {
                    _fullBuildPending = false;
                    _fullBuildSession = true;
                    _fullBuildActive = true;
                    _shaderMode = "full-cache";
                    _shaderBuildPeakRam = Math.Max(_shaderBuildPeakRam, _currentRamGb);
                }
                else
                {
                    _shaderMode = "runtime";
                }

                var remaining = ParseIntField(message, "remaining=");
                if (remaining >= 0) AddShaderSample(timestamp, remaining);
            }
            else if (name == "SHADER_PROGRESS")
            {
                var remaining = ParseIntField(message, "remaining=");
                if (remaining >= 0) AddShaderSample(timestamp, remaining);
            }
            else if (name == "SHADER_COMPLETE")
            {
                _shaderRemaining = 0;
                _shaderSamples.Clear();
                if (_fullBuildActive)
                {
                    _fullBuildActive = false;
                    _fullBuildLastCompleteUtc = timestamp;
                    _shaderBuildRamAfter = _currentRamGb;
                }
            }
        }

        private bool IsWithinFullBuildGrace(DateTime timestamp)
        {
            return _fullBuildSession && _fullBuildLastCompleteUtc.HasValue &&
                   (timestamp - _fullBuildLastCompleteUtc.Value).TotalSeconds >= 0 &&
                   (timestamp - _fullBuildLastCompleteUtc.Value).TotalSeconds <= 30;
        }

        private void FinalizeFullBuildSummary(bool force)
        {
            if (!_fullBuildSession || _fullBuildPending || _fullBuildActive) return;
            if (!_fullBuildLastCompleteUtc.HasValue) return;
            if (!force && (DateTime.UtcNow - _fullBuildLastCompleteUtc.Value).TotalSeconds < 30) return;

            _shaderBuildRamAfter = _currentRamGb;
            var message = "beforeGB=" + F(_shaderBuildRamBefore) +
                          "; peakGB=" + F(_shaderBuildPeakRam) +
                          "; afterGB=" + F(_shaderBuildRamAfter) +
                          "; sessionPeakGB=" + F(_peakRamGb) +
                          "; roster=" + _rosterSummary;
            MonitorEvent("SHADER_BUILD_RAM", message);
            ExternalEvent("SHADER_BUILD_RAM", "monitor=0.4.4; " + message);
            _fullBuildSession = false;
        }

        private void ReadCleaveEvents()
        {
            var path = Path.Combine(_gameRoot, "Modules", "KaiCleave", "KaiCleave.log");
            if (!File.Exists(path)) return;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
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
                            {
                                _cleaveStatus = "KaiCleave: " + Shorten(line, 155);
                                if (line.IndexOf("CLEAVE_TIMING", StringComparison.OrdinalIgnoreCase) >= 0)
                                    MonitorEvent("CLEAVE_TIMING_SEEN", Shorten(line, 240));
                            }
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

        private void UpdateStartupEta()
        {
            if (!_processStartUtc.HasValue)
            {
                _eta.Text = "ETA: ждём Process.StartTime";
                return;
            }
            if (!_historicalProcessToMenuSeconds.HasValue)
            {
                _eta.Text = "ETA: калибровка process→menu";
                return;
            }
            var elapsed = (DateTime.UtcNow - _processStartUtc.Value).TotalSeconds;
            var remaining = Math.Max(0, _historicalProcessToMenuSeconds.Value - elapsed);
            _eta.Text = "ETA process→menu: ~" + FormatDuration(TimeSpan.FromSeconds(remaining));
        }

        private double? GetWaitToProcessSeconds()
        {
            if (!_processStartUtc.HasValue) return null;
            return Math.Max(0, (_processStartUtc.Value - _monitorStartedUtc).TotalSeconds);
        }

        private double? GetProcessToMenuSeconds()
        {
            if (_engineLoadingSeconds.HasValue && _engineLoadingSeconds.Value > 0)
                return _engineLoadingSeconds;
            if (_processStartUtc.HasValue && _readyUtc.HasValue)
                return Math.Max(0, (_readyUtc.Value - _processStartUtc.Value).TotalSeconds);
            return null;
        }

        private double? GetModuleToMenuSeconds()
        {
            if (_moduleLoadedUtc.HasValue && _readyUtc.HasValue)
                return Math.Max(0, (_readyUtc.Value - _moduleLoadedUtc.Value).TotalSeconds);
            return null;
        }

        private static double? LoadMedianProcessHistory()
        {
            try
            {
                if (!File.Exists(HistoryV2File)) return null;
                var values = new List<double>();
                foreach (var line in File.ReadAllLines(HistoryV2File))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("timestampUtc", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    double seconds;
                    if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds > 1)
                        values.Add(seconds);
                }
                if (values.Count == 0) return null;
                var recent = values.Skip(Math.Max(0, values.Count - 8)).OrderBy(x => x).ToArray();
                return recent[recent.Length / 2];
            }
            catch { return null; }
        }

        private static void SaveStructuredHistory(double? waitToProcess, double? processToMenu,
            double? moduleToMenu, double? engineLoading, double peakRamGb)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                if (!File.Exists(HistoryV2File))
                    File.AppendAllText(HistoryV2File,
                        "timestampUtc,waitToProcessSec,processToMenuSec,moduleToMenuSec,engineLoadingSec,peakRamGB" + Environment.NewLine);
                File.AppendAllText(HistoryV2File,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "," +
                    F(waitToProcess) + "," + F(processToMenu) + "," + F(moduleToMenu) + "," +
                    F(engineLoading) + "," + F(peakRamGb) + Environment.NewLine);
            }
            catch { }
        }

        private void WriteSessionSummary(string reason)
        {
            MonitorEvent("SESSION_SUMMARY",
                "reason=" + reason +
                "; waitToProcessSec=" + F(GetWaitToProcessSeconds()) +
                "; processToMenuSec=" + F(GetProcessToMenuSeconds()) +
                "; moduleToMenuSec=" + F(GetModuleToMenuSeconds()) +
                "; engineLoadingSec=" + F(_engineLoadingSeconds) +
                "; currentRamGB=" + F(_currentRamGb) +
                "; peakRamGB=" + F(_peakRamGb) +
                "; rglLines=" + _rglLinesRead +
                "; rglBytes=" + _rglBytesRead +
                "; optimizationActive=" + _optimizationActive);
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
            _progressText.Text = "Точного процента нет — показываем реальные process / RGL / RAM / I/O признаки";
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
            return end > index && int.TryParse(message.Substring(index, end - index), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : -1;
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

        private static string FormatSeconds(double? seconds)
        {
            return seconds.HasValue ? seconds.Value.ToString("0.###", CultureInfo.InvariantCulture) + "с" : "n/a";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
            return text.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static void MonitorEvent(string eventName, string message)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                File.AppendAllText(MonitorLog,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" + eventName + "|" +
                    (message ?? string.Empty) + Environment.NewLine);
            }
            catch { }
        }

        private static void ExternalEvent(string eventName, string message)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                File.AppendAllText(StabilityLog,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" + eventName + "|" +
                    (message ?? string.Empty) + Environment.NewLine);
            }
            catch { }
        }
    }
}
