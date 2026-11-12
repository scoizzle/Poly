using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.Compile;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Lowering;

using TUnit.Core;

namespace Poly.Tests.TestHelpers;

/// <summary>Knobs for one boot. The defaults are the production budgets; every override here exists for a
/// harness test that must trip a bound or a guard deterministically (the slot wait and the HTTP timeout are
/// fixed budgets, not knobs).</summary>
public sealed class PackOracleBootOptions {
    /// <summary>How long to wait for "Now listening on".</summary>
    public TimeSpan Readiness { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The child's own lifetime cap, enforced by the startup hook.</summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Test only: the hook blocks <c>Initialize</c> after starting its watchdog, so the child never listens.</summary>
    public bool StallHook { get; init; }

    /// <summary>False returns right after launch, without waiting for the listening line.</summary>
    public bool WaitForReady { get; init; } = true;
}

/// <summary>A boot that did not reach a listening host. The child, if one started, is already dead.</summary>
public sealed class PackOracleBootException(
    string message, int? childPid, string? runDirectory, string errorTail, Exception? inner = null)
    : Exception(message, inner) {
    public int? ChildPid { get; } = childPid;
    public string? RunDirectory { get; } = runDirectory;
    public string ErrorTail { get; } = errorTail;
}

/// <summary>One emitted, cached app: a directory holding app.dll, its dependency closure, the
/// native sqlite library and a runtimeconfig.</summary>
public sealed record CompiledApp(string Directory, string AppDll, string DomainName, string Key);

/// <summary>
/// The shared executable oracle for the persistence packs (#687, slice P-01). It compiles a fixture
/// exactly as polyc does (<see cref="PolyCompiler"/> over <see cref="ProductCatalog.Catalog"/>), emits
/// the generated host with Roslyn, and boots it as a bounded child process on a file SQLite database in a
/// fresh run directory. A child process, because the generated Program.cs hardcodes
/// <c>UseSqlite("Data Source=&lt;domain&gt;.db")</c> and ends in <c>app.Run()</c>: in-process, every app
/// would share one file and none could be stopped.
///
/// Bounds, all of them because a spawn-heavy harness froze the machine once:
/// at most two live children; one gated polyc compile (30 s) and one gated Roslyn emit (30 s); a 25 s slot
/// wait, 30 s readiness, 10 s kill wait and 10 s per-request HTTP budget. A worst-case boot is
/// 30 + 30 + 25 + 30 + 10 + 10 = 135 s. That number bounds one boot, not one test: it excludes the compile
/// gate wait (compiles serialise in-process), a cache-hit rebuild, child runtime startup and JIT, and the
/// first-use sweep. The 25 s slot wait is the shared <see cref="ChildProcessGate"/> budget, so a booted
/// oracle and a test-host child contend for the same two slots. 135 s sits inside the assembly's 180 s test
/// timeout with 45 s of margin; the tests that boot twice carry their own explicit <c>[Timeout]</c>.
/// </summary>
public sealed class PackOracle : IAsyncDisposable {
    private const string HookFileName = "PackOracleHook.dll";
    private const string LayoutVersion = "2";
    private static readonly TimeSpan _compileBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _slotWait = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _staleAfter = TimeSpan.FromDays(1);
    private static readonly string[] _reflectionLoadedSqlite = ["SQLitePCLRaw.batteries_v2", "SQLitePCLRaw.provider.e_sqlite3"];
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly Regex _listening = new(@"Now listening on: (http://\S+)", RegexOptions.Compiled);

    // Compile+emit is Roslyn-heavy: one at a time per process; the temp+move cache makes it safe across processes.
    private static readonly SemaphoreSlim _compileGate = new(1, 1);
    // At most two live children, for the whole test process: the gate PolycProcess shares.
    private static readonly SemaphoreSlim _childSlots = ChildProcessGate.Slots;
    private static readonly ConcurrentDictionary<string, Lazy<Task<CompiledApp>>> _apps = new();
    private static readonly ConcurrentDictionary<PackOracle, byte> _live = new();
    private static readonly ConcurrentBag<int> _recordedChildPids = [];
    private static readonly AsyncLocal<string?> _launcherOverride = new();
    private static readonly Lazy<bool> _initialised = new(Initialise);
    private static readonly Lazy<string> _hookPath = new(EmitHook);
    private static readonly Lazy<IReadOnlyList<ReferenceFile>> _references = new(LoadReferences);

    private readonly Process _process;
    private readonly RingBuffer _stdout;
    private readonly RingBuffer _stderr;
    private readonly string _pidFilePath;
    private readonly string _runDirectory;
    private int _disposed;

    private PackOracle(
        Process process, HttpClient client, RingBuffer stdout, RingBuffer stderr, string runDirectory, string pidFilePath) {
        _process = process;
        Client = client;
        _stdout = stdout;
        _stderr = stderr;
        _runDirectory = runDirectory;
        _pidFilePath = pidFilePath;
        ChildPid = process.Id;
    }

    /// <summary>An HttpClient on the child's base address (no base address when the boot did not wait for readiness).</summary>
    public HttpClient Client { get; }

    public Uri? BaseAddress => Client.BaseAddress;

    /// <summary>The tail of the child's stderr (200 lines or 32 KB), streamed for the child's whole life.</summary>
    public string ErrorTail => _stderr.Tail();

    public int ChildPid { get; }

    /// <summary>The child's exit code once it has exited, or null while it runs (or if it never started).</summary>
    public int? ChildExitCode {
        get {
            try {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException) {
                return null;
            }
        }
    }

    /// <summary>The child's SQLite file in its run directory, once the host has created it.</summary>
    public string? DatabasePath =>
        Directory.Exists(_runDirectory) ? Directory.EnumerateFiles(_runDirectory, "*.db").FirstOrDefault() : null;

    public string RunDirectory => _runDirectory;

    public string PidFilePath => _pidFilePath;

    /// <summary>Every child pid this process ever started, for the "no survivor" check.</summary>
    public static IReadOnlyCollection<int> RecordedChildPids => _recordedChildPids;

    private static string Root => Path.Combine(Path.GetTempPath(), "poly-oracle");

    private static string AppRoot => Path.Combine(Root, "app");

    private static string HookRoot => Path.Combine(Root, "hook");

    private static string WorkRoot => Path.Combine(Root, "work");

    private static string PidRoot => Path.Combine(Root, "run");

    // ───────────────────────────── boot ─────────────────────────────

    /// <summary>Compiles <paramref name="fixture"/>, emits and caches the host, boots it and returns a handle.
    /// Dispose the handle: it kills the child and deletes its run directory.</summary>
    public static async Task<PackOracle> BootAsync(
        PackOracleDomain fixture, string provider = "sqlite", PackOracleBootOptions? options = null) {
        ArgumentNullException.ThrowIfNull(fixture);
        PolycProcess.GuardDepth("PackOracle");
        options ??= new PackOracleBootOptions();
        if (!string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"PackOracle boots 'sqlite' only; '{provider}' arrives with its own slice.");
        if (!fixture.Boots)
            throw new InvalidOperationException($"Fixture '{fixture.Name}' does not boot: {fixture.NotBootingReason}");

        var app = await CompileAsync(fixture);
        var hook = _hookPath.Value;

        var testToken = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;
        using (var slotWait = CancellationTokenSource.CreateLinkedTokenSource(testToken)) {
            slotWait.CancelAfter(_slotWait);
            try {
                await _childSlots.WaitAsync(slotWait.Token);
            }
            catch (OperationCanceledException) when (!testToken.IsCancellationRequested) {
                throw new TimeoutException(
                    $"No child slot free within {_slotWait.TotalSeconds:0} s (at most 2 live children).");
            }
        }

        var slotHeld = true;
        var started = false;
        string? runDirectory = null;
        string? pidFile = null;
        Process? process = null;
        var stdout = new RingBuffer();
        var stderr = new RingBuffer();
        try {
            runDirectory = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);

            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var crashed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process = new Process { StartInfo = LaunchInfo(app, runDirectory, hook, options), EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => {
                if (e.Data is null) {
                    crashed.TrySetResult(true);
                    return;
                }
                stdout.Add(e.Data);
                var match = _listening.Match(e.Data);
                if (match.Success)
                    ready.TrySetResult(match.Groups[1].Value);
            };
            process.ErrorDataReceived += (_, e) => {
                if (e.Data is not null)
                    stderr.Add(e.Data);
            };
            process.Exited += (_, _) => crashed.TrySetResult(true);

            var launchedUtc = DateTime.UtcNow;
            process.Start();
            started = true;
            _recordedChildPids.Add(process.Id);
            // Drain both pipes before any file work: a chatty child must never block on a full pipe while
            // the harness reads StartTime and writes the pidfile.
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            pidFile = WritePidFile(process, runDirectory, app.Directory, launchedUtc);

            if (!options.WaitForReady) {
                var detached = new PackOracle(process, NewClient(null), stdout, stderr, runDirectory, pidFile);
                _live[detached] = 0;
                slotHeld = false;
                return detached;
            }

            await Task.WhenAny(ready.Task, crashed.Task).WaitAsync(options.Readiness);
            if (!ready.Task.IsCompleted) {
                await KillAndWaitAsync(process);
                throw new PackOracleBootException(
                    $"'{app.DomainName}' exited before listening (exit {SafeExitCode(process)}).\n{Diagnostics(stdout, stderr)}",
                    process.Id, runDirectory, stderr.Tail());
            }

            var oracle = new PackOracle(process, NewClient(new Uri(ready.Task.Result)), stdout, stderr, runDirectory, pidFile);
            _live[oracle] = 0;
            slotHeld = false;
            return oracle;
        }
        catch (TimeoutException ex) when (started) {
            await KillAndWaitAsync(process!);
            throw new PackOracleBootException(
                $"'{app.DomainName}' did not listen within {options.Readiness.TotalSeconds:0.#} s; child killed.\n{Diagnostics(stdout, stderr)}",
                process!.Id, runDirectory, stderr.Tail(), ex);
        }
        finally {
            if (slotHeld) {
                // `started` guards HasExited: a Process that failed to Start throws there and would mask the
                // real error. Release sits in the innermost finally so a failed kill or cleanup can never
                // leak the slot: a leak caps the whole process at one live child for the rest of the run.
                try {
                    if (started && !SafeHasExited(process!))
                        await KillAndWaitAsync(process!);
                }
                finally {
                    try {
                        process?.Dispose();
                        Cleanup(runDirectory, pidFile);
                    }
                    finally {
                        _childSlots.Release();
                    }
                }
            }
        }
    }

    private static HttpClient NewClient(Uri? baseAddress) {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(1) };
        var client = new HttpClient(handler) { Timeout = _httpTimeout };
        if (baseAddress is not null)
            client.BaseAddress = baseAddress;
        return client;
    }

    private static ProcessStartInfo LaunchInfo(CompiledApp app, string runDirectory, string hook, PackOracleBootOptions options) {
        var info = new ProcessStartInfo(LauncherPathOverride ?? LauncherPath()) {
            WorkingDirectory = runDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(app.AppDll);

        // The parent's environment minus everything that steers the runtime, then exactly what the plan sets.
        var inherited = info.Environment.Keys.Where(IsRuntimeSteering).ToList();
        foreach (var key in inherited)
            info.Environment.Remove(key);
        info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["Logging__LogLevel__Default"] = "Warning";
        info.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        info.Environment["DOTNET_gcServer"] = "0";
        info.Environment["DOTNET_GCHeapHardLimit"] = "0x40000000";
        info.Environment["DOTNET_STARTUP_HOOKS"] = hook;
        info.Environment["POLY_ORACLE_DEADLINE_MS"] = ((long)options.Deadline.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (options.StallHook)
            info.Environment["POLY_ORACLE_TEST_STALL"] = "1";
        return info;
    }

    /// <summary>The same scrub as <see cref="PolycProcess.Children"/>: nothing that steers the runtime or the
    /// harness crosses into the app, so an ambient <c>POLY_CHILD_DEPTH</c> or GC knob cannot change its behaviour.
    /// The oracle sets its own <c>POLY_ORACLE_*</c> after the scrub.</summary>
    private static bool IsRuntimeSteering(string key) =>
        key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("POLY_", StringComparison.OrdinalIgnoreCase);

    /// <summary>DOTNET_HOST_PATH when set, else the dotnet next to the running runtime, else <c>dotnet</c> on PATH.</summary>
    public static string LauncherPath() {
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
            return fromEnv;
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        // <root>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var root = runtimeDir is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)));
        var beside = root is null ? null : Path.Combine(root, exe);
        return beside is not null && File.Exists(beside) ? beside : exe;
    }

    /// <summary>Test only: overrides the launcher for this asynchronous flow, so one boot can be made to fail
    /// at <c>Process.Start</c> without touching the machine or any test that runs beside it.</summary>
    internal static string? LauncherPathOverride {
        get => _launcherOverride.Value;
        set => _launcherOverride.Value = value;
    }

    /// <summary>Test only: the directory the pidfile sweeps read, so a test can write a record and drive a sweep.</summary>
    internal static string PidDirectoryForTests => PidRoot;

    private static string Diagnostics(RingBuffer stdout, RingBuffer stderr) =>
        $"--- child stderr tail ---\n{stderr.Tail()}\n--- child stdout tail ---\n{stdout.Tail()}";

    private static int SafeExitCode(Process process) {
        try {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException) {
            return -1;
        }
    }

    // ───────────────────────────── child lifetime ─────────────────────────────

    /// <summary>Closes the write end of the child's stdin. To the startup hook that is a dead parent.</summary>
    public void CloseChildStdin() => _process.StandardInput.Close();

    /// <summary>True when the child has exited within <paramref name="timeout"/>.</summary>
    public async Task<bool> WaitForChildExitAsync(TimeSpan timeout) {
        try {
            await _process.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException) {
            return false;
        }
    }

    /// <summary>kill -0 semantics: does a process with this pid exist and run? A process that exists but
    /// cannot be inspected is treated as alive, because the sweep's next move is a kill.</summary>
    public static bool IsProcessAlive(int pid) {
        try {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
        catch (System.ComponentModel.Win32Exception) {
            return true;
        }
        catch (UnauthorizedAccessException) {
            return true;
        }
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _live.TryRemove(this, out _);
        try {
            Client.Dispose();
            await KillAndWaitAsync(_process);
        }
        finally {
            _process.Dispose();
            Cleanup(_runDirectory, _pidFilePath);
            _childSlots.Release();
        }
    }

    private static async Task KillAndWaitAsync(Process process) {
        try {
            if (!SafeHasExited(process))
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) {
            // Already gone.
        }
        catch (System.ComponentModel.Win32Exception) {
            // Already gone, or not ours to signal.
        }
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) {
            // Reported by the survivor check; nothing more a harness can do here.
        }
        catch (InvalidOperationException) {
            // Never started or already disposed.
        }
    }

    private static bool SafeHasExited(Process process) {
        try {
            return process.HasExited;
        }
        catch (InvalidOperationException) {
            return true; // never started, or already disposed: nothing to kill
        }
    }

    private static void Cleanup(string? runDirectory, string? pidFile) {
        TryDeleteDirectory(runDirectory);
        if (pidFile is not null)
            TryDeleteFile(pidFile);
    }

    private static void TryDeleteDirectory(string? path) {
        if (path is null)
            return;
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) {
            // A sqlite file may still be held for an instant; the age sweep collects it.
        }
        catch (UnauthorizedAccessException) {
        }
    }

    private static void TryDeleteFile(string path) {
        try {
            File.Delete(path);
        }
        catch (IOException) {
        }
        catch (UnauthorizedAccessException) {
        }
    }

    // ───────────────────────────── pidfiles and sweeps ─────────────────────────────

    private sealed record PidRecord(int HostPid, int ChildPid, DateTime StartedUtc, string RunDir, string? AppDir);

    /// <summary>Writes the record after the child is running. <paramref name="launchedUtc"/> is the parent's
    /// clock at <c>Start</c>, used when the child died before its own start time could be read.</summary>
    private static string WritePidFile(Process child, string runDirectory, string appDirectory, DateTime launchedUtc) {
        DateTime started;
        try {
            started = child.StartTime.ToUniversalTime();
        }
        catch (InvalidOperationException) {
            // A child that exited this fast has no readable start time; the parent's clock is within seconds,
            // which is all the sweep's 5 s match window needs.
            started = launchedUtc;
        }
        Directory.CreateDirectory(PidRoot);
        var path = Path.Combine(PidRoot, Guid.NewGuid().ToString("N") + ".json");
        var record = new PidRecord(Environment.ProcessId, child.Id, started, runDirectory, appDirectory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, _json));
        File.Move(temp, path);
        return path;
    }

    /// <summary>A record read that could not happen is not evidence the record is corrupt: an I/O or permission
    /// wall skips the record, because it may still name a live child. Only a corrupt or empty document is droppable.</summary>
    private readonly record struct PidRead(PidRecord? Record, bool Unreadable) {
        public static PidRead UnreadableFile => new(null, Unreadable: true);
        public static PidRead Droppable => new(null, Unreadable: false);
    }

    private static PidRead ReadPidFile(string path) {
        string text;
        try {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return PidRead.UnreadableFile;
        }
        if (string.IsNullOrWhiteSpace(text))
            return PidRead.Droppable;
        try {
            var record = JsonSerializer.Deserialize<PidRecord>(text, _json);
            return record is null ? PidRead.Droppable : new PidRead(record, Unreadable: false);
        }
        catch (JsonException) {
            return PidRead.Droppable;
        }
    }

    /// <summary>
    /// For every pidfile whose host process is dead: kill its child, but only when that pid is still the
    /// process the record names (start time within seconds, name <c>dotnet</c>, command line under
    /// <c>poly-oracle</c>), otherwise only delete the record; then delete its run directory.
    /// A corrupt or empty record cannot name a live child, so it is dropped at once. A record that cannot be
    /// read at all (an I/O or permission wall) is skipped and kept: the wall is not evidence of corruption, and
    /// the record may still name a live child. Returns how many children were killed. A live host's record is
    /// never touched, and one bad record never aborts the sweep.
    /// </summary>
    public static int SweepOrphans() {
        if (!Directory.Exists(PidRoot))
            return 0;
        var killed = 0;
        List<string> files;
        try {
            files = Directory.EnumerateFiles(PidRoot, "*.json").ToList();
        }
        catch (Exception ex) when (IsPidStoreFailure(ex)) {
            return 0;
        }
        foreach (var file in files) {
            try {
                var read = ReadPidFile(file);
                if (read.Unreadable)
                    continue;
                if (read.Record is null) {
                    TryDeleteFile(file);
                    continue;
                }
                var record = read.Record;
                if (IsProcessAlive(record.HostPid))
                    continue;
                if (ChildStillMatches(record)) {
                    try {
                        using var child = Process.GetProcessById(record.ChildPid);
                        child.Kill(entireProcessTree: true);
                        child.WaitForExit(5000);
                        killed++;
                    }
                    catch (ArgumentException) {
                    }
                    catch (InvalidOperationException) {
                    }
                }
                TryDeleteDirectory(record.RunDir);
                TryDeleteFile(file);
            }
            catch (Exception ex) when (IsPidStoreFailure(ex)) {
                // One damaged record must never fault the sweep (nor the Lazy Initialise that runs it).
            }
        }
        return killed;
    }

    private static bool ChildStillMatches(PidRecord record) {
        try {
            using var process = Process.GetProcessById(record.ChildPid);
            if (process.HasExited)
                return false;
            if (Math.Abs((process.StartTime.ToUniversalTime() - record.StartedUtc).TotalSeconds) > 5)
                return false;
            if (!process.ProcessName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
                return false;
            // Windows has no cheap command-line read; there the record is dropped and the hook is the guard.
            var command = CommandLineOf(record.ChildPid);
            return command is not null && command.Contains("poly-oracle", StringComparison.Ordinal);
        }
        catch (ArgumentException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
        catch (System.ComponentModel.Win32Exception) {
            return false;
        }
    }

    /// <summary>The child's command line, or null when it cannot be read. The 5 s bound is real: a <c>ps</c>
    /// that does not answer is killed, and a timeout reads as "does not match" so the sweep never kills on it.</summary>
    private static string? CommandLineOf(int pid) {
        if (OperatingSystem.IsWindows())
            return null;
        try {
            var info = new ProcessStartInfo("ps") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("-p");
            info.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("command=");
            using var ps = Process.Start(info);
            if (ps is null)
                return null;
            var text = ps.StandardOutput.ReadToEndAsync();
            if (!ps.WaitForExit(5000)) {
                try {
                    ps.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) {
                }
                return null;
            }
            return text.GetAwaiter().GetResult();
        }
        catch (System.ComponentModel.Win32Exception) {
            return null;
        }
        catch (IOException) {
            return null;
        }
    }

    /// <summary>Removes cache, hook and work directories not used for a day, except any a live pidfile names.
    /// A disappearing directory or an unreadable record is skipped, never thrown.</summary>
    private static void AgeSweep() {
        var protectedDirs = new HashSet<string>(StringComparer.Ordinal);
        List<string> records;
        try {
            records = Directory.Exists(PidRoot) ? Directory.EnumerateFiles(PidRoot, "*.json").ToList() : [];
        }
        catch (Exception ex) when (IsPidStoreFailure(ex)) {
            records = [];
        }
        foreach (var file in records) {
            PidRead read;
            try {
                read = ReadPidFile(file);
            }
            catch (Exception ex) when (IsPidStoreFailure(ex)) {
                continue; // an unreadable record protects nothing it cannot name
            }
            if (read.Unreadable || read.Record is null)
                continue;
            var record = read.Record;
            protectedDirs.Add(record.RunDir);
            if (record.AppDir is not null)
                protectedDirs.Add(record.AppDir);
        }
        foreach (var root in new[] { AppRoot, HookRoot, WorkRoot }) {
            List<string> dirs;
            try {
                if (!Directory.Exists(root))
                    continue;
                dirs = Directory.EnumerateDirectories(root).ToList();
            }
            catch (Exception ex) when (IsPidStoreFailure(ex)) {
                continue;
            }
            foreach (var dir in dirs) {
                try {
                    if (protectedDirs.Contains(dir))
                        continue;
                    if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow - _staleAfter)
                        TryDeleteDirectory(dir);
                }
                catch (Exception ex) when (IsPidStoreFailure(ex)) {
                }
            }
        }
    }

    /// <summary>True for the failures a pid store can raise on a machine we do not own: a corrupt record, a
    /// permission wall, or a process/ps race. None of them may fault the Lazy that boots every fixture.</summary>
    private static bool IsPidStoreFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException
            or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException;

    private static bool Initialise() {
        Directory.CreateDirectory(Root);
        // Lazy caches a thrown exception for the process's life: a corrupt record or an unreadable store must
        // degrade to "no sweep", never to "every boot in this run fails".
        try {
            SweepOrphans();
        }
        catch (Exception ex) when (IsPidStoreFailure(ex)) {
        }
        try {
            AgeSweep();
        }
        catch (Exception ex) when (IsPidStoreFailure(ex)) {
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            foreach (var oracle in _live.Keys) {
                try {
                    if (!oracle._process.HasExited)
                        oracle._process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) {
                }
                catch (System.ComponentModel.Win32Exception) {
                }
                Cleanup(oracle._runDirectory, oracle._pidFilePath);
            }
        };
        return true;
    }

    // ───────────────────────────── compile + emit + cache ─────────────────────────────

    /// <summary>Compiles <paramref name="fixture"/> exactly as polyc does and returns the cached app directory.
    /// polyc always runs; the cache key is the hash of the emitted sources, the dependency identities and the runtime.</summary>
    public static async Task<CompiledApp> CompileAsync(PackOracleDomain fixture) {
        ArgumentNullException.ThrowIfNull(fixture);
        _ = _initialised.Value;

        var (domainName, sources) = await RunGatedAsync(ct => Task.Run(() => PolyCompile(fixture, ct), ct));
        var key = ComputeKey(sources);
        Lazy<Task<CompiledApp>> Entry() => new(
            () => RunGatedAsync(ct => Task.Run(() => BuildApp(key, domainName, sources, ct), ct)));
        var lazy = _apps.GetOrAdd(key, _ => Entry());
        try {
            var app = await lazy.Value;
            if (CacheEntryBootable(app.Directory))
                return app;
        }
        catch {
            // A faulted or cancelled entry must not poison the next caller.
            _apps.TryRemove(new KeyValuePair<string, Lazy<Task<CompiledApp>>>(key, lazy));
            throw;
        }

        // A hit must be bootable, in memory and on disk: a damaged directory (or an entry that outlived one)
        // is rebuilt once through BuildApp, which verifies the layout before it publishes.
        _apps.TryRemove(new KeyValuePair<string, Lazy<Task<CompiledApp>>>(key, lazy));
        var rebuilt = _apps.GetOrAdd(key, _ => Entry());
        try {
            return await rebuilt.Value;
        }
        catch {
            _apps.TryRemove(new KeyValuePair<string, Lazy<Task<CompiledApp>>>(key, rebuilt));
            throw;
        }
    }

    /// <summary>True when a cached directory holds what a child needs to start.</summary>
    private static bool CacheEntryBootable(string directory) =>
        File.Exists(Path.Combine(directory, "app.dll"))
        && File.Exists(Path.Combine(directory, "app.runtimeconfig.json"));

    private static async Task<T> RunGatedAsync<T>(Func<CancellationToken, Task<T>> work) {
        using var cts = new CancellationTokenSource(_compileBudget);
        await _compileGate.WaitAsync(cts.Token);
        try {
            return await work(cts.Token);
        }
        finally {
            _compileGate.Release();
        }
    }

    private static (string DomainName, IReadOnlyList<(string FileName, string Source)> Sources) PolyCompile(
        PackOracleDomain fixture, CancellationToken cancel) {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(fixture.Poly, fixture.Name + ".poly", cancel);
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                $"polyc refused fixture '{fixture.Name}': "
                + string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        }
        var sources = result.Files
            .Where(f => f.FileName.EndsWith(".cs", StringComparison.Ordinal))
            .OrderBy(f => f.FileName, StringComparer.Ordinal)
            .ToList();
        if (!sources.Any(f => f.FileName == "Program.cs"))
            throw new InvalidOperationException($"Fixture '{fixture.Name}' emitted no Program.cs; there is no host to boot.");
        return (result.DomainName ?? fixture.Name, sources);
    }

    private static string ComputeKey(IReadOnlyList<(string FileName, string Source)> sources) {
        var text = new StringBuilder();
        // The app directory's contents are a function of this harness, not only of the emitted sources.
        // Bump LayoutVersion when one of these changes without a matching keyed input: AssembleDependencies
        // (the files it copies, or the closure rule), EmitApp or the implicit usings, the runtimeconfig
        // template, the native-sqlite discovery rule, or the published layout (temp dir + move).
        // The startup hook has its own hash in EmitHook.
        text.Append(LayoutVersion).Append('|').Append(string.Join(',', _reflectionLoadedSqlite)).Append('\n');
        text.Append(RuntimeInformation.FrameworkDescription).Append('|').Append(RuntimeInformation.RuntimeIdentifier).Append('\n');
        text.Append(ImplicitUsings).Append('\n');
        text.Append(RuntimeConfig()).Append('\n');
        text.Append(NativeSqliteFingerprint()).Append('\n');
        foreach (var reference in _references.Value.Where(r => r.InKey))
            text.Append(reference.Name).Append(':').Append(reference.Mvid).Append('\n');
        foreach (var (name, source) in sources)
            text.Append(name).Append('\n').Append(source).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant()[..32];
    }

    /// <summary>The native files that land in the app directory, by name and size: the key must change when
    /// the resolved e_sqlite3 does.</summary>
    private static string NativeSqliteFingerprint() =>
        string.Join(',', FindNativeSqlite().Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}"));

    private static CompiledApp BuildApp(
        string key, string domainName, IReadOnlyList<(string FileName, string Source)> sources, CancellationToken cancel) {
        var final = Path.Combine(AppRoot, key);
        var appDll = Path.Combine(final, "app.dll");
        if (Directory.Exists(final)) {
            if (CacheEntryBootable(final)) {
                Directory.SetLastWriteTimeUtc(final, DateTime.UtcNow);
                return new CompiledApp(final, appDll, domainName, key);
            }
            // A cache hit must be bootable: a half-published or damaged directory is rebuilt, never handed
            // to a child that cannot start.
            TryDeleteDirectory(final);
        }

        Directory.CreateDirectory(AppRoot);
        var temp = final + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temp);
        try {
            EmitApp(sources, Path.Combine(temp, "app.dll"), cancel);
            AssembleDependencies(temp);
            MoveIntoPlace(temp, final);
        }
        catch {
            TryDeleteDirectory(temp);
            throw;
        }
        return new CompiledApp(final, Path.Combine(final, "app.dll"), domainName, key);
    }

    /// <summary>Atomic publish: another process may have won; then the loser's temp directory is discarded.</summary>
    private static void MoveIntoPlace(string temp, string final) {
        try {
            Directory.Move(temp, final);
        }
        catch (IOException) when (Directory.Exists(final)) {
            TryDeleteDirectory(temp);
            Directory.SetLastWriteTimeUtc(final, DateTime.UtcNow);
        }
    }

    private const string ImplicitUsings = """
        global using System;
        global using System.Linq;
        global using System.Threading.Tasks;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Net.Http;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;

    private static void EmitApp(IReadOnlyList<(string FileName, string Source)> sources, string outputPath, CancellationToken cancel) {
        var trees = sources.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.FileName, cancellationToken: cancel)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(ImplicitUsings, path: "ImplicitUsings.cs", cancellationToken: cancel));
        var compilation = CSharpCompilation.Create(
            "app",
            trees,
            DslCompilerCompileOracleTests.GatherReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));
        using var stream = File.Create(outputPath);
        var emitted = compilation.Emit(stream, cancellationToken: cancel);
        if (!emitted.Success) {
            var errors = emitted.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Take(20)
                .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}");
            throw new InvalidOperationException("The generated host did not compile:\n" + string.Join("\n", errors));
        }
    }

    // ───────────────────────────── dependency closure ─────────────────────────────

    private sealed record ReferenceFile(string Name, string Path, Guid Mvid, bool IsFramework, bool InKey);

    private static string SharedFrameworkRoot() {
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return Path.GetDirectoryName(Path.GetDirectoryName(coreDir))!; // .../shared
    }

    private static IReadOnlyList<ReferenceFile> LoadReferences() {
        var shared = SharedFrameworkRoot() + Path.DirectorySeparatorChar;
        var self = typeof(PackOracle).Assembly.GetName().Name;
        var files = new List<ReferenceFile>();
        foreach (var reference in DslCompilerCompileOracleTests.GatherReferences().OfType<PortableExecutableReference>()) {
            var path = reference.FilePath;
            if (path is null)
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var framework = path.StartsWith(shared, StringComparison.Ordinal);
            // The key covers every non-framework dependency except the test assembly and its test framework:
            // those churn on every build and can never be part of the generated app. BuildApp enforces that
            // the closure stays inside the keyed set, so an omission here fails loudly, never silently stale.
            var inKey = !framework
                && !string.Equals(name, self, StringComparison.Ordinal)
                && !name.StartsWith("TUnit", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.Testing", StringComparison.Ordinal);
            files.Add(new ReferenceFile(name, path, framework ? Guid.Empty : MvidOf(path), framework, inKey));
        }
        return files.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    private static Guid MvidOf(string path) {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            return Guid.Empty; // a native or mixed-mode file has no MVID; it can never be a managed reference
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    private static IEnumerable<string> AssemblyRefNames(string path) {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences
            .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
            .ToList();
    }

    /// <summary>Copies the transitive AssemblyRef closure of <c>app.dll</c> (minus the shared frameworks),
    /// the native sqlite library and a runtimeconfig that names both frameworks.</summary>
    private static void AssembleDependencies(string appDirectory) {
        var byName = _references.Value.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var closure = new Dictionary<string, ReferenceFile>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(AssemblyRefNames(Path.Combine(appDirectory, "app.dll")));
        while (pending.Count > 0) {
            var name = pending.Dequeue();
            if (closure.ContainsKey(name) || !byName.TryGetValue(name, out var reference) || reference.IsFramework)
                continue;
            if (!reference.InKey) {
                throw new InvalidOperationException(
                    $"The generated host depends on '{name}', which the cache key excludes; remove it from the exclusion list in PackOracle.LoadReferences.");
            }
            closure[name] = reference;
            foreach (var next in AssemblyRefNames(reference.Path))
                pending.Enqueue(next);
        }
        if (closure.Count == 0)
            throw new InvalidOperationException("The generated host has no non-framework dependency; the closure walk found nothing (EF Core is expected).");

        // Found by the first boot (F1 falsified in part): Microsoft.Data.Sqlite loads these two by reflection,
        // so no AssemblyRef names them. They join the closure whenever SQLitePCLRaw.core does.
        if (closure.ContainsKey("SQLitePCLRaw.core")) {
            foreach (var name in _reflectionLoadedSqlite) {
                if (!byName.TryGetValue(name, out var reflected) || !reflected.InKey)
                    throw new InvalidOperationException($"'{name}' (loaded by reflection by Microsoft.Data.Sqlite) is missing from the test bin.");
                closure[name] = reflected;
            }
        }

        foreach (var reference in closure.Values)
            File.Copy(reference.Path, Path.Combine(appDirectory, Path.GetFileName(reference.Path)), overwrite: true);
        foreach (var native in FindNativeSqlite())
            File.Copy(native, Path.Combine(appDirectory, Path.GetFileName(native)), overwrite: true);

        File.WriteAllText(Path.Combine(appDirectory, "app.runtimeconfig.json"), RuntimeConfig());
    }

    private static string RuntimeConfig() {
        var runtime = $"{Environment.Version.Major}.{Environment.Version.Minor}.0";
        var coreDir = Path.GetFileName(Path.GetDirectoryName(typeof(object).Assembly.Location));
        var aspNetRoot = Path.Combine(SharedFrameworkRoot(), "Microsoft.AspNetCore.App");
        if (DslCompilerCompileOracleTests.ResolveSharedFrameworkVersion(aspNetRoot, coreDir) is null)
            throw new InvalidOperationException($"No Microsoft.AspNetCore.App shared framework under '{aspNetRoot}'; the generated host cannot run.");
        // The floor is major.minor.0: the host rolls forward to the machine's patch.
        return $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{Environment.Version.Major}}.{{Environment.Version.Minor}}",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "{{runtime}}" },
                  { "name": "Microsoft.AspNetCore.App", "version": "{{runtime}}" }
                ],
                "configProperties": {
                  "System.GC.Server": false
                }
              }
            }
            """;
    }

    private static string PortableRid() {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.Ordinal) ? "linux-musl"
            : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };
        return $"{os}-{arch}";
    }

    /// <summary>The e_sqlite3 native library for this machine. Windows has no <c>lib</c> prefix, so the glob is
    /// on the name fragment, and finding none is an error with the directories searched.</summary>
    private static IReadOnlyList<string> FindNativeSqlite() {
        var wanted = new[] { PortableRid(), RuntimeInformation.RuntimeIdentifier };
        var found = new List<string>();
        var runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        if (Directory.Exists(runtimes)) {
            foreach (var ridDir in Directory.EnumerateDirectories(runtimes)) {
                if (!wanted.Contains(Path.GetFileName(ridDir), StringComparer.OrdinalIgnoreCase))
                    continue;
                var native = Path.Combine(ridDir, "native");
                if (Directory.Exists(native))
                    found.AddRange(Directory.EnumerateFiles(native, "*e_sqlite3*"));
            }
        }
        if (found.Count == 0)
            found.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "*e_sqlite3*"));
        if (found.Count == 0) {
            throw new InvalidOperationException(
                $"No e_sqlite3 native library for '{string.Join("' or '", wanted)}' under '{runtimes}' or '{AppContext.BaseDirectory}'. "
                + "The generated host cannot open SQLite without it.");
        }
        return found;
    }

    // ───────────────────────────── startup hook ─────────────────────────────

    /// <summary>
    /// BCL-only source for the startup hook the child loads before <c>Main</c>. A background thread reads the
    /// child's stdin to EOF (the parent holds the pipe; EOF means the parent died) and another enforces the
    /// deadline; either one kills the process with <c>Process.Kill</c>, never <c>Environment.Exit</c>.
    /// </summary>
    private const string HookSource = """
        using System;
        using System.Diagnostics;
        using System.Threading;

        public class StartupHook
        {
            public static void Initialize()
            {
                var deadlineMs = 600000L;
                long parsed;
                if (long.TryParse(Environment.GetEnvironmentVariable("POLY_ORACLE_DEADLINE_MS"), out parsed) && parsed > 0)
                    deadlineMs = parsed;

                var stdin = new Thread(() =>
                {
                    try
                    {
                        var input = Console.OpenStandardInput();
                        var buffer = new byte[256];
                        while (input.Read(buffer, 0, buffer.Length) > 0) { }
                    }
                    catch (Exception) { }
                    Process.GetCurrentProcess().Kill();
                });
                stdin.IsBackground = true;
                stdin.Name = "poly-oracle-stdin";
                stdin.Start();

                var deadline = new Thread(() =>
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(deadlineMs));
                    Process.GetCurrentProcess().Kill();
                });
                deadline.IsBackground = true;
                deadline.Name = "poly-oracle-deadline";
                deadline.Start();

                if (Environment.GetEnvironmentVariable("POLY_ORACLE_TEST_STALL") == "1")
                    Thread.Sleep(Timeout.Infinite);
            }
        }
        """;

    private static string EmitHook() {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HookSource + RuntimeInformation.FrameworkDescription)))
            .ToLowerInvariant()[..32];
        var final = Path.Combine(HookRoot, hash);
        var path = Path.Combine(final, HookFileName);
        if (Directory.Exists(final)) {
            Directory.SetLastWriteTimeUtc(final, DateTime.UtcNow);
            return path;
        }

        Directory.CreateDirectory(HookRoot);
        var temp = final + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temp);
        try {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "System.Private.CoreLib", "System.Runtime", "System.Console", "System.Diagnostics.Process",
                "System.Threading", "System.Threading.Thread", "System.Collections", "System.ComponentModel.Primitives",
            };
            var references = _references.Value
                .Where(r => r.IsFramework && wanted.Contains(r.Name))
                .Select(r => (MetadataReference)MetadataReference.CreateFromFile(r.Path))
                .ToList();
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(HookFileName),
                [CSharpSyntaxTree.ParseText(HookSource, path: "PackOracleHook.cs")],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            using (var stream = File.Create(Path.Combine(temp, HookFileName))) {
                var emitted = compilation.Emit(stream);
                if (!emitted.Success)
                    throw new InvalidOperationException("The oracle startup hook did not compile:\n" + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
            }
            MoveIntoPlace(temp, final);
        }
        catch {
            TryDeleteDirectory(temp);
            throw;
        }
        return path;
    }

    // ───────────────────────────── output ring ─────────────────────────────

    /// <summary>The last 200 lines, at most 4 KB each and 32 KB of text in total, thread-safe.</summary>
    private sealed class RingBuffer {
        private const int MaxLines = 200;
        private const int MaxLineChars = 4 * 1024;
        private const int MaxChars = 32 * 1024;
        private readonly Queue<string> _lines = new();
        private readonly object _gate = new();
        private int _chars;

        public void Add(string line) {
            // A single unbounded line (a stack trace, a dumped payload) would break the 32 KB bound on its own.
            if (line.Length > MaxLineChars)
                line = line[..MaxLineChars];
            lock (_gate) {
                _lines.Enqueue(line);
                _chars += line.Length;
                while (_lines.Count > MaxLines || _chars > MaxChars) {
                    if (_lines.Count <= 1)
                        break;
                    _chars -= _lines.Dequeue().Length;
                }
            }
        }

        public string Tail() {
            lock (_gate)
                return string.Join('\n', _lines);
        }
    }
}