using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

using Poly.Tests.Polyc;

using TUnit.Core;

namespace Poly.Tests.TestHelpers;

/// <summary>What to start. <see cref="Dll"/> is run as <c>dotnet &lt;Dll&gt; &lt;Args&gt;</c>.</summary>
internal sealed record ChildSpec(string Dll, IReadOnlyList<string> Args) {
    public IReadOnlyDictionary<string, string?>? Env { get; init; }
    /// <summary>Stdin stays open by default: a hook that reads EOF as "parent died" must not see a spurious one.</summary>
    public bool KeepStdinOpen { get; init; } = true;
    /// <summary>When set, written to stdin on a task (a broken pipe is swallowed) and stdin is then closed.</summary>
    public string? StdinText { get; init; }
    public TimeSpan? Timeout { get; init; }
    /// <summary>Called with the pid right after the start, still inside the slot.</summary>
    public Action<int>? Started { get; init; }
    /// <summary>Test seam: replaces the launcher so a start failure can be provoked.</summary>
    internal string? Launcher { get; init; }
}

internal sealed record ProcessResult(
    int ExitCode, string Stdout, string Stderr, TimeSpan Duration, long StdoutDroppedChars, long StderrDroppedChars) {
    public bool Succeeded => ExitCode == 0;
    public bool IsAborted => ExitCode == PolycProcess.AbortExitCode;
}

internal class PolycChildException(string message, int processId, bool killed, Exception? inner = null) : Exception(message, inner) {
    public int ProcessId { get; } = processId;
    public bool Killed { get; } = killed;
}

internal sealed class PolycTimeoutException(string message, int processId) : PolycChildException(message, processId, killed: true);

internal enum McpTarget { Polyc, PolyMcp }

/// <summary>The one place under Poly.Tests that starts a polyc/MCP/test-host child. Every path is
/// acquire slot -> start -> drain (bounded, chunked) -> kill tree -> bounded wait -> release.</summary>
internal static class PolycProcess {
    /// <summary>128 + SIGABRT(6): what .NET's Unix <see cref="Process.ExitCode"/> reports for an aborted child (measured).</summary>
    public const int AbortExitCode = 134;
    public const int StdoutCapBytes = 1 << 20;
    public const int StderrHeadChars = 4 * 1024;
    public const int StderrTailChars = 32 * 1024;
    public static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan _killWait = TimeSpan.FromSeconds(10);

    internal static void GuardDepth() {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLY_CHILD_DEPTH")))
            throw new InvalidOperationException("PolycProcess inside a child (POLY_CHILD_DEPTH=1)");
    }

    public static async Task<ProcessResult> RunAsync(ChildSpec spec, CancellationToken ct = default) {
        GuardDepth();
        using var linked = Link(ct);
        var timeout = spec.Timeout ?? BatchTimeout;
        using var child = await ManagedChild.StartAsync(spec, linked.Token);
        var process = child.Process;
        var started = Stopwatch.StartNew();
        using var drainCts = new CancellationTokenSource();
        var stdout = new BoundedText(StdoutCapBytes, 0);
        var stderr = new BoundedText(StderrHeadChars, StderrTailChars);
        var drains = Task.WhenAll(
            Drain(process.StandardOutput, stdout, drainCts.Token),
            Drain(process.StandardError, stderr, drainCts.Token));
        if (spec.StdinText is not null)
            _ = WriteStdinAsync(process, spec.StdinText);
        else if (!spec.KeepStdinOpen)
            TryCloseStdin(process);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        deadline.CancelAfter(timeout);
        try {
            await process.WaitForExitAsync(deadline.Token);
            try {
                await drains.WaitAsync(_killWait);
            }
            catch (TimeoutException) {
                // A grandchild holds the pipe open: take what we have.
            }
        }
        catch (OperationCanceledException) {
            child.KillAndWait();
            await StopDrains(drainCts, drains);
            if (linked.IsCancellationRequested)
                throw new OperationCanceledException("Child run cancelled; the process tree was killed.", linked.Token);
            throw new PolycTimeoutException($"Child {child.Pid} exceeded {timeout.TotalSeconds:0.###} s and was killed.", child.Pid);
        }
        await StopDrains(drainCts, drains);
        return new ProcessResult(process.ExitCode, stdout.Text, stderr.Text, started.Elapsed, stdout.Dropped, stderr.Dropped);
    }

    public static Task<ProcessResult> CheckAsync(string path, string? seed = null, CancellationToken ct = default) =>
        RunAsync(PolycSpec(["check", path], seed, null, null), ct);

    public static Task<ProcessResult> CheckTextAsync(string poly, string? seed = null, CancellationToken ct = default) {
        var spec = PolycSpec(["check"], seed, null, null);
        var env = new Dictionary<string, string?>(spec.Env!) { ["POLY_CHILD_STDIN_CLOSED_BY_PARENT"] = "1" };
        return RunAsync(spec with { StdinText = poly, Env = env }, ct);
    }

    public static Task<ProcessResult> BuildAsync(
        string path, string outDir, string? seed = null, string? target = null, IReadOnlyList<string>? artifacts = null, CancellationToken ct = default) {
        var args = new List<string> { "build", path, "--out", outDir };
        if (artifacts is not null)
            foreach (var artifact in artifacts) {
                args.Add("--artifact");
                args.Add(artifact);
            }
        return RunAsync(PolycSpec(args, seed, target, null), ct);
    }

    /// <summary>Runs <c>dotnet Poly.Tests.dll --treenode-filter ...</c>: no build, no <c>dotnet run</c> recursion.
    /// A zero-match filter is non-zero (<c>--minimum-expected-tests 1</c>).</summary>
    public static Task<ProcessResult> RunTestInChildAsync(
        string treenodeFilter, IReadOnlyDictionary<string, string?>? env = null, CancellationToken ct = default) =>
        RunAsync(TestChildSpec(treenodeFilter, env), ct);

    internal static ChildSpec TestChildSpec(string treenodeFilter, IReadOnlyDictionary<string, string?>? env = null) {
        var merged = Children.Env(env);
        merged["POLY_CHILD_DEPTH"] = "1";
        return new ChildSpec(
            typeof(PolycProcess).Assembly.Location,
            ["--treenode-filter", treenodeFilter, "--minimum-expected-tests", "1", "--no-ansi"]) { Env = merged };
    }

    public static async Task<McpClient> McpAsync(McpTarget target, TimeSpan? readTimeout = null, CancellationToken ct = default) {
        GuardDepth();
        var (dll, _) = HostFiles(target == McpTarget.Polyc ? "polyc" : "Poly.Mcp");
        string[] args = target == McpTarget.Polyc ? ["mcp"] : [];
        using var linked = Link(ct);
        var child = await ManagedChild.StartAsync(new ChildSpec(dll, args) { Env = Children.Env(null) }, linked.Token);
        try {
            return new McpClient(child, readTimeout ?? TimeSpan.FromSeconds(60));
        }
        catch {
            child.Dispose();
            throw;
        }
    }

    /// <summary>The host dll and runtimeconfig: beside the test assembly first (the plain <c>dotnet build</c> layout),
    /// else the one project output directory for this configuration. Never a build, never a glob.</summary>
    public static (string Dll, string RuntimeConfig) HostFiles(string name) {
        var beside = (Dll: Path.Combine(AppContext.BaseDirectory, name + ".dll"), Config: Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json"));
        if (File.Exists(beside.Dll) && File.Exists(beside.Config))
            return beside;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var project = name == "polyc" ? "src/polyc" : name;
        var output = Path.Combine(PolycTestSupport.RepoRoot(), project, "bin", configuration, "net10.0");
        var fallback = (Dll: Path.Combine(output, name + ".dll"), Config: Path.Combine(output, name + ".runtimeconfig.json"));
        if (File.Exists(fallback.Dll) && File.Exists(fallback.Config))
            return fallback;
        throw new FileNotFoundException(
            $"Host '{name}' not found. Expected {beside.Dll} + {beside.Config}, or {fallback.Dll} + {fallback.Config}. Build the solution first; the tests never build.");
    }

    private static ChildSpec PolycSpec(List<string> args, string? seed, string? target, IReadOnlyDictionary<string, string?>? env) {
        if (seed is not null) {
            args.Add("--seed");
            args.Add(seed);
        }
        if (target is not null) {
            args.Add("--target");
            args.Add(target);
        }
        return new ChildSpec(HostFiles("polyc").Dll, args) { Env = Children.Env(env) };
    }

    /// <summary>The caller's token linked with the running test's token, so a failed or timed-out test cancels its children.</summary>
    private static CancellationTokenSource Link(CancellationToken ct) {
        var testToken = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;
        return CancellationTokenSource.CreateLinkedTokenSource(ct, testToken);
    }

    private static async Task StopDrains(CancellationTokenSource drainCts, Task drains) {
        await drainCts.CancelAsync();
        try {
            await drains;
        }
        catch (OperationCanceledException) {
        }
    }

    private static async Task WriteStdinAsync(Process process, string text) {
        try {
            await process.StandardInput.WriteAsync(text);
            await process.StandardInput.FlushAsync();
        }
        catch (IOException) {
            // The child exited (or crashed) before reading it all: that is the result, not an error.
        }
        finally {
            TryCloseStdin(process);
        }
    }

    private static void TryCloseStdin(Process process) {
        try {
            process.StandardInput.Close();
        }
        catch (IOException) {
        }
    }

    /// <summary>Fixed 8 KiB char chunks; never line-based, so a newline-free flood cannot grow a buffer.</summary>
    internal static async Task Drain(StreamReader reader, BoundedText sink, CancellationToken ct) {
        var chunk = new char[8192];
        try {
            while (true) {
                var read = await reader.ReadAsync(chunk.AsMemory(), ct);
                if (read == 0)
                    return;
                sink.Append(chunk.AsSpan(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) {
            // The pipe closed under the read: the child is gone.
        }
    }

    /// <summary>The parent's environment minus everything that steers the runtime, then the child's fixed limits.</summary>
    internal static class Children {
        public static Dictionary<string, string?> Env(IReadOnlyDictionary<string, string?>? extra) {
            var env = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
                var key = (string)entry.Key;
                if (!IsRuntimeSteering(key))
                    env[key] = (string?)entry.Value;
            }
            env["DOTNET_gcServer"] = "0";
            env["DOTNET_GCHeapHardLimit"] = "0x40000000";
            env["DOTNET_DbgEnableMiniDump"] = "0";
            env["DOTNET_EnableCrashReport"] = "0";
            if (extra is not null)
                foreach (var (key, value) in extra)
                    env[key] = value;
            return env;
        }

        private static bool IsRuntimeSteering(string key) =>
            key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("POLY_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A started child holding one gate slot. <see cref="Dispose"/> kills the tree, waits (bounded), then releases.</summary>
    internal sealed class ManagedChild : IDisposable {
        // Live children, reaped when the host exits or is interrupted. SIGKILL of the host is not covered (follow-up).
        private static readonly ConcurrentDictionary<int, Process> _live = new();
        private int _disposed;

        static ManagedChild() {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ReapAll();
            Console.CancelKeyPress += (_, _) => ReapAll();
        }

        private ManagedChild(Process process) {
            Process = process;
            Pid = process.Id;
        }

        public Process Process { get; }
        public int Pid { get; }

        public static async Task<ManagedChild> StartAsync(ChildSpec spec, CancellationToken ct) {
            await ChildProcessGate.AcquireAsync(ct);
            ManagedChild? child = null;
            try {
                var info = new ProcessStartInfo(spec.Launcher ?? PackOracle.LauncherPath()) {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                info.ArgumentList.Add(spec.Dll);
                foreach (var arg in spec.Args)
                    info.ArgumentList.Add(arg);
                info.Environment.Clear();
                foreach (var (key, value) in spec.Env ?? Children.Env(null))
                    if (value is not null)
                        info.Environment[key] = value;
                var process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null.");
                child = new ManagedChild(process);
                _live[child.Pid] = process;
                spec.Started?.Invoke(child.Pid);
                return child;
            }
            catch {
                if (child is not null)
                    child.Dispose();
                else
                    ChildProcessGate.Release();
                throw;
            }
        }

        public void KillAndWait() {
            try {
                if (!Process.HasExited)
                    Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) {
                // Already gone, or not ours to signal.
            }
            try {
                Process.WaitForExit((int)_killWait.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) {
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try {
                KillAndWait();
            }
            finally {
                _live.TryRemove(Pid, out _);
                Process.Dispose();
                ChildProcessGate.Release();
            }
        }

        private static void ReapAll() {
            foreach (var process in _live.Values) {
                try {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                catch (Exception) {
                    // Best effort inside a host-exit handler; never throw or block here.
                }
            }
        }
    }

    /// <summary>A head of at most <c>head</c> chars plus a tail ring of at most <c>tail</c> chars; the rest is counted.</summary>
    internal sealed class BoundedText(int head, int tail) {
        private readonly StringBuilder _head = new();
        private readonly StringBuilder _tail = new();
        private readonly object _gate = new();
        private long _dropped;

        public long Dropped {
            get {
                lock (_gate)
                    return _dropped;
            }
        }

        public string Text {
            get {
                lock (_gate)
                    return tail > 0 && _dropped > 0 ? $"{_head}\n...[{_dropped} chars dropped]...\n{_tail}" : _head.ToString() + _tail;
            }
        }

        public void Append(ReadOnlySpan<char> chars) {
            lock (_gate) {
                var room = head - _head.Length;
                var toHead = Math.Min(room, chars.Length);
                if (toHead > 0)
                    _head.Append(chars[..toHead]);
                chars = chars[toHead..];
                if (chars.IsEmpty)
                    return;
                if (tail == 0) {
                    _dropped += chars.Length;
                    return;
                }
                _tail.Append(chars);
                if (_tail.Length > tail) {
                    var excess = _tail.Length - tail;
                    _tail.Remove(0, excess);
                    _dropped += excess;
                }
            }
        }
    }
}

/// <summary>A scripted JSON-RPC stdio client over a managed child. <see cref="Dispose"/> kills the tree and releases the slot.
/// Every read has a total deadline and the client has an absolute lifetime.</summary>
internal sealed class McpClient : IDisposable {
    public const int MaxLineChars = 1 << 20;
    private readonly PolycProcess.ManagedChild _child;
    private readonly TimeSpan _readTimeout;
    private readonly CancellationTokenSource _life = new();
    private readonly PolycProcess.BoundedText _stderr = new(0, PolycProcess.StderrTailChars);
    private readonly char[] _buffer = new char[8192];
    private readonly StringBuilder _line = new();
    private int _pos, _len, _id;

    internal McpClient(PolycProcess.ManagedChild child, TimeSpan readTimeout) {
        _child = child;
        _readTimeout = readTimeout;
        Lifetime = TimeSpan.FromSeconds(150);
        _life.CancelAfter(Lifetime);
        _life.Token.Register(() => child.KillAndWait());
        _ = PolycProcess.Drain(child.Process.StandardError, _stderr, CancellationToken.None);
    }

    public string? Instructions { get; private set; }
    public TimeSpan Lifetime { get; }
    public string StderrTail => _stderr.Text;

    public async Task<JsonNode> InitializeAsync(string protocolVersion = "2025-06-18") {
        var response = await CallAsync("initialize", new JsonObject {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "polyc-test", ["version"] = "1" },
        });
        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        var result = response["result"]!;
        Instructions = result["instructions"]?.GetValue<string>();
        return result;
    }

    /// <summary>Sends a request and returns the full response message with the matching id.</summary>
    public async Task<JsonNode> CallAsync(string method, JsonObject parameters, TimeSpan? timeout = null) {
        var id = ++_id;
        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
        var limit = timeout ?? _readTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        deadline.CancelAfter(limit);
        try {
            while (true) {
                var message = JsonNode.Parse(await ReadLineAsync(deadline.Token))!;
                if (message["id"]?.GetValue<int>() == id)
                    return message;
            }
        }
        catch (OperationCanceledException) {
            throw new TimeoutException(
                $"No MCP response to '{method}' within {limit.TotalSeconds:0.###} s (lifetime {Lifetime.TotalSeconds:0} s). stderr tail: {StderrTail}");
        }
    }

    public async Task<IReadOnlyList<JsonObject>> ToolsListAsync() {
        var response = await CallAsync("tools/list", new JsonObject());
        return [.. response["result"]!["tools"]!.AsArray().Select(t => t!.AsObject())];
    }

    public async Task<JsonNode> ToolAsync(string name, JsonObject arguments) {
        var response = await CallAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });
        var result = response["result"]!;
        return result["structuredContent"] ?? JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
    }

    private async Task SendAsync(JsonNode message) {
        var stdin = _child.Process.StandardInput;
        await stdin.WriteLineAsync(message.ToJsonString());
        await stdin.FlushAsync();
    }

    private async Task<string> ReadLineAsync(CancellationToken ct) {
        var reader = _child.Process.StandardOutput;
        while (true) {
            while (_pos < _len) {
                var c = _buffer[_pos++];
                if (c == '\n') {
                    var line = _line.ToString().TrimEnd('\r');
                    _line.Clear();
                    return line;
                }
                _line.Append(c);
                if (_line.Length > MaxLineChars)
                    throw new InvalidDataException($"An MCP line exceeded the {MaxLineChars}-char cap.");
            }
            _len = await reader.ReadAsync(_buffer.AsMemory(), ct);
            _pos = 0;
            if (_len == 0)
                throw new EndOfStreamException("The MCP host closed stdout.");
        }
    }

    public void Dispose() {
        _life.Dispose();
        _child.Dispose();
    }
}