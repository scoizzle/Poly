using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.Packs.Product;

namespace Polyc;

/// <summary>The <c>build</c> and <c>check</c> commands. Testable: all I/O is passed in.</summary>
internal static class PolycApp {
    public static int Run(
        string[] args,
        TextReader stdin,
        bool stdinRedirected,
        TextWriter stdout,
        TextWriter stderr) {
        try {
            return RunCore(args, stdin, stdinRedirected, stdout, stderr);
        }
        catch (Exception ex) {
            stderr.WriteLine($"polyc: unexpected failure: {ex}");
            return 2;
        }
    }

    private static int RunCore(string[] args, TextReader stdin, bool stdinRedirected, TextWriter stdout, TextWriter stderr) {
        if (args.Length == 0 || args[0] is not ("build" or "check")) {
            stderr.WriteLine(Options.Usage);
            return 1;
        }
        var options = Options.Parse(args, out var error);
        if (options is null) {
            stderr.WriteLine($"polyc: {error}");
            stderr.WriteLine(Options.Usage);
            return 1;
        }

        var inputs = new List<(string File, string Text)>();
        if (options.Paths.Count == 0) {
            if (!stdinRedirected) {
                stderr.WriteLine(Options.Usage);
                return 1;
            }
            inputs.Add(("<stdin>", stdin.ReadToEnd()));
        }
        else {
            foreach (var path in options.Paths) {
                if (Directory.Exists(path)) {
                    foreach (var file in Directory.EnumerateFiles(path, "*.poly", SearchOption.TopDirectoryOnly)
                                 .OrderBy(f => f, StringComparer.Ordinal))
                        inputs.Add((file, File.ReadAllText(file)));
                }
                else if (File.Exists(path)) {
                    inputs.Add((path, File.ReadAllText(path)));
                }
                else {
                    stderr.WriteLine($"polyc: no such file or directory: {path}");
                    return 1;
                }
            }
            if (inputs.Count == 0) {
                stderr.WriteLine("polyc: no .poly files found.");
                return 1;
            }
        }

        var compiler = new PolyCompiler(ProductCatalog.Catalog, options.Seed);
        var units = inputs.Select(i => compiler.Compile(i.Text, i.File, options.Target)).ToList();

        var extra = new List<CompileDiagnostic>();
        var written = new List<string>();
        var plan = new List<(string Path, string Text)>();
        if (options.Command == "build" && !units.Any(u => !u.Succeeded)) {
            if (!Plan(options, units, plan, extra, stderr))
                return Finish(options, units, extra, written, stdout, stderr);
        }

        var failed = units.Any(u => !u.Succeeded) || extra.Any(d => d.IsError);
        if (options.Command == "build" && !failed) {
            foreach (var (path, text) in plan) {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                written.Add(path);
            }
        }
        return Finish(options, units, extra, written, stdout, stderr);
    }

    private static int Finish(
        Options options,
        IReadOnlyList<CompileResult> units,
        IReadOnlyList<CompileDiagnostic> extra,
        IReadOnlyList<string> written,
        TextWriter stdout,
        TextWriter stderr) {
        var all = units.SelectMany(u => u.Diagnostics).Concat(extra).ToList();
        var errors = all.Count(d => d.IsError);
        var warnings = all.Count(d => d.IsWarning);
        if (options.Format == "json") {
            stdout.WriteLine(DiagnosticFormat.Report(units, extra));
            foreach (var path in written)
                stderr.WriteLine($"wrote {path}");
        }
        else {
            foreach (var line in DiagnosticFormat.TextLines(all, options.Verbosity))
                stdout.WriteLine(line);
            if (options.Command == "build" && errors == 0)
                stdout.WriteLine($"polyc: wrote {written.Count} file(s).");
        }
        return errors > 0 || (options.WarnAsError && warnings > 0) ? 1 : 0;
    }

    /// <summary>Plans every write before any happens. Returns false after recording an error.</summary>
    private static bool Plan(
        Options options,
        IReadOnlyList<CompileResult> units,
        List<(string Path, string Text)> plan,
        List<CompileDiagnostic> extra,
        TextWriter stderr) {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units) {
            IReadOnlyList<(string Name, string Text)> files;
            try {
                files = FilesFor(options, unit);
            }
            catch (InvalidOperationException ex) {
                extra.Add(Refused(unit.File, ex.Message));
                return false;
            }
            var directory = Path.Combine(options.Out, unit.DomainName ?? "PolyDomain");
            foreach (var (name, text) in files) {
                var path = Path.Combine(directory, name);
                if (owners.TryGetValue(path, out var owner)) {
                    extra.Add(Refused(unit.File, $"'{unit.File}' and '{owner}' both write '{path}'."));
                    return false;
                }
                owners[path] = unit.File;
                plan.Add((path, text));
            }
        }
        return true;
    }

    private static IReadOnlyList<(string Name, string Text)> FilesFor(Options options, CompileResult unit) {
        if (options.Artifacts.Count == 0)
            return unit.Files.Select(f => (f.FileName, f.Source)).ToList();

        var catalog = unit.Catalog ?? throw new InvalidOperationException("No artifact catalog was produced.");
        var result = new List<(string, string)>();
        foreach (var type in options.Artifacts) {
            var matching = catalog.Artifacts.Where(a => string.Equals(a.Descriptor.Id.Type, type, StringComparison.Ordinal)).ToList();
            if (matching.Count == 0)
                throw new InvalidOperationException($"No artifact of type '{type}' was produced for '{unit.File}'.");
            foreach (var artifact in matching) {
                if (type == ContributedFile.Type) {
                    result.Add((ContributedFile.FileName(artifact), ContributedFile.Text(artifact)));
                }
                else if (artifact.Payload is AnalysisReport report) {
                    var findings = report.Findings.Select(f => new {
                        code = f.Code,
                        severity = f.Severity.ToString().ToLowerInvariant(),
                        message = f.Message,
                        path = f.ElementPath,
                    });
                    result.Add(($"{string.Join('.', artifact.Descriptor.Id.Segments)}.{type}.json",
                        System.Text.Json.JsonSerializer.Serialize(findings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })));
                }
                else {
                    throw new InvalidOperationException(
                        $"Artifact type '{type}' is not a file or a report, so polyc cannot write it.");
                }
            }
        }
        return result;
    }

    private static CompileDiagnostic Refused(string file, string message) =>
        new(file, 1, 1, 1, 2, Severities.Error, DiagnosticCodes.Refused, message, "", Placed: false);
}