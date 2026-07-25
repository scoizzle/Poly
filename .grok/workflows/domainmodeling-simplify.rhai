let meta = #{
    name: "domainmodeling-simplify",
    description: "Review DomainModeling for simplification opportunities; verify and rank concrete cuts",
    when_to_use: "When seeking to simplify DomainModeling (passes, lowering, evolution, DSL, codegen seams) without inventing parallel machinery",
    phases: [
        #{ title: "Map", detail: "inventory modules and hotspots under Poly/DomainModeling" },
        #{ title: "Review", detail: "parallel simplification passes by concern" },
        #{ title: "Verify", detail: "adversarial check of each opportunity" },
        #{ title: "Synthesize", detail: "ranked report of real, safe simplifications" },
    ],
};

let map_schema = #{
    "type": "object",
    "required": ["modules", "hotspots", "notes"],
    "properties": #{
        "modules": #{
            "type": "array",
            "maxItems": 20,
            "items": #{
                "type": "object",
                "required": ["path", "role"],
                "properties": #{
                    "path": #{ "type": "string" },
                    "role": #{ "type": "string" },
                },
            },
        },
        "hotspots": #{
            "type": "array",
            "maxItems": 12,
            "items": #{ "type": "string" },
        },
        "notes": #{ "type": "string" },
    },
};

let opportunity_schema = #{
    "type": "object",
    "required": ["opportunities"],
    "properties": #{
        "opportunities": #{
            "type": "array",
            "maxItems": 8,
            "items": #{
                "type": "object",
                "required": ["id", "title", "area", "effort", "risk", "why_simpler", "evidence_paths"],
                "properties": #{
                    "id": #{ "type": "string" },
                    "title": #{ "type": "string" },
                    "area": #{ "type": "string" },
                    "effort": #{ "type": "string" },
                    "risk": #{ "type": "string" },
                    "why_simpler": #{ "type": "string" },
                    "evidence_paths": #{
                        "type": "array",
                        "maxItems": 6,
                        "items": #{ "type": "string" },
                    },
                    "proposed_cut": #{ "type": "string" },
                },
            },
        },
    },
};

let verdict_schema = #{
    "type": "object",
    "required": ["real", "reason", "evidence", "safer_variant"],
    "properties": #{
        "real": #{ "type": "boolean" },
        "reason": #{ "type": "string" },
        "evidence": #{ "type": "string" },
        "safer_variant": #{ "type": "string" },
    },
};

let report_schema = #{
    "type": "object",
    "required": ["summary", "ranked", "do_not_do", "next_slice"],
    "properties": #{
        "summary": #{ "type": "string" },
        "ranked": #{
            "type": "array",
            "maxItems": 12,
            "items": #{
                "type": "object",
                "required": ["title", "priority", "effort", "risk", "proposed_cut", "why"],
                "properties": #{
                    "title": #{ "type": "string" },
                    "priority": #{ "type": "string" },
                    "effort": #{ "type": "string" },
                    "risk": #{ "type": "string" },
                    "proposed_cut": #{ "type": "string" },
                    "why": #{ "type": "string" },
                },
            },
        },
        "do_not_do": #{
            "type": "array",
            "maxItems": 8,
            "items": #{ "type": "string" },
        },
        "next_slice": #{ "type": "string" },
    },
};

// ── args ──────────────────────────────────────────────────────────
// Optional: args.focus (string free-text), args.max_per_dimension (number as string/int)
let focus = if args == () { () } else { args.focus };
let focus_text = if focus == () { "general simplification across DomainModeling" } else { focus };

// ── Map ───────────────────────────────────────────────────────────
phase("Map");
log("Mapping DomainModeling modules and simplification hotspots");

let map_prompt = "";
map_prompt += "You are mapping Poly DomainModeling for a SIMPLIFICATION review.\n";
map_prompt += "Scope root: Poly/DomainModeling/ (also note consumers: Poly.Mcp, src/Poly.DslCompiler, Poly.Tests/DomainModeling).\n";
map_prompt += "REQUIRED: use list_dir, grep, and read_file. Do not invent paths from memory.\n";
map_prompt += "Read at least: docs/CORE.md (DomainModeling section), Poly/DomainModeling/README.md if present, ";
map_prompt += "Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs, and skim Lowering/ + Evolution/ + Parsing/.\n";
map_prompt += "Focus theme: " + focus_text + "\n";
map_prompt += "Return modules[{path,role}] (top folders/files that matter) and hotspots (concrete duplication, dual pipelines, dead/legacy surfaces, oversized files).\n";
map_prompt += "Empty hotspots only if you truly found none after reading the tree.\n";

let map_r = agent(map_prompt, #{
    label: "map-domainmodeling",
    capability_mode: "read-only",
    output_schema: map_schema,
});

let map_notes = "";
let map_hotspots = [];
if map_r != () && map_r.success && map_r.output != () {
    if map_r.output.notes != () { map_notes = map_r.output.notes; }
    if map_r.output.hotspots != () { map_hotspots = map_r.output.hotspots; }
}
log("Map complete; hotspots=" + map_hotspots.len().to_string());

// ── Review (parallel dimensions) ──────────────────────────────────
phase("Review");

let dimensions = [
    #{
        key: "analysis-pipeline",
        title: "Analysis pipeline",
        prompt_extra: "DomainModelAnalyzer registration, number of INodeAnalyzer passes, Dependencies, pass vs Lowering/*Analyzer wrappers, dead diagnostics, metadata bags that overlap.",
    },
    #{
        key: "lowering-codegen",
        title: "Lowering and codegen",
        prompt_extra: "DomainExpressionLoweringPass, PolicyEvaluator, Aggregate/Behavior/Topology/Storage/Transport, DslCompiler dual pipeline, GenerationAssertions, IR vs string emit leftovers.",
    },
    #{
        key: "domain-surface",
        title: "Domain model surface",
        prompt_extra: "Entity/Action/Stage/Policy/Effect types, Builders/, Constraints/, Annotations, DomainExpression shape complexity, duplication with DSL keywords.",
    },
    #{
        key: "evolution-runtime",
        title: "Evolution and runtime",
        prompt_extra: "DomainEvolution Apply, DomainEntityInstance, DomainInstanceStore, effect/action dispatch, link/unlink, evaluate_policy paths — accidental complexity vs needed fidelity.",
    },
    #{
        key: "dsl-mcp-docs",
        title: "DSL MCP and docs",
        prompt_extra: "PolyDslParser/Printer, Poly.Mcp tools honesty, poly-dsl-guide, plans vs shipped reality, naming cleanup (V3 labels), guide drift.",
    },
    #{
        key: "tests-dogfood",
        title: "Tests and dogfood cost",
        prompt_extra: "Poly.Tests/DomainModeling size, dual-oracle patterns, fixture explosion, what could be fewer more generic tests vs special-case tests that freeze complexity.",
    },
];

let review_jobs = [];
for d in dimensions {
    let p = "";
    p += "SIMPLIFICATION review of Poly DomainModeling — dimension: " + d.title + ".\n";
    p += "Theme: " + focus_text + "\n";
    p += "Map notes (may be partial): " + map_notes + "\n";
    p += "Known hotspots from map: " + json_encode(map_hotspots) + "\n";
    p += "Focus this dimension: " + d.prompt_extra + "\n\n";
    p += "RULES (Poly AGENTS / CORE):\n";
    p += "- Prefer domain fidelity and CORE seams over locally clever renames.\n";
    p += "- Prefer deleting dual paths over adding abstraction frameworks.\n";
    p += "- Working code before extracted abstractions; no pattern catalogs.\n";
    p += "- Simplification = fewer concepts, fewer special cases, clearer ownership — not more generic frameworks.\n";
    p += "- Do NOT propose moving Storage into always-on domain analysis without packs; do NOT invent domain VM opcodes.\n\n";
    p += "REQUIRED TOOL USE: grep + read_file on Poly/DomainModeling and relevant consumers. ";
    p += "Cite real file paths in evidence_paths. Empty opportunities only after inspecting code.\n";
    p += "For each opportunity: id (short slug), title, area, effort (S/M/L), risk (low/med/high), ";
    p += "why_simpler (customer/operator outcome), proposed_cut (smallest coherent cut), evidence_paths.\n";
    p += "Max 8 opportunities. Prefer concrete delete/merge/inline over vague refactors.\n";

    review_jobs.push(#{
        prompt: p,
        label: "review:" + d.key,
        capability_mode: "read-only",
        output_schema: opportunity_schema,
    });
}

let review_results = parallel(review_jobs);

let opportunities = [];
let dim_i = 0;
for r in review_results {
    if r != () && r.success && r.output != () && r.output.opportunities != () {
        for o in r.output.opportunities {
            // Tag area if empty-ish with dimension key for later grouping
            opportunities.push(o);
        }
    } else {
        log("Review slot failed or empty at index " + dim_i.to_string());
    }
    dim_i += 1;
}

log("Collected " + opportunities.len().to_string() + " raw opportunities");

if opportunities.len() == 0 {
    complete(#{
        summary: "No simplification opportunities found (or all review agents failed). Re-run with a narrower args.focus.",
        ranked: [],
        do_not_do: [],
        next_slice: "Re-run after ensuring DomainModeling is readable; pass args.focus e.g. \"analysis pipeline passes\".",
    });
}

// Cap verification fan-out
const MAX_VERIFY = 12;
let to_verify = [];
let oi = 0;
for o in opportunities {
    if to_verify.len() >= MAX_VERIFY { break; }
    to_verify.push(o);
    oi += 1;
}
if opportunities.len() > MAX_VERIFY {
    log("Truncated verification to " + MAX_VERIFY.to_string() + " of " + opportunities.len().to_string());
}

// ── Verify ────────────────────────────────────────────────────────
phase("Verify");
log("Adversarially verifying " + to_verify.len().to_string() + " opportunities");

let vjobs = [];
for o in to_verify {
    let title = if o.title != () { o.title } else { "untitled" };
    let why = if o.why_simpler != () { o.why_simpler } else { "" };
    let cut = if o.proposed_cut != () { o.proposed_cut } else { "" };
    let paths = if o.evidence_paths != () { json_encode(o.evidence_paths) } else { "[]" };

    let vp = "";
    vp += "Adversarially verify this DomainModeling SIMPLIFICATION opportunity.\n";
    vp += "Claim title: " + title + "\n";
    vp += "Why simpler: " + why + "\n";
    vp += "Proposed cut: " + cut + "\n";
    vp += "Cited paths: " + paths + "\n\n";
    vp += "You MUST open the cited paths (read_file/grep) and try to REFUTE the claim.\n";
    vp += "Set real=true ONLY if: (1) the complexity exists in code, (2) the cut reduces concepts/special cases without breaking CORE seams, ";
    vp += "(3) a second real use or dead path justifies the change — not pure aesthetic rename.\n";
    vp += "Default real=false when evidence is thin, when the cut adds framework, or when it would warp domain fidelity.\n";
    vp += "evidence: quote what you read. safer_variant: if real=false but a thinner cut exists, state it; else empty string.\n";

    vjobs.push(#{
        prompt: vp,
        label: "verify:" + title,
        capability_mode: "read-only",
        output_schema: verdict_schema,
    });
}

let verdicts = parallel(vjobs);

let confirmed = [];
let rejected = [];
let vi = 0;
for v in verdicts {
    let claim = to_verify[vi];
    if v != () && v.success && v.output != () && v.output.real == true
        && v.output.evidence != () && v.output.evidence != "" {
        confirmed.push(#{
            claim: claim,
            reason: v.output.reason,
            evidence: v.output.evidence,
            safer_variant: v.output.safer_variant,
        });
    } else {
        let reason = "unverified";
        if v != () && v.success && v.output != () && v.output.reason != () {
            reason = v.output.reason;
        }
        rejected.push(#{ claim: claim, reason: reason });
    }
    vi += 1;
}

log(confirmed.len().to_string() + "/" + to_verify.len().to_string() + " opportunities survived verification");

// ── Synthesize ────────────────────────────────────────────────────
phase("Synthesize");

let syn = "";
syn += "Synthesize a ranked DomainModeling simplification report for Poly.\n";
syn += "Focus theme: " + focus_text + "\n";
syn += "Map notes: " + map_notes + "\n\n";
syn += "CONFIRMED opportunities (JSON): " + json_encode(confirmed) + "\n\n";
syn += "REJECTED / unverified (JSON): " + json_encode(rejected) + "\n\n";
syn += "Produce:\n";
syn += "- summary: 1 short paragraph on the shape of accidental complexity.\n";
syn += "- ranked: priority P0/P1/P2, effort S/M/L, risk, proposed_cut, why — only confirmed items (or safer_variant if better).\n";
syn += "- do_not_do: anti-patterns (new abstraction layers, Storage always-on, second IR, etc.).\n";
syn += "- next_slice: the single smallest coherent first PR.\n";
syn += "Prefer CORE-aligned cuts. No fluff.\n";

let syn_r = agent(syn, #{
    label: "synthesize-report",
    capability_mode: "read-only",
    output_schema: report_schema,
});

if syn_r == () || !syn_r.success || syn_r.output == () {
    let path = write_scratch_file("domainmodeling-simplify-report.md",
        "# DomainModeling simplification (partial)\n\nSynthesizer failed. Confirmed count: "
        + confirmed.len().to_string() + "\n\nRaw confirmed JSON:\n\n```\n"
        + json_encode(confirmed) + "\n```\n");
    complete(#{
        summary: "Synthesizer failed; raw confirmed opportunities written to scratch.",
        path: path,
        confirmed_count: confirmed.len(),
        ranked: [],
        do_not_do: ["Do not trust unranked raw list without re-run"],
        next_slice: "Re-run synthesize phase or manually triage confirmed JSON.",
    });
}

let report = syn_r.output;

// Markdown artifact for the pager
let md = "";
md += "# DomainModeling simplification review\n\n";
md += report.summary + "\n\n";
md += "## Ranked opportunities\n\n";
if report.ranked != () {
    for item in report.ranked {
        let t = if item.title != () { item.title } else { "?" };
        let pri = if item.priority != () { item.priority } else { "?" };
        let eff = if item.effort != () { item.effort } else { "?" };
        let rk = if item.risk != () { item.risk } else { "?" };
        let cut = if item.proposed_cut != () { item.proposed_cut } else { "" };
        let why = if item.why != () { item.why } else { "" };
        md += "### [" + pri + "] " + t + "\n";
        md += "- Effort: " + eff + " · Risk: " + rk + "\n";
        md += "- Cut: " + cut + "\n";
        md += "- Why: " + why + "\n\n";
    }
}
md += "## Do not do\n\n";
if report.do_not_do != () {
    for dnd in report.do_not_do {
        md += "- " + dnd + "\n";
    }
}
md += "\n## Next slice\n\n";
md += if report.next_slice != () { report.next_slice } else { "(none)" };
md += "\n\n---\nVerified " + confirmed.len().to_string() + "/" + to_verify.len().to_string();
md += " · rejected " + rejected.len().to_string() + " · focus: " + focus_text + "\n";

let report_path = write_scratch_file("domainmodeling-simplify-report.md", md);
log("Wrote " + report_path);

complete(#{
    summary: report.summary,
    ranked: report.ranked,
    do_not_do: report.do_not_do,
    next_slice: report.next_slice,
    path: report_path,
    verified: confirmed.len(),
    rejected: rejected.len(),
});
