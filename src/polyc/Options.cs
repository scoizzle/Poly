using Poly.Compile;
using Poly.DomainModeling.Compile;

namespace Polyc;

/// <summary>Parsed command line for <c>build</c> and <c>check</c>.</summary>
internal sealed class Options {
    public required string Command { get; init; }
    public List<string> Paths { get; } = [];
    public string Out { get; set; } = "./polyc-out";
    public string Seed { get; set; } = "authoring";
    public List<string> Artifacts { get; } = [];
    public string Format { get; set; } = "text";
    public bool WarnAsError { get; set; }
    public string Verbosity { get; set; } = Severities.Warning;

    public const string Usage = """
        Usage:
          polyc build [<file.poly|dir>...] [--out <dir>] [--seed <name>] [--artifact <type>]...
                      [--format text|json] [--warn-as-error] [--verbosity info|hint]
          polyc check [<file.poly|dir>...] [--seed <name>] [--format text|json]
                      [--warn-as-error] [--verbosity info|hint]
          polyc lsp
          polyc mcp

        A directory means every *.poly directly inside it (not recursive); each file is its own
        compilation unit. With no path and piped stdin, stdin is one document.
        Seeds: authoring (default), language. A document's own `uses` wins over the seed.
        Exit codes: 0 no errors, 1 errors or usage, 2 unexpected failure.
        --format json prints a JSON array of diagnostic records. A build also prints each
        written path on stderr as `wrote <path>`.
        """;

    /// <summary>Returns null with <paramref name="error"/> set on a usage problem.</summary>
    public static Options? Parse(string[] args, out string? error) {
        error = null;
        var options = new Options { Command = args[0] };
        for (var i = 1; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--out" or "--seed" or "--artifact" or "--format" or "--verbosity":
                    if (i + 1 >= args.Length) {
                        error = $"{arg} needs a value.";
                        return null;
                    }
                    var value = args[++i];
                    switch (arg) {
                        case "--out": options.Out = value; break;
                        case "--seed": options.Seed = value; break;
                        case "--artifact": options.Artifacts.Add(value); break;
                        case "--format":
                            if (value is not ("text" or "json")) {
                                error = $"Unknown format '{value}'. Use text or json.";
                                return null;
                            }
                            options.Format = value;
                            break;
                        default:
                            options.Verbosity = value switch {
                                "info" or "information" => Severities.Information,
                                "hint" => Severities.Hint,
                                _ => "",
                            };
                            if (options.Verbosity.Length == 0) {
                                error = $"Unknown verbosity '{value}'. Use info or hint.";
                                return null;
                            }
                            break;
                    }
                    break;
                case "--warn-as-error":
                    options.WarnAsError = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal)) {
                        error = $"Unknown option '{arg}'.";
                        return null;
                    }
                    options.Paths.Add(arg);
                    break;
            }
        }
        if (options.Command == "check" && options.Artifacts.Count > 0) {
            error = "--artifact applies to build only.";
            return null;
        }
        try {
            ExtensionCatalog.Seed(options.Seed);
        }
        catch (ArgumentException ex) {
            error = ex.Message;
            return null;
        }
        return options;
    }
}