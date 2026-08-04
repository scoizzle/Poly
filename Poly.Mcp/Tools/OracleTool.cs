using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Queries;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Mcp.Sessions;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.Mcp.Tools;

/// <summary>
/// Oracle tools: visibility, lowering, description, and simulation for domain expressions and elements.
/// These tools are read-only — they never mutate session state.
/// </summary>
[McpServerToolType]
internal sealed class OracleTool {
    // ── Shared helpers ──────────────────────────────────────────

    private static DomainToolResponse? TryParseExpression(string expressionJson, out DomainExpression expr) {
        expr = null!;
        if (string.IsNullOrWhiteSpace(expressionJson)) {
            return new DomainToolResponse(Success: false, Message: "Expression JSON must not be empty.", Affordances: []);
        }
        try {
            expr = DomainExpressionJsonParser.ParseJson(expressionJson);
            return null;
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid expression JSON: {ex.Message}", Data: new { parseError = ex.Message }, Affordances: []);
        }
    }

    private static LoweredNodeData LowerToNodeData(DomainExpression expr) {
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(expr, new Syntactic.Parameter("entity"));
        return BuildNodeData(lowered);
    }

    private static LoweredNodeData BuildNodeData(Ast.Node node) {
        return node switch {
            Syntactic.Parameter p => new LoweredNodeData("Parameter", p.Name, null),
            Syntactic.Constant c => new LoweredNodeData("Constant", c.Value?.ToString() ?? "null", null),
            Syntactic.Member m => new LoweredNodeData("Member", m.MemberName, null),
            Syntactic.Equal e => new LoweredNodeData("Equal", null, [BuildNodeData(e.LeftHandValue), BuildNodeData(e.RightHandValue)]),
            Syntactic.NotEqual ne => new LoweredNodeData("NotEqual", null, [BuildNodeData(ne.LeftHandValue), BuildNodeData(ne.RightHandValue)]),
            Syntactic.LessThan lt => new LoweredNodeData("LessThan", null, [BuildNodeData(lt.LeftHandValue), BuildNodeData(lt.RightHandValue)]),
            Syntactic.LessThanOrEqual le => new LoweredNodeData("LessThanOrEqual", null, [BuildNodeData(le.LeftHandValue), BuildNodeData(le.RightHandValue)]),
            Syntactic.GreaterThan gt => new LoweredNodeData("GreaterThan", null, [BuildNodeData(gt.LeftHandValue), BuildNodeData(gt.RightHandValue)]),
            Syntactic.GreaterThanOrEqual ge => new LoweredNodeData("GreaterThanOrEqual", null, [BuildNodeData(ge.LeftHandValue), BuildNodeData(ge.RightHandValue)]),
            Syntactic.And a => new LoweredNodeData("And", null, [BuildNodeData(a.LeftHandValue), BuildNodeData(a.RightHandValue)]),
            Syntactic.Or o => new LoweredNodeData("Or", null, [BuildNodeData(o.LeftHandValue), BuildNodeData(o.RightHandValue)]),
            Syntactic.Not n => new LoweredNodeData("Not", null, [BuildNodeData(n.Value)]),
            Syntactic.Add a => new LoweredNodeData("Add", null, [BuildNodeData(a.LeftHandValue), BuildNodeData(a.RightHandValue)]),
            Syntactic.Subtract s => new LoweredNodeData("Subtract", null, [BuildNodeData(s.LeftHandValue), BuildNodeData(s.RightHandValue)]),
            Syntactic.Multiply m => new LoweredNodeData("Multiply", null, [BuildNodeData(m.LeftHandValue), BuildNodeData(m.RightHandValue)]),
            Syntactic.Divide d => new LoweredNodeData("Divide", null, [BuildNodeData(d.LeftHandValue), BuildNodeData(d.RightHandValue)]),
            Syntactic.Invoke inv => new LoweredNodeData("Invoke", GetMemberName(inv.Delegate), [.. inv.Arguments.Select(BuildNodeData)]),
            _ => new LoweredNodeData(node.GetType().Name, node.ToString(), null)
        };
    }

    private static string? GetMemberName(Ast.Node node) =>
        node is Syntactic.Member m ? m.MemberName : node.ToString();

    private static DescribeExpressionData DescribeExpression(DomainExpression expr) {
        var structured = DescribeStructured(expr, 0);
        var plainEnglish = DescribePlainEnglish(expr);
        return new DescribeExpressionData(structured, plainEnglish);
    }

    private static string DescribeStructured(DomainExpression expr, int indent) {
        var pad = new string(' ', indent * 2);
        return expr switch {
            PropertyAccess p => $"{pad}Property: {p.Name}",
            ParameterAccess p => $"{pad}Parameter: {p.Name}",
            Literal l => $"{pad}Literal: {l.Value ?? "null"}",
            OwnedAccess oa => $"{pad}Owned: {oa.OwnedName}\n{DescribeStructured(oa.Inner, indent + 1)}",
            RelationshipNavigation rn => $"{pad}Nav: {rn.RelationshipName}\n{DescribeStructured(rn.TargetProperty, indent + 1)}",
            Comparison c => $"{pad}{OpName(c.Kind)}\n{DescribeStructured(c.Left, indent + 1)}\n{DescribeStructured(c.Right, indent + 1)}",
            And a => $"{pad}And\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Or o => $"{pad}Or\n{DescribeStructured(o.Left, indent + 1)}\n{DescribeStructured(o.Right, indent + 1)}",
            Not n => $"{pad}Not\n{DescribeStructured(n.Operand, indent + 1)}",
            Add a => $"{pad}Add\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Subtract s => $"{pad}Subtract\n{DescribeStructured(s.Left, indent + 1)}\n{DescribeStructured(s.Right, indent + 1)}",
            Multiply m => $"{pad}Multiply\n{DescribeStructured(m.Left, indent + 1)}\n{DescribeStructured(m.Right, indent + 1)}",
            Divide d => $"{pad}Divide\n{DescribeStructured(d.Left, indent + 1)}\n{DescribeStructured(d.Right, indent + 1)}",
            Exists e => $"{pad}Exists\n{DescribeStructured(e.Target, indent + 1)}",
            NotExists ne => $"{pad}NotExists\n{DescribeStructured(ne.Target, indent + 1)}",
            DateOperation d => $"{pad}DateOp ({d.Kind})\n{DescribeStructured(d.Date, indent + 1)}\n{DescribeStructured(d.Offset, indent + 1)}",
            _ => $"{pad}{expr.GetType().Name}: {expr}"
        };
    }

    private static string DescribePlainEnglish(DomainExpression expr) => expr switch {
        PropertyAccess p => $"the value of '{p.Name}'",
        ParameterAccess p => $"parameter '{p.Name}'",
        Literal l => FormatLiteral(l.Value),
        OwnedAccess oa => $"{DescribePlainEnglish(oa.Inner)} of '{oa.OwnedName}'",
        RelationshipNavigation rn => $"{DescribePlainEnglish(rn.TargetProperty)} via '{rn.RelationshipName}'",
        Comparison c => $"{DescribePlainEnglish(c.Left)} {OpEnglish(c.Kind)} {DescribePlainEnglish(c.Right)}",
        And a => $"{DescribePlainEnglish(a.Left)} and {DescribePlainEnglish(a.Right)}",
        Or o => $"{DescribePlainEnglish(o.Left)} or {DescribePlainEnglish(o.Right)}",
        Not n => $"not ({DescribePlainEnglish(n.Operand)})",
        Add a => $"{DescribePlainEnglish(a.Left)} plus {DescribePlainEnglish(a.Right)}",
        Subtract s => $"{DescribePlainEnglish(s.Left)} minus {DescribePlainEnglish(s.Right)}",
        Multiply m => $"{DescribePlainEnglish(m.Left)} times {DescribePlainEnglish(m.Right)}",
        Divide d => $"{DescribePlainEnglish(d.Left)} divided by {DescribePlainEnglish(d.Right)}",
        Exists e => $"{DescribePlainEnglish(e.Target)} exists",
        NotExists ne => $"{DescribePlainEnglish(ne.Target)} does not exist",
        DateOperation d => $"date operation ({d.Kind}) on {DescribePlainEnglish(d.Date)} with {DescribePlainEnglish(d.Offset)}",
        _ => expr.ToString() ?? "unknown"
    };

    private static string OpName(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        ComparisonKind.GreaterThanOrEqual => ">=",
        _ => kind.ToString()
    };

    private static string OpEnglish(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "equals",
        ComparisonKind.NotEqual => "does not equal",
        ComparisonKind.LessThan => "is less than",
        ComparisonKind.LessThanOrEqual => "is at most",
        ComparisonKind.GreaterThan => "is greater than",
        ComparisonKind.GreaterThanOrEqual => "is at least",
        _ => kind.ToString()
    };

    private static string FormatLiteral(object? value) => value switch {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };

    /// <summary>
    /// Runs the Interpreter's 13-pass Syntax AST analysis on a lowered node
    /// and returns the <see cref="AnalysisResult"/>.
    /// Returns null if analysis is not available (no regression).
    /// <summary>
    /// Runs the Interpreter's 13-pass Syntax AST analysis on a lowered node.
    /// If analysis reports errors, a non-null <paramref name="error"/> is set.
    /// On success, <paramref name="analysis"/> is set and <paramref name="error"/> is null.
    /// </summary>
    private static bool TryAnalyze(Ast.Node lowered,
        out Poly.Analysis.AnalysisResult? analysis,
        out DomainToolResponse? error) {
        analysis = null;
        error = null;
        try {
            analysis = Interpreter.Analyze(lowered);
        }
        catch (Exception ex) {
            error = new DomainToolResponse(Success: false, Message: $"AST analysis failed: {ex.Message}",
                Data: new { error = ex.Message }, Affordances: ["analyze_expression"]);
            return false;
        }

        if (analysis.HasErrors) {
            var errorDiags = analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .ToList();
            error = new DomainToolResponse(Success: false,
                Message: $"C# generation blocked: analysis found {errorDiags.Count} error(s).",
                Data: new {
                    diagnostics = errorDiags.Select(d => new {
                        severity = d.Severity.ToString(),
                        code = d.Code,
                        message = d.Message,
                        nodeKind = d.Node?.GetType().Name
                    }),
                    hasErrors = true,
                    hasStructuralFailure = analysis.HasStructuralFailure
                },
                Affordances: ["analyze_expression"]);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Generates C# from a lowered node, running Interpreter analysis first.
    /// Returns the C# text on success, or an error with diagnostics if analysis failed.
    /// </summary>
    private static DomainToolResponse CSharpWithAnalysis(Ast.Node lowered, string successMessage,
        Func<string, object>? buildData = null, string[]? affordances = null) {
        if (!TryAnalyze(lowered, out var analysis, out var error))
            return error!;
        var generator = new CSharpGenerator(analysis!);
        var csharp = generator.Generate(lowered);
        var data = buildData?.Invoke(csharp) ?? new { csharp };
        return new DomainToolResponse(Success: true, Message: successMessage, Data: data, Affordances: affordances ?? ["analyze_expression", "describe_expression", "lower_expression"]);
    }

    // ── V0.3b: analyze_expression ──────────────────────────

    [McpServerTool(Name = "analyze_expression"), Description("Parses a JSON policy expression, lowers it through the Syntax AST pipeline, and runs the Interpreter's 13-pass analysis pipeline. Returns diagnostics, side-effect classification, constant-folding results, and other analysis metadata. No session required.")]
    public static DomainToolResponse AnalyzeExpression(
        [Description("JSON expression string (same format as add_policy).")] string expressionJson) {
        var failure = TryParseExpression(expressionJson, out var expr);
        if (failure is not null) return failure;
        try {
            var lowered = LowerExpressionToNode(expr);
            var analysis = Interpreter.Analyze(lowered);

            var diagnostics = analysis.Diagnostics.Select(d => new {
                severity = d.Severity.ToString(),
                code = d.Code,
                message = d.Message,
                nodeKind = d.Node?.GetType().Name
            }).ToList();

            return new DomainToolResponse(Success: true, Message: $"Expression analyzed: {diagnostics.Count} diagnostics.", Data: new {
                diagnostics,
                hasErrors = analysis.HasErrors,
                hasStructuralFailure = analysis.HasStructuralFailure,
                wasTerminatedEarly = analysis.AnalysisWasTerminatedEarly,
                ast = LowerToNodeData(expr),
                telemetry = new {
                    passCount = analysis.Telemetry.Passes.Count,
                    passes = analysis.Telemetry.Passes.Select(p => new { name = p.PassName, elapsed = p.Elapsed.TotalMilliseconds }),
                    totalWallClock = analysis.Telemetry.TotalElapsed.TotalMilliseconds,
                    incremental = analysis.Telemetry.Incremental
                }
            }, Affordances: ["lower_expression", "describe_expression", "lower_expression_to_csharp"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Analysis failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── V0.3c: analyze_effect ──────────────────────────────

    [McpServerTool(Name = "analyze_effect"), Description("Lowers an action's effects through the Syntax AST pipeline and runs the Interpreter's analysis pipeline. Returns diagnostics, side-effect classification, and analysis metadata for the action's effects. Specify entityName and actionName. Optionally set stageName for stage-scoped actions.")]
    public static DomainToolResponse AnalyzeEffect(
        [Description("Session ID")] string sessionId,
        [Description("Entity name")] string entityName,
        [Description("Action name")] string actionName,
        [Description("Optional stage name for stage-scoped actions")] string? stageName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        var entity = state.Domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(Success: false, Message: $"Entity '{entityName}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add_entity"]);

        DomainModeling.Action? action = null;
        if (stageName is not null) {
            var stage = entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, stageName, StringComparison.Ordinal));
            if (stage is null)
                return new DomainToolResponse(Success: false, Message: $"Stage '{stageName}' not found on Entity '{entityName}'.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
            action = stage.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }
        else {
            action = entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }

        if (action is null)
            return new DomainToolResponse(Success: false, Message: $"Action '{actionName}' not found{(stageName is not null ? $" on stage '{stageName}'" : "")} on Entity '{entityName}'.", SessionId: sessionId, Affordances: ["get_entity_detail"]);

        try {
            var paramNames = new HashSet<string>(action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
            var context = new LoweringContext(
                new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
                Analysis: state.LatestAnalysis,
                UseThisReference: true,
                ActionParameterNames: paramNames,
                LowerStageTransitions: true,
                Domain: state.Domain
            );
            var effectPass = new EffectLoweringPass(entity, context);
            var composite = new CompositeEffect(action.Effects);
            var lowered = effectPass.TryLowerVmNode(composite);

            if (lowered is null)
                return new DomainToolResponse(Success: false, Message: $"Action '{actionName}' has no VM-compilable effects.", SessionId: sessionId, Affordances: []);

            var analysis = Interpreter.Analyze(lowered);
            var diagnostics = analysis.Diagnostics.Select(d => new {
                severity = d.Severity.ToString(),
                code = d.Code,
                message = d.Message,
                nodeKind = d.Node?.GetType().Name
            }).ToList();

            return new DomainToolResponse(Success: true, Message: $"Effect analyzed: {diagnostics.Count} diagnostics.", SessionId: sessionId, Data: new {
                diagnostics,
                hasErrors = analysis.HasErrors,
                hasStructuralFailure = analysis.HasStructuralFailure,
                effectCount = action.Effects.Count,
                telemetry = new {
                    passCount = analysis.Telemetry.Passes.Count,
                    passes = analysis.Telemetry.Passes.Select(p => new { name = p.PassName, elapsedMs = p.Elapsed.TotalMilliseconds }),
                    totalWallClockMs = analysis.Telemetry.TotalElapsed.TotalMilliseconds
                }
            }, Affordances: ["lower_effect_to_csharp", "get_entity_detail"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Effect analysis failed: {ex.Message}", SessionId: sessionId, Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── V0.4: lower_expression_to_csharp ─────────────────────────

    [McpServerTool(Name = "lower_expression_to_csharp"), Description("Parses a JSON policy expression, lowers it through the Syntax AST pipeline, and generates C# source code. Returns analysis errors if the expression cannot compile. No session required.")]
    public static DomainToolResponse LowerExpressionToCSharp(
        [Description("JSON expression string (same format as add_policy). Example: {\"and\":[{\"property\":\"Age\",\"op\":\">=\",\"value\":18},{\"property\":\"Active\",\"op\":\"==\",\"value\":true}]}")] string expressionJson) {
        var failure = TryParseExpression(expressionJson, out var expr);
        if (failure is not null) return failure;
        try {
            var lowered = LowerExpressionToNode(expr);
            return CSharpWithAnalysis(lowered, "Expression lowered to C# successfully.",
                buildData: csharp => new { csharp, ast = LowerToNodeData(expr) },
                affordances: ["lower_expression", "describe_expression", "analyze_expression"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"C# lowering failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── V0.5: lower_effect_to_csharp ─────────────────────────────

    [McpServerTool(Name = "lower_effect_to_csharp"), Description("Generates C# source code for an action's lowered effects from the session domain. Specify entityName and actionName. Optionally set stageName for stage-scoped actions.")]
    public static DomainToolResponse LowerEffectToCSharp(
        [Description("Session ID")] string sessionId,
        [Description("Entity name")] string entityName,
        [Description("Action name")] string actionName,
        [Description("Optional stage name for stage-scoped actions")] string? stageName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        var entity = state.Domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(Success: false, Message: $"Entity '{entityName}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add_entity"]);

        // Find the action
        DomainModeling.Action? action = null;
        if (stageName is not null) {
            var stage = entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, stageName, StringComparison.Ordinal));
            if (stage is null)
                return new DomainToolResponse(Success: false, Message: $"Stage '{stageName}' not found on Entity '{entityName}'.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
            action = stage.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }
        else {
            action = entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }

        if (action is null)
            return new DomainToolResponse(Success: false, Message: $"Action '{actionName}' not found{(stageName is not null ? $" on stage '{stageName}'" : "")} on Entity '{entityName}'.", SessionId: sessionId, Affordances: ["get_entity_detail"]);

        try {
            var paramNames = new HashSet<string>(action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
            var context = new LoweringContext(
                new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
                Analysis: state.LatestAnalysis,
                UseThisReference: true,
                ActionParameterNames: paramNames,
                LowerStageTransitions: true,
                Domain: state.Domain
            );
            var effectPass = new EffectLoweringPass(entity, context);
            // Lower the action as a composite of all its effects
            var composite = new CompositeEffect(action.Effects);
            var lowered = effectPass.TryLowerVmNode(composite);

            if (lowered is null)
                return new DomainToolResponse(Success: false, Message: $"Action '{actionName}' has no VM-compilable effects. All its effects are direct-execution only (transition, create, invoke, delete, link).", SessionId: sessionId, Affordances: ["lower_expression_to_csharp"]);

            var result = CSharpWithAnalysis(lowered, $"Action '{actionName}' lowered to C# successfully.",
                buildData: csharp => new { csharp, effectCount = action.Effects.Count },
                affordances: ["get_entity_detail", "lower_expression_to_csharp", "analyze_effect"]);
            return result with { SessionId = sessionId };
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"C# lowering failed: {ex.Message}", SessionId: sessionId, Data: new { error = ex.Message }, Affordances: []);
        }
    }

    /// <summary>
    /// Parses and lowers a DomainExpression to a Syntax AST Node.
    /// </summary>
    private static Ast.Node LowerExpressionToNode(DomainExpression expr) {
        var pass = new DomainExpressionLoweringPass();
        return pass.Lower(expr, new Syntactic.Parameter("entity"));
    }

    // ── V0.6: export_domain_to_csharp ────────────────────────────

    [McpServerTool(Name = "export_domain_to_csharp"), Description("Generates C# source code for an entire domain session as a set of record/class definitions. Each entity becomes a C# record with its properties, navigation properties (as collections for many, references for one), stages as enums or additional state, and actions as methods with their lowered effects as the method body. Useful for inspecting how a domain model maps to C#.")]
    public static DomainToolResponse ExportDomainToCSharp(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        try {
            var exporter = new DomainToCSharpExporter();
            var typeDefs = exporter.Export(state.Domain, state.LatestAnalysis);
            var generator = new CSharpGenerator();
            var csharp = generator.Generate(typeDefs);
            return new DomainToolResponse(Success: true, Message: $"Domain exported to C#: {typeDefs.Count} types.", SessionId: sessionId, Data: new { csharp, typeCount = typeDefs.Count }, Affordances: ["get_domain_overview", "get_entity_detail", "apply_dsl"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Domain-to-C# export failed: {ex.Message}", SessionId: sessionId, Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── DTO types ───────────────────────────────────────────────

    internal sealed record LoweredNodeData(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("detail")] string? Detail,
        [property: JsonPropertyName("children")] IReadOnlyList<LoweredNodeData>? Children
    );

    internal sealed record DescribeExpressionData(
        [property: JsonPropertyName("structured")] string Structured,
        [property: JsonPropertyName("plainEnglish")] string PlainEnglish
    );

    internal sealed record DomainElementData(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("parentEntity")] string? ParentEntity,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("expression")] DescribeExpressionData? Expression = null
    );

    // ── V0.1: lower_expression ──────────────────────────────────

    [McpServerTool(Name = "lower_expression"), Description("Parses a JSON policy expression and lowers it through the Syntax AST pipeline. Returns the structured AST tree for inspection — no session required.")]
    public static DomainToolResponse LowerExpression(
        [Description("JSON expression string (same format as add_policy). Comparison: {\"property\":\"Age\",\"op\":\">=\",\"value\":18}. Composite: {\"and\":[{...},{...}]}, {\"or\":[...]}, {\"not\":{...}}. Literal: {\"literal\":true}.")] string expressionJson) {
        var failure = TryParseExpression(expressionJson, out var expr);
        if (failure is not null) return failure;
        try {
            var lowered = LowerToNodeData(expr);
            return new DomainToolResponse(Success: true, Message: "Expression lowered successfully.", Data: new { ast = lowered }, Affordances: ["describe_expression", "add_policy", "analyze_expression"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Lowering failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── V0.2: describe_expression ───────────────────────────────

    [McpServerTool(Name = "describe_expression"), Description("Parses a JSON policy expression and returns a structured breakdown plus a plain-English description. No session required.")]
    public static DomainToolResponse DescribeExpression(
        [Description("JSON expression string (same format as add_policy).")] string expressionJson) {
        var failure = TryParseExpression(expressionJson, out var expr);
        if (failure is not null) return failure;
        try {
            var description = DescribeExpression(expr);
            return new DomainToolResponse(Success: true, Message: "Expression described successfully.", Data: description, Affordances: ["lower_expression", "add_policy", "analyze_expression"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Description failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── V0.3: describe_domain_element ───────────────────────────

    [McpServerTool(Name = "describe_domain_element"), Description("Returns a structured description of a domain element (entity, stage, action, policy, or relationship) from the session domain. For stage/action/policy, specify entityName if the name is not unique across entities.")]
    public static DomainToolResponse DescribeDomainElement(
        [Description("Session ID")] string sessionId,
        [Description("Element kind: 'entity', 'stage', 'action', 'policy', or 'relationship'")] string kind,
        [Description("Element name")] string name,
        [Description("Optional entity name to disambiguate stage/action/policy (recommended when multiple entities share the same element name)")] string? entityName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);

        try {
            return kind.ToLowerInvariant() switch {
                "entity" => DescribeEntity(sessionId, state, name),
                "stage" => DescribeStage(sessionId, state, name, entityName),
                "action" => DescribeAction(sessionId, state, name, entityName),
                "policy" => DescribePolicy(sessionId, state, name, entityName),
                "relationship" => DescribeRelationship(sessionId, state, name),
                _ => new DomainToolResponse(Success: false, Message: $"Unknown element kind '{kind}'. Use: entity, stage, action, policy, or relationship.", SessionId: sessionId, Affordances: ["get_entity_detail", "get_domain_overview"])
            };
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Failed to describe element: {ex.Message}", SessionId: sessionId, Affordances: ["get_entity_detail", "get_domain_overview"]);
        }
    }

    private static DomainToolResponse DescribeEntity(string sessionId, McpSessionState state, string name) {
        var detail = DomainQueries.GetEntity(state.Domain, name, state.LatestAnalysis);
        if (detail is null) return new DomainToolResponse(Success: false, Message: $"Entity '{name}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add_entity"]);

        var sb = new StringBuilder();
        sb.Append($"Entity '{name}' with {detail.Properties.Count} properties, {detail.Stages.Count} stages, {detail.Actions.Count} actions, {detail.Policies.Count} policies.");

        if (detail.Navigations.Count > 0) sb.Append($" Navigations: {string.Join(", ", detail.Navigations.Select(n => n.RelationshipName))}.");

        return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("entity", name, null, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail", "get_domain_analysis"]);
    }

    private static DomainToolResponse DescribeStage(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Structural entity enumeration (projection) + ESM-backed stage resolution only.
        // Missing EntityStructureMetadata ≠ not-found.
        var analysis = state.LatestAnalysis;
        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        var missingStructure = false;
        foreach (var entity in entities) {
            if (analysis.GetMetadata<EntityStructureMetadata>(entity) is null) {
                missingStructure = true;
                continue;
            }
            if (!analysis.TryGetStage(entity, name, out var stage) || stage is null)
                continue;

            var effectiveActionCount = analysis.GetEffectiveActions(state.Domain, entity, name).Count;
            var effectivePolicyCount = analysis.GetEffectivePolicies(state.Domain, entity, name).Count;

            var sb = new StringBuilder();
            sb.Append($"Stage '{name}' on entity '{entity.Name}' with {effectiveActionCount} effective actions, {effectivePolicyCount} effective policies, {stage.Subscriptions.Count} subscriptions.");
            if (stage.OnEntryEffects.Count > 0) sb.Append($" Has {stage.OnEntryEffects.Count} entry effect(s).");
            if (stage.OnExitEffects.Count > 0) sb.Append($" Has {stage.OnExitEffects.Count} exit effect(s).");
            if (effectiveActionCount > stage.Actions.Count)
                sb.Append($" Includes {effectiveActionCount - stage.Actions.Count} inherited action(s).");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("stage", name, entity.Name, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail"]);
        }

        if (missingStructure)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing EntityStructureMetadata required to describe stage '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        return new DomainToolResponse(Success: false, Message: $"Stage '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_domain_overview"]);
    }

    private static DomainToolResponse DescribeAction(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog action map only. Missing catalog ≠ not-found.
        var analysis = state.LatestAnalysis;
        if (analysis.GetCatalog(state.Domain) is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata required to describe action '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        foreach (var entity in entities) {
            // Entity-first search priority: this is a describe/search tool, not
            // runtime dispatch. TryResolveAction uses stage-first + SA semantics
            // for execution; entity-first is preferred here for broad search.
            var arm = analysis.GetActionResolution(state.Domain, entity);
            Poly.DomainModeling.Action? action = null;
            if (arm is not null) {
                arm.EntityActions.TryGetValue(name, out action);
                if (action is null) {
                    foreach (var kv in arm.StageActions) {
                        if (kv.Value.TryGetValue(name, out var sa)) {
                            action = sa;
                            break;
                        }
                    }
                }
            }
            if (action is null) continue;

            var actionCap = analysis.GetMetadata<ActionCapabilityMetadata>(action);
            var transitionTargets = actionCap?.View.TransitionTargets.Select(t => t.Name).ToList() ?? [];

            var sb = new StringBuilder();
            sb.Append($"Action '{name}' on entity '{entity.Name}'");
            if (action.Parameters.Count > 0) sb.Append($" with parameters: {string.Join(", ", action.Parameters.Select(p => $"{p.Name}: {p.Type.TypeName}"))}");
            sb.Append($", {action.Effects.Count} effect(s), {action.Policies.Count} guard(s).");
            if (transitionTargets.Count > 0)
                sb.Append($" Transitions to: {string.Join(", ", transitionTargets)}.");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("action", name, entity.Name, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail"]);
        }

        return new DomainToolResponse(Success: false, Message: $"Action '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
    }

    private static DomainToolResponse DescribePolicy(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog mutation index only. Missing index ≠ not-found.
        var analysis = state.LatestAnalysis;
        var mti = analysis.GetMutationIndex(state.Domain);
        if (mti is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata (mutation index) required to describe policy '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        foreach (var entity in entities) {
            Policy? policy = null;
            string? scope = null;

            if (mti.EntityPoliciesByEntity.TryGetValue(entity.Name, out var entityPols)
                && entityPols.TryGetValue(name, out var ep)) {
                policy = ep;
                scope = "entity";
            }

            if (policy is null
                && mti.StagePoliciesByEntity.TryGetValue(entity.Name, out var stagePols)) {
                foreach (var kv in stagePols) {
                    if (kv.Value.TryGetValue(name, out var sp)) {
                        policy = sp;
                        scope = $"stage '{kv.Key}'";
                        break;
                    }
                }
            }

            if (policy is null
                && mti.ActionPoliciesByEntity.TryGetValue(entity.Name, out var actionPols)) {
                foreach (var kv in actionPols) {
                    if (kv.Value.TryGetValue(name, out var ap)) {
                        policy = ap;
                        scope = $"action '{kv.Key}'";
                        break;
                    }
                }
            }

            if (policy is null) continue;

            var exprDescription = DescribeExpression(policy.Expression);
            var sb = new StringBuilder();
            sb.Append($"Policy '{name}' on entity '{entity.Name}'");
            if (scope is not null) sb.Append($" ({scope} scope)");
            sb.Append($": {exprDescription.PlainEnglish}.");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("policy", name, entity.Name, sb.ToString(), sb.ToString(), exprDescription), Affordances: ["get_policy_expression", "get_entity_detail"]);
        }

        return new DomainToolResponse(Success: false, Message: $"Policy '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
    }

    private static DomainToolResponse DescribeRelationship(string sessionId, McpSessionState state, string name) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog relationship lookup only. Missing catalog ≠ not-found.
        var analysis = state.LatestAnalysis;
        if (analysis.GetRelationshipLookup(state.Domain) is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata (relationship lookup) required to describe relationship '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        if (!analysis.TryGetRelationship(state.Domain, name, out var rel) || rel is null)
            return new DomainToolResponse(Success: false, Message: $"Relationship '{name}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add_relationship"]);

        var cardinality = rel.Cardinality switch { RelationshipCardinality.OneToOne => "one-to-one", RelationshipCardinality.OneToMany => "one-to-many", RelationshipCardinality.ManyToMany => "many-to-many", _ => rel.Cardinality.ToString() };
        var sb = new StringBuilder();
        sb.Append($"Relationship '{name}': {rel.Source.TypeName} → {rel.Target.TypeName} ({cardinality})");
        if (rel.SourceOwnsTarget) sb.Append(", source owns target");
        if (rel.Stages.Count > 0) sb.Append($", {rel.Stages.Count} stage(s)");
        return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("relationship", name, null, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail", "get_domain_overview"]);
    }

    // ── S0: simulate_policy ────────────────────────────────────

    [McpServerTool(Name = "simulate_policy"), Description("Simulates a JSON policy expression against a sample subject properties bag — no session required. Returns {'result': true/false} from the VM evaluation path, matching the same engine used by add_policy + evaluate_policy.")]
    public static DomainToolResponse SimulatePolicy(
        [Description("JSON expression string (same format as add_policy).")] string expressionJson,
        [Description("JSON object of property values, e.g. \"{\\\"Age\\\":25,\\\"Status\\\":\\\"Active\\\"}\"")] string propertiesJson) {
        // Parse expression
        var parseFailure = TryParseExpression(expressionJson, out var expr);
        if (parseFailure is not null) return parseFailure;

        // Parse subject properties
        Dictionary<string, object?> subjectValues;
        try {
            subjectValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(propertiesJson)
                ?.ToDictionary(kv => kv.Key, kv => JsonElementToClrValue(kv.Value), StringComparer.Ordinal)
                ?? throw new ArgumentException("Failed to parse properties JSON.");
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid subject properties: {ex.Message}", Affordances: []);
        }

        if (subjectValues.Count == 0) {
            return new DomainToolResponse(Success: false, Message: "Subject properties must not be empty.", Affordances: []);
        }

        // G1: Fail closed — validate all property references exist in the subject bag.
        var missingProperties = CollectPropertyNames(expr)
            .Where(p => !subjectValues.ContainsKey(p))
            .ToList();
        if (missingProperties.Count > 0) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Expression references properties not present in the subject properties bag: [{string.Join(", ", missingProperties)}]. " +
                         "Provide values for these properties in the propertiesJson parameter.",
                Affordances: ["lower_expression", "describe_expression"]);
        }

        try {
            // Infer property types from the expression to ensure VM compatibility
            var propertyTypes = InferPropertyTypes(expr);
            var props = subjectValues.Keys
                .Select(k => new Property(k, new DomainTypeReference(
                    propertyTypes.TryGetValue(k, out var t) ? t : "Text"), []))
                .ToList();
            var entity = new Entity("Subject", props, [], [], []);
            var policy = new Policy("_sim", expr);
            var instance = DomainEntityInstance.Create(entity, subjectValues);
            var result = instance.EvaluatePolicy(policy);

            var data = new { result };
            return new DomainToolResponse(Success: true, Message: result ? "Expression passed (true)." : "Expression failed (false).", Data: data, Affordances: ["lower_expression", "describe_expression", "add_policy"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Simulation failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    /// <summary>Collects all <see cref="PropertyAccess"/> names from an expression tree.</summary>
    private static HashSet<string> CollectPropertyNames(DomainExpression expr) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(expr);
        return names;

        void Walk(DomainExpression e) {
            switch (e) {
                case PropertyAccess pa:
                    names.Add(pa.Name);
                    break;
                case OwnedAccess oa:
                    Walk(oa.Inner);
                    break;
                case Exists ex:
                    Walk(ex.Target);
                    break;
                case NotExists nex:
                    Walk(nex.Target);
                    break;
                case Subtract sub:
                    Walk(sub.Left); Walk(sub.Right);
                    break;
                case Add add:
                    Walk(add.Left); Walk(add.Right);
                    break;
                case Multiply mul:
                    Walk(mul.Left); Walk(mul.Right);
                    break;
                case Divide div:
                    Walk(div.Left); Walk(div.Right);
                    break;
                case DateOperation dOp:
                    Walk(dOp.Date); Walk(dOp.Offset);
                    break;
                case RelationshipNavigation rn:
                    Walk(rn.TargetProperty);
                    break;
                case AnyExpr any:
                    Walk(any.Body);
                    break;
                case AllExpr all:
                    Walk(all.Body);
                    break;
                case NoneExpr none:
                    Walk(none.Body);
                    break;
                case CountExpr cnt:
                    if (cnt.Body is not null) Walk(cnt.Body);
                    break;
                case Comparison cmp:
                    Walk(cmp.Left); Walk(cmp.Right);
                    break;
                case And and:
                    Walk(and.Left); Walk(and.Right);
                    break;
                case Or or:
                    Walk(or.Left); Walk(or.Right);
                    break;
                case Not not:
                    Walk(not.Operand);
                    break;
                    // ParameterAccess, Literal — no property references
            }
        }
    }

    /// <summary>
    /// Walks a <see cref="DomainExpression"/> to infer property types from comparison literal values.
    /// Number literals → "Number", boolean literals → "Boolean", else → "Text".
    /// </summary>
    private static Dictionary<string, string> InferPropertyTypes(DomainExpression expr) {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        InferTypesCore(expr, types);
        return types;
    }

    private static void InferTypesCore(DomainExpression expr, Dictionary<string, string> types) {
        switch (expr) {
            case Comparison c:
                var leftProp = c.Left is PropertyAccess lp ? lp.Name : null;
                var rightLit = c.Right is Literal rl ? rl.Value : null;
                var rightProp = c.Right is PropertyAccess rp ? rp.Name : null;
                var leftLit = c.Left is Literal ll ? ll.Value : null;

                if (leftProp is not null && rightLit is not null)
                    types[leftProp] = InferClrType(rightLit);
                if (rightProp is not null && leftLit is not null)
                    types[rightProp] = InferClrType(leftLit);
                InferTypesCore(c.Left, types);
                InferTypesCore(c.Right, types);
                break;
            case And a:
                InferTypesCore(a.Left, types);
                InferTypesCore(a.Right, types);
                break;
            case Or o:
                InferTypesCore(o.Left, types);
                InferTypesCore(o.Right, types);
                break;
            case Not n:
                InferTypesCore(n.Operand, types);
                break;
            case Add a:
                InferTypesCore(a.Left, types);
                InferTypesCore(a.Right, types);
                break;
            case Subtract s:
                InferTypesCore(s.Left, types);
                InferTypesCore(s.Right, types);
                break;
            case Multiply m:
                InferTypesCore(m.Left, types);
                InferTypesCore(m.Right, types);
                break;
            case Divide d:
                InferTypesCore(d.Left, types);
                InferTypesCore(d.Right, types);
                break;
            case Exists e:
                InferTypesCore(e.Target, types);
                break;
            case NotExists ne:
                InferTypesCore(ne.Target, types);
                break;
            case OwnedAccess oa:
                InferTypesCore(oa.Inner, types);
                break;
            case RelationshipNavigation rn:
                InferTypesCore(rn.TargetProperty, types);
                break;
        }
    }

    private static string InferClrType(object? value) => value switch {
        long or int or double or decimal or float => "Number",
        bool => "Boolean",
        _ => "Text"
    };

    private static object? JsonElementToClrValue(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number when je.TryGetInt32(out var i) => (long)i,
        JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        JsonValueKind.Number => (long)je.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => je.GetRawText()
    };
}