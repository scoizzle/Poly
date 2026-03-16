using System;
using System.Collections.Generic;

namespace Poly.DomainModeling.V2.Spike;

public static class VerticalSliceSpike {
    public static IReadOnlyList<Diagnostic> Evaluate(PersonInput input)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(input.Name)) {
            diagnostics.Add(new Diagnostic(
                Code: "STRUCT.MISSING_FIELD",
                Message: "Name is required.",
                Path: "name"));
        }

        return diagnostics;
    }
}

public sealed record PersonInput(string? Name);

public sealed record Diagnostic(string Code, string Message, string Path);