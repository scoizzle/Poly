using System.Text.Json;

using Poly.DslCompiler;

if (args.Length < 1 || args.Length > 2) {
    Console.Error.WriteLine("Usage: poly-dsl-compiler <input.poly> [output-dir]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Compiles a .poly DSL domain model into C# type definitions.");
    Console.Error.WriteLine("If output-dir is provided, one .cs file per type is created.");
    Console.Error.WriteLine("Otherwise, the generated C# code is written to stdout.");
    return 1;
}

var inputPath = args[0];
var outputDir = args.Length > 1 ? args[1] : null;

if (!File.Exists(inputPath)) {
    Console.Error.WriteLine($"Error: File not found: {inputPath}");
    return 1;
}

var polyText = await File.ReadAllTextAsync(inputPath);
var compiler = new DslCompiler();

try {
    var result = compiler.Compile(polyText);

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
        // stdout mode: emit only the combined file (avoids duplication with per-entity files)
        var combined = result.Files!.First(f => f.FileName == "_all.cs").Source;
        await Console.Out.WriteLineAsync(combined);
    }

    return 0;
}
catch (Exception ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}