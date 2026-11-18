using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// What a bare type name means is its scope's answer (#998 S2): a C# unit sees its keywords, its declarations and the
/// namespaces its usings import, and nothing the process merely happens to have loaded.
/// </summary>
public class TypeScopeTests {
    /// <summary>The value a unit's top-level statements yield, or the errors that stopped it.</summary>
    private static (object? Value, string Errors) Run(string text) {
        try {
            var unit = CSharpLanguage.Read(text);
            var analysis = Interpreter.Analyze(unit);
            var errors = analysis.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message).ToList();
            if (errors.Count > 0)
                return (null, string.Join("; ", errors));
            using var execution = Interpreter.Execute(Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis));
            return (execution.Result.Value, "");
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or FormatException or NotSupportedException) {
            return (null, refusal.Message);
        }
    }

    [Test]
    public async Task Using_BindsABareTypeName_InATypePosition() {
        var (value, errors) = Run("using System.Text;\nnew StringBuilder(\"ab\");");

        var (qualified, qualifiedErrors) = Run("new System.Text.StringBuilder(\"ab\");");

        await Assert.That(errors).IsEmpty();
        await Assert.That(qualifiedErrors).IsEmpty();
        await Assert.That($"{value?.GetType().FullName}:{value}").IsEqualTo($"{qualified?.GetType().FullName}:{qualified}")
            .Because("a name a using imports means what its qualified spelling means");
        await Assert.That(value).IsTypeOf<System.Text.StringBuilder>();
    }

    [Test]
    public async Task BareTypeName_OutsideTheUnitsUsings_DoesNotResolve() {
        var (_, errors) = Run("new StringBuilder(\"ab\");");

        await Assert.That(errors).Contains("StringBuilder");
    }

    [Test]
    public async Task BareTypeName_InTwoImportedNamespaces_IsAmbiguous() {
        var (_, errors) = Run("using System.Threading;\nusing System.Timers;\nnew Timer(1000.0);");

        await Assert.That(errors).Contains("ambiguous");
    }

    [Test]
    public async Task Keyword_NamesItsType_WithNoUsing() {
        var (value, errors) = Run("int.MaxValue == System.Int32.MaxValue;");

        await Assert.That(errors).IsEmpty();
        await Assert.That(Convert.ToInt64(value)).IsEqualTo(1L).Because("the VM states a true Boolean as the word 1");
    }

    [Test]
    public async Task BareClrName_WithNoUsing_DoesNotResolve_WhateverTheVocabularyNames() {
        // `DateTime` resolves today only because a shared primitive id happens to carry that name.
        var (_, errors) = Run("DateTime.UtcNow.Year > 2000;");
        await Assert.That(errors).Contains("DateTime");

        var (value, importedErrors) = Run("using System;\nDateTime.UtcNow.Year > 2000;");
        await Assert.That(importedErrors).IsEmpty();
        await Assert.That(Convert.ToInt64(value)).IsEqualTo(1L).Because("the VM states a true Boolean as the word 1");
    }
}