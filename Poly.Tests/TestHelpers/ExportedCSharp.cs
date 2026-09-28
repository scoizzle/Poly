using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Poly.Tests.TestHelpers;

/// <summary>Shared helpers for reading and loading C# emitted by DomainToCSharpExporter.</summary>
public static class ExportedCSharp {
    /// <summary>Extracts a method's text by signature, walking braces to the matching close.</summary>
    public static string ExtractMethod(string cs, string signature) {
        var start = cs.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Method '{signature}' not found.");
        var brace = cs.IndexOf('{', start);
        if (brace < 0)
            throw new InvalidOperationException($"No body for '{signature}'.");
        var depth = 0;
        for (var i = brace; i < cs.Length; i++) {
            if (cs[i] == '{') depth++;
            else if (cs[i] == '}') {
                depth--;
                if (depth == 0)
                    return cs[start..(i + 1)];
            }
        }
        throw new InvalidOperationException($"Unbalanced braces for '{signature}'.");
    }

    /// <summary>Compiles exported C# with Roslyn and loads the assembly.</summary>
    public static Assembly CompileAndLoad(string cs, string assemblyName = "ExportedDomain") {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException(string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)) + "\n" + cs);
        pe.Position = 0;
        return new AssemblyLoadContext(assemblyName, isCollectible: true).LoadFromStream(pe);
    }

    /// <summary>Calls the widest static Create by named args and unwraps Value.</summary>
    public static object CreateEntity(Type type, params (string Name, object? Value)[] named) {
        var create = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var parameters = create.GetParameters();
        var callArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var match = named.FirstOrDefault(n =>
                string.Equals(n.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is not null)
                callArgs[i] = match.Value;
            else if (parameters[i].HasDefaultValue)
                callArgs[i] = parameters[i].DefaultValue;
            else if (parameters[i].ParameterType == typeof(string))
                callArgs[i] = "";
            else if (parameters[i].ParameterType == typeof(bool))
                callArgs[i] = false;
            else
                callArgs[i] = null;
        }
        var result = create.Invoke(null, callArgs)!;
        var ok = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        if (!ok)
            throw new InvalidOperationException(
                result.GetType().GetProperty("ErrorMessage")?.GetValue(result) as string
                ?? $"{type.Name}.Create failed.");
        return result.GetType().GetProperty("Value")!.GetValue(result)!;
    }

    /// <summary>Calls the widest static Create by named args and unwraps Value.</summary>
    public static object CreateEntity(Assembly assembly, string typeName, params (string Name, object? Value)[] named) =>
        CreateEntity(assembly.GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found."), named);
}
