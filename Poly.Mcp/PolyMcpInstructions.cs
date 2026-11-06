namespace Poly.Mcp;

/// <summary>
/// The server-level framing an MCP client injects before any tool call. It answers the
/// question the tool list cannot: what is Poly for, when should an agent reach for it,
/// and what is the shortest path from a task to a verified domain model.
/// </summary>
public static class PolyMcpInstructions {
    public const string Text = """
        Poly is an executable ontology tool. Use it to describe the domain you are working in —
        entities, properties, constraints, stages, actions, policies — prove the description
        with the constraint solver, then export or simulate from that one model.

        Use when the task involves rules, state, lifecycle, validation, contradictions, a
        schema, an API contract, a simulation, or a human who needs to understand the domain.

        Loop:
        1. poly_guide — learn the .poly surface.
        2. poly_open — one workspace; keep its handle.
        3. poly_define — write one .poly document and apply it; apply:false dry-runs. Fix
           the findings until it is valid.
        4. poly_check — proven, violated, or unknown.
        5. poly_export (dsl, json-schema, csharp, markdown, html, json-ld, shacl) or
           poly_simulate. poly_inspect reads the model back.

        Rules: poly_define replaces the model and clears instances; a rejected define
        changes nothing. Every stateful call needs the handle; WORKSPACE_REQUIRED means
        call poly_open. Sessions are scratch; export dsl to keep one. Fix the model, never
        weaken a constraint to get past a diagnostic.
        """;
}