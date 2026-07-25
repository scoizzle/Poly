let meta = #{
    name: "design-integration-review",
    description: "Code review focused on cohesive system design and end-to-end feature integration",
    when_to_use: "When reviewing uncommitted work, a branch, or a path for architecture cohesion, CORE seams, and integrated behavior—not only local bugs",
    phases: [
        #{ title: "Scope", detail: "resolve target and map touched seams" },
        #{ title: "Review", detail: "parallel design/integration dimensions" },
        #{ title: "Verify", detail: "adversarial evidence check per finding" },
        #{ title: "Synthesize", detail: "ranked report with integration verdict" },
    ],
};

let scope_schema = #{
    "type": "object",
    "required": ["summary", "paths", "seams", "intent"],
    "properties": #{
        "summary": #{ "type": "string" },
        "paths": #{
            "type": "array",
            "maxItems": 40,
            "items": #{ "type": "string" },
        },
        "seams": #{
            "type": "array",
            "maxItems": 16,
            "items": #{ "type": "string" },
        },
        "intent": #{ "type": "string" },
    },
};

let findings_schema = #{
    "type": "object",
    "required": ["findings"],
    "properties": #{
        "findings": #{
            "type": "array",
            "maxItems": 8,
            "items": #{
                "type": "object",
                "required": ["id", "severity", "title", "dimension", "issue", "evidence_paths", "integration_gap"],
                "properties": #{
                    "id": #{ "type": "string" },
                    "severity": #{ "type": "string" },
                    "title": #{ "type": "string" },
                    "dimension": #{ "type": "string" },
                    "issue": #{ "type": "string" },
                    "evidence_paths": #{
                        "type": "array",
                        "maxItems": 6,
                        "items": #{ "type": "string" },
                    },
                    "integration_gap": #{ "type": "string" },
                    "suggested_fix": #{ "type": "string" },
                },
            },
        },
    },
};

let verdict_schema = #{
    "type": "object",
    "required": ["real", "reason", "evidence"],
    "properties": #{
        "real": #{ "type": "boolean" },
        "reason": #{ "type": "string" },
        "evidence": #{ "type": "string" },
        "severity_adjust": #{ "type": "string" },
    },
};

let report_schema = #{
    "type": "object",
    "required": ["summary", "cohesion_verdict", "ranked", "integration_checklist", "next_actions"],
    "properties": #{
        "summary": #{ "type": "string" },
        "cohesion_verdict": #{ "type": "string" },
        "ranked": #{
            "type": "array",
            "maxItems": 12,
            "items": #{
                "type": "object",
                "required": ["severity", "title", "issue", "suggested_fix"],
                "properties": #{
                    "severity": #{ "type": "string" },
                    "title": #{ "type": "string" },
                    "issue": #{ "type": "string" },
                    "suggested_fix": #{ "type": "string" },
                },
            },
        },
        "integration_checklist": #{
            "type": "array",
            "maxItems": 12,
            "items": #{ "type": "string" },
        },
        "next_actions": #{
            "type": "array",
            "maxItems": 8,
            "items": #{ "type": "string" },
        },
    },
};

// ── args ──────────────────────────────────────────────────────────
// args.target  — optional: path, branch, or "uncommitted" (default uncommitted)
// args.focus   — optional free-text theme
// args.base    — optional git base ref for diff (default HEAD)
let target = if args == () { () } else { args.target };
let focus = if args == () { () } else { args.focus };
let base = if args == () { () } else { args.base };

let target_text = if target == () { "uncommitted" } else { target };
let focus_text = if focus == () {
    "cohesive system design and end-to-end feature integration"
} else {
    focus
};
let base_text = if base == () { "HEAD" } else { base };

// ── Scope ─────────────────────────────────────────────────────────
phase("Scope");
log("Scoping design-integration review for " + target_text);

let scope_prompt = "";
scope_prompt += "You are scoping a DESIGN + INTEGRATION code review for the Poly repo.\n";
scope_prompt += "Target: " + target_text + "\n";
scope_prompt += "Base ref (for git): " + base_text + "\n";
scope_prompt += "Focus theme: " + focus_text + "\n\n";
scope_prompt += "REQUIRED TOOLS: run_terminal_command for git status/diff --stat, list_dir/grep/read_file as needed.\n";
scope_prompt += "If target is uncommitted or empty: git status, git diff --stat HEAD, skim git diff HEAD for hotspots.\n";
scope_prompt += "If target is a path: list and sample changed-related files under that path.\n";
scope_prompt += "If target looks like a branch: git log/diff vs base.\n\n";
scope_prompt += "Also read docs/CORE.md (placement + pipeline) and any plan files touched by the change if present under docs/plans/.\n\n";
scope_prompt += "Return:\n";
scope_prompt += "- summary: what the change appears to do (1 short paragraph)\n";
scope_prompt += "- paths: key files (real paths only)\n";
scope_prompt += "- seams: platform seams touched (e.g. DomainModeling Analysis, Lowering, DslCompiler, MCP, Syntax.Analysis, tests)\n";
scope_prompt += "- intent: customer/operator-visible intent or 'unclear'\n";
scope_prompt += "Do not invent paths. Empty paths only if the tree is clean and target is empty.\n";

let scope_r = agent(scope_prompt, #{
    label: "scope-change",
    capability_mode: "read-only",
    output_schema: scope_schema,
});

let scope_summary = "";
let scope_paths = [];
let scope_seams = [];
let scope_intent = "";
if scope_r != () && scope_r.success && scope_r.output != () {
    if scope_r.output.summary != () { scope_summary = scope_r.output.summary; }
    if scope_r.output.paths != () { scope_paths = scope_r.output.paths; }
    if scope_r.output.seams != () { scope_seams = scope_r.output.seams; }
    if scope_r.output.intent != () { scope_intent = scope_r.output.intent; }
}

if scope_paths.len() == 0 {
    log("No paths in scope — empty or unreadable target");
    complete(#{
        summary: "No reviewable paths for target=" + target_text + ". " + scope_summary,
        cohesion_verdict: "n/a",
        ranked: [],
        integration_checklist: [],
        next_actions: ["Confirm args.target (uncommitted | path | branch) and dirty tree"],
    });
}

log("Scope: " + scope_paths.len().to_string() + " paths, " + scope_seams.len().to_string() + " seams");

// ── Review dimensions ─────────────────────────────────────────────
phase("Review");

let dimensions = [
    #{
        key: "end-to-end-ownership",
        title: "End-to-end ownership",
        prompt_extra: "Trace the full path the change claims to serve (e.g. domain evolve → analyze → metadata → codegen/MCP/runtime). Is each step owned by the right module per docs/CORE.md and AGENTS placement? Orphan steps, silent dual paths, or 'works only if no one looks' glue?",
    },
    #{
        key: "cohesion-dual-homes",
        title: "Cohesion and dual homes",
        prompt_extra: "Look for split ownership of one concept (Pass vs Analyzer, Analysis vs Lowering for domain facts, docs vs code, two metadata bags for one fact). Prefer findings that increase long-term cohesion, not renames for fashion.",
    },
    #{
        key: "feature-integration",
        title: "Feature integration completeness",
        prompt_extra: "Is the feature integrated across parse / analyze / runtime (or emit) as needed? Missing consumers, registration without algorithm home, algorithm without pipeline registration, tests that unit-pass but skip the product entrypoint (DomainModelAnalyzer, MCP, DslCompiler)?",
    },
    #{
        key: "contracts-fail-closed",
        title: "Contracts and fail-closed",
        prompt_extra: "Silent fallbacks, vacuous success, simplified algorithms that drop prior diagnostics without tests, missing errors on empty/invalid config. Severity: structure / contract / edge / hygiene (use 🔴🟠🟡⚪ language in severity field as Structure|Contract|Edge|Hygiene).",
    },
    #{
        key: "analysis-vs-emit",
        title: "Analysis vs lowering/codegen",
        prompt_extra: "Domain facts belong in Analysis; true lower/execute and host emit in Lowering/DslCompiler. Transport domain facts vs RestApi as transport *implementation* that consumes Transport. Storage/pack binding via context. Flag category errors either direction.",
    },
    #{
        key: "tests-and-honesty",
        title: "Tests and plan honesty",
        prompt_extra: "Do tests prove the integrated path? Overclaimed plan checkboxes? Goldens that only cover the happy subset of a simplified algorithm? Missing rebuild/wiring for new types?",
    },
];

let review_jobs = [];
for d in dimensions {
    let p = "";
    p += "DESIGN + INTEGRATION code review — dimension: " + d.title + ".\n";
    p += "Focus theme: " + focus_text + "\n";
    p += "Change intent: " + scope_intent + "\n";
    p += "Scope summary: " + scope_summary + "\n";
    p += "Paths (JSON): " + json_encode(scope_paths) + "\n";
    p += "Seams (JSON): " + json_encode(scope_seams) + "\n\n";
    p += "Dimension focus: " + d.prompt_extra + "\n\n";
    p += "POLY RULES (must honor):\n";
    p += "- Domain model is the key artifact; prefer CORE seams over local tricks.\n";
    p += "- Engineer end-to-end behavior with clear ownership.\n";
    p += "- Working code before new abstractions; no pattern catalogs.\n";
    p += "- Fail-closed: empty/missing/invalid must not vacuous-succeed.\n";
    p += "- Mid-migration dual homes may be intentional residue — flag as residual only if the change makes the dual permanent or claims Done too early.\n\n";
    p += "REQUIRED: use git diff / read_file / grep on the scoped paths. Do not invent findings from memory.\n";
    p += "For each finding: id, severity (Structure|Contract|Edge|Hygiene), title, dimension, issue, evidence_paths, ";
    p += "integration_gap (what end-to-end path breaks or drifts), suggested_fix (smallest coherent fix).\n";
    p += "Max 8 findings. Prefer integration/cohesion issues over pure style. Empty list only after reading the code.\n";

    review_jobs.push(#{
        prompt: p,
        label: "review:" + d.key,
        capability_mode: "read-only",
        output_schema: findings_schema,
    });
}

let review_results = parallel(review_jobs);

let findings = [];
let ri = 0;
for r in review_results {
    if r != () && r.success && r.output != () && r.output.findings != () {
        for f in r.output.findings {
            findings.push(f);
        }
    } else {
        log("Review slot failed or empty at " + ri.to_string());
    }
    ri += 1;
}

log("Collected " + findings.len().to_string() + " raw findings");

if findings.len() == 0 {
    let md0 = "";
    md0 += "# Design integration review\n\n";
    md0 += scope_summary + "\n\n";
    md0 += "**Cohesion verdict:** no design/integration findings after dimensional review (or reviewers failed).\n";
    md0 += "\nFocus: " + focus_text + "\n";
    let path0 = write_scratch_file("design-integration-review.md", md0);
    complete(#{
        summary: "No design/integration findings reported.",
        cohesion_verdict: "clean-or-unverified",
        ranked: [],
        integration_checklist: [
            "Re-run if scope was empty or agents failed",
            "Spot-check end-to-end path manually if this was a large change",
        ],
        next_actions: [],
        path: path0,
    });
}

const MAX_VERIFY = 14;
let to_verify = [];
for f in findings {
    if to_verify.len() >= MAX_VERIFY { break; }
    to_verify.push(f);
}
if findings.len() > MAX_VERIFY {
    log("Truncated verification to " + MAX_VERIFY.to_string() + " of " + findings.len().to_string());
}

// ── Verify ────────────────────────────────────────────────────────
phase("Verify");
log("Verifying " + to_verify.len().to_string() + " findings");

let vjobs = [];
for f in to_verify {
    let title = if f.title != () { f.title } else { "untitled" };
    let issue = if f.issue != () { f.issue } else { "" };
    let gap = if f.integration_gap != () { f.integration_gap } else { "" };
    let paths = if f.evidence_paths != () { json_encode(f.evidence_paths) } else { "[]" };
    let sev = if f.severity != () { f.severity } else { "Edge" };

    let vp = "";
    vp += "Adversarially verify this DESIGN/INTEGRATION review finding.\n";
    vp += "Title: " + title + "\n";
    vp += "Claimed severity: " + sev + "\n";
    vp += "Issue: " + issue + "\n";
    vp += "Integration gap: " + gap + "\n";
    vp += "Cited paths: " + paths + "\n\n";
    vp += "You MUST open the cited paths (and git diff if needed) and try to REFUTE the claim.\n";
    vp += "Set real=true ONLY if: (1) the issue exists in code, (2) it harms cohesion or end-to-end integration ";
    vp += "(not mere style), (3) evidence is specific. Default real=false when thin or aesthetic.\n";
    vp += "evidence: quote what you read. severity_adjust: keep or lower (Structure|Contract|Edge|Hygiene).\n";

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
        let sev = if claim.severity != () { claim.severity } else { "Edge" };
        if v.output.severity_adjust != () && v.output.severity_adjust != "" {
            sev = v.output.severity_adjust;
        }
        confirmed.push(#{
            claim: claim,
            severity: sev,
            reason: v.output.reason,
            evidence: v.output.evidence,
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

log(confirmed.len().to_string() + "/" + to_verify.len().to_string() + " findings confirmed");

// ── Synthesize ────────────────────────────────────────────────────
phase("Synthesize");

let syn = "";
syn += "Synthesize a DESIGN + INTEGRATION code review report for Poly.\n";
syn += "Focus: " + focus_text + "\n";
syn += "Scope summary: " + scope_summary + "\n";
syn += "Intent: " + scope_intent + "\n\n";
syn += "CONFIRMED (JSON): " + json_encode(confirmed) + "\n\n";
syn += "REJECTED (JSON): " + json_encode(rejected) + "\n\n";
syn += "Produce:\n";
syn += "- summary: short paragraph on design/integration health of the change\n";
syn += "- cohesion_verdict: one of cohesive | partial | fragmented | blocked (with brief why in summary)\n";
syn += "- ranked: severity Structure|Contract|Edge|Hygiene, title, issue, suggested_fix — confirmed only\n";
syn += "- integration_checklist: 3–8 yes/no style checks for the end-to-end path (e.g. 'domain analyze exposes new metadata')\n";
syn += "- next_actions: ordered smallest coherent fixes\n";
syn += "Prefer CORE-aligned integration fixes over new frameworks.\n";

let syn_r = agent(syn, #{
    label: "synthesize-report",
    capability_mode: "read-only",
    output_schema: report_schema,
});

if syn_r == () || !syn_r.success || syn_r.output == () {
    let path_f = write_scratch_file("design-integration-review.md",
        "# Design integration review (partial)\n\nSynthesizer failed.\n\nConfirmed:\n```\n"
        + json_encode(confirmed) + "\n```\n");
    complete(#{
        summary: "Synthesizer failed; raw confirmed findings in scratch.",
        cohesion_verdict: "unknown",
        ranked: [],
        integration_checklist: [],
        next_actions: ["Re-run synthesize or triage confirmed JSON"],
        path: path_f,
        confirmed_count: confirmed.len(),
    });
}

let report = syn_r.output;

let md = "";
md += "# Design integration review\n\n";
md += report.summary + "\n\n";
md += "**Cohesion verdict:** ";
md += if report.cohesion_verdict != () { report.cohesion_verdict } else { "unknown" };
md += "\n\n";
md += "## Scope\n\n";
md += scope_summary + "\n\n";
md += "## Ranked findings\n\n";
if report.ranked != () {
    for item in report.ranked {
        let sev = if item.severity != () { item.severity } else { "?" };
        let t = if item.title != () { item.title } else { "?" };
        let iss = if item.issue != () { item.issue } else { "" };
        let fix = if item.suggested_fix != () { item.suggested_fix } else { "" };
        md += "### [" + sev + "] " + t + "\n";
        md += "- " + iss + "\n";
        md += "- Fix: " + fix + "\n\n";
    }
}
md += "## Integration checklist\n\n";
if report.integration_checklist != () {
    for c in report.integration_checklist {
        md += "- " + c + "\n";
    }
}
md += "\n## Next actions\n\n";
if report.next_actions != () {
    for a in report.next_actions {
        md += "1. " + a + "\n";
    }
}
md += "\n---\nVerified " + confirmed.len().to_string() + "/" + to_verify.len().to_string();
md += " · rejected " + rejected.len().to_string() + " · target: " + target_text + "\n";

let report_path = write_scratch_file("design-integration-review.md", md);
log("Wrote " + report_path);

complete(#{
    summary: report.summary,
    cohesion_verdict: report.cohesion_verdict,
    ranked: report.ranked,
    integration_checklist: report.integration_checklist,
    next_actions: report.next_actions,
    path: report_path,
    verified: confirmed.len(),
    rejected: rejected.len(),
});
