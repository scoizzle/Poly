using Poly.DslCompiler;

// ── Argument parsing ────────────────────────────────────────────
// Usage: poly-dsl-compiler [options] <input.poly> [output-dir]
// Options:
//   --mode entities|db|all     What to generate (default: entities)
//   --dbms generic|sqlite|sqlserver   Storage type defaults (default: generic)

var inputPath = (string?)null;
var outputDir = (string?)null;
var mode = CompileMode.Entities;
var dbms = DbmsPack.Generic;

try {
    for (int i = 0; i < args.Length; i++) {
        if (args[i] == "--mode" && i + 1 < args.Length) {
            i++;
            mode = args[i].ToLowerInvariant() switch {
                "entities" => CompileMode.Entities,
                "db" => CompileMode.Db,
                "all" => CompileMode.All,
                var other => throw new FormatException(
                    $"Unknown mode '{other}'. Valid values: entities, db, all")
            };
        }
        else if ((args[i] == "--dbms" || args[i] == "--pack") && i + 1 < args.Length) {
            i++;
            dbms = DslCompiler.ParseDbmsPack(args[i]);
        }
        else if (inputPath is null) {
            inputPath = args[i];
        }
        else if (outputDir is null) {
            outputDir = args[i];
        }
        else {
            throw new FormatException($"Unexpected argument '{args[i]}'.");
        }
    }
}
catch (FormatException ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

if (inputPath is null) {
    Console.Error.WriteLine("Usage: poly-dsl-compiler [--mode entities|db|all] [--dbms generic|sqlite|sqlserver] <input.poly> [output-dir]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Compiles a .poly DSL domain model into C# source files.");
    Console.Error.WriteLine("  --mode entities    Entity type definitions only (default)");
    Console.Error.WriteLine("  --mode db          Entity types + EF Core DbContext");
    Console.Error.WriteLine("  --mode all          Entity types + DbContext + Minimal API (via Syntax IR)");
    Console.Error.WriteLine("  --dbms generic     Core generic SQL column types (default)");
    Console.Error.WriteLine("  --dbms sqlite      SQLite affinities (first shippable pack; no server required)");
    Console.Error.WriteLine("  --dbms sqlserver   SQL Server column types");
    Console.Error.WriteLine();
    Console.Error.WriteLine("If output-dir is provided, files are written there.");
    Console.Error.WriteLine("Otherwise, the combined file is written to stdout.");
    return 1;
}

if (!File.Exists(inputPath)) {
    Console.Error.WriteLine($"Error: File not found: {inputPath}");
    return 1;
}

var polyText = await File.ReadAllTextAsync(inputPath);
var compiler = new DslCompiler();

try {
    var result = compiler.Compile(polyText, mode, dbms);

    if (!result.Success) {
        Console.Error.WriteLine("Compilation failed:");
        if (result.Errors is { Count: > 0 }) {
            foreach (var msg in result.Errors)
                Console.Error.WriteLine($"  {msg}");
        }
        else {
            Console.Error.WriteLine("  (unknown error)");
        }
        return 1;
    }

    if (outputDir is not null) {
        Directory.CreateDirectory(outputDir);

        foreach (var (fileName, source) in result.Files!) {
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllTextAsync(filePath, source);
            Console.WriteLine($"  wrote {filePath}");
        }

        Console.WriteLine($"Done: {result.Files!.Count} file(s) written to {outputDir}");
    }
    else {
        // stdout mode: prefer combined entity file when present;
        // otherwise emit all generated files with stable file headers.
        var files = result.Files!;
        var combined = files.FirstOrDefault(f => f.FileName == "_all.cs");
        if (combined != default) {
            await Console.Out.WriteLineAsync(combined.Source);
        }
        else {
            for (int i = 0; i < files.Count; i++) {
                var file = files[i];
                await Console.Out.WriteLineAsync($"// ===== {file.FileName} =====");
                await Console.Out.WriteLineAsync(file.Source);
                if (i < files.Count - 1) {
                    await Console.Out.WriteLineAsync();
                }
            }
        }
    }

    return 0;
}
catch (Exception ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}