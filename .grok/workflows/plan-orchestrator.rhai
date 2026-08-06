let meta = #{
    name: "plan-orchestrator",
    description: "Orchestrate a simple-agent-tasks suite README: one task (next) or loop until-done",
    when_to_use: "When running a suite under docs/plans/simple-agent-tasks/ (e.g. spe-README.md, das-README.md). Pass suite= path. mode until-done chains tasks until complete/blocked/budget.",
    phases: [
        #{ title: "Orient", detail: "read suite + parent plan; pick next unchecked task" },
        #{ title: "Implement", detail: "execute selected task under hard rules" },
        #{ title: "Verify", detail: "adversarial AC check" },
        #{ title: "Record", detail: "update task/suite docs; loop if until-done" },
    ],
};

let orient_schema = #{
    "type": "object",
    "required": ["suite_complete", "task_id", "task_file", "wave", "objective", "prereqs_met", "blocked_reason", "hard_rules"],
    "properties": #{
        "suite_complete": #{ "type": "boolean" },
        "task_id": #{ "type": "string" },
        "task_file": #{ "type": "string" },
        "wave": #{ "type": "string" },
        "objective": #{ "type": "string" },
        "prereqs_met": #{ "type": "boolean" },
        "blocked_reason": #{ "type": "string" },
        "hard_rules": #{
            "type": "array",
            "maxItems": 12,
            "items": #{ "type": "string" },
        },
        "parallel_ok_with": #{
            "type": "array",
            "maxItems": 4,
            "items": #{ "type": "string" },
        },
        "open_task_count": #{ "type": "integer" },
        "notes": #{ "type": "string" },
    },
};

let implement_schema = #{
    "type": "object",
    "required": ["success", "summary", "files_changed", "remaining", "tests_run"],
    "properties": #{
        "success": #{ "type": "boolean" },
        "summary": #{ "type": "string" },
        "files_changed": #{
            "type": "array",
            "maxItems": 40,
            "items": #{ "type": "string" },
        },
        "remaining": #{
            "type": "array",
            "maxItems": 12,
            "items": #{ "type": "string" },
        },
        "tests_run": #{ "type": "string" },
        "build_ok": #{ "type": "boolean" },
        "blockers": #{
            "type": "array",
            "maxItems": 8,
            "items": #{ "type": "string" },
        },
    },
};

let verify_schema = #{
    "type": "object",
    "required": ["pass", "severity", "issues", "evidence"],
    "properties": #{
        "pass": #{ "type": "boolean" },
        "severity": #{ "type": "string" },
        "issues": #{
            "type": "array",
            "maxItems": 12,
            "items": #{
                "type": "object",
                "required": ["severity", "file", "description"],
                "properties": #{
                    "severity": #{ "type": "string" },
                    "file": #{ "type": "string" },
                    "description": #{ "type": "string" },
                    "suggestion": #{ "type": "string" },
                },
            },
        },
        "evidence": #{ "type": "string" },
    },
};

let record_schema = #{
    "type": "object",
    "required": ["docs_updated", "task_status", "next_task_id", "report_path_hint"],
    "properties": #{
        "docs_updated": #{
            "type": "array",
            "maxItems": 20,
            "items": #{ "type": "string" },
        },
        "task_status": #{ "type": "string" },
        "next_task_id": #{ "type": "string" },
        "report_path_hint": #{ "type": "string" },
        "notes": #{ "type": "string" },
    },
};

// --- args ---
// suite: path to suite README (preferred) | short key "das" | "spe"
//        also accepts args.suite_readme / args.path if the host passes @file that way
// task: optional forced task id (only first iteration in until-done)
// mode: "next" | "status-only" | "until-done"
// skip_implement: bool
// max_tasks: int (until-done only; default 16)
// stop_on_verify_fail: bool (default false for full-send; set true to halt on fail)
// min_budget_remaining: int agents to leave (default 8)

let suite_key = "das";
let mode = "next";
let task_override = ();
let skip_implement = false;
let max_tasks = 16;
let stop_on_verify_fail = false;
let min_budget_remaining = 8;

if args != () {
    // Structured fields (when host supplies them)
    if args.suite != () { suite_key = args.suite; }
    if args.suite_readme != () { suite_key = args.suite_readme; }
    if args.path != () { suite_key = args.path; }
    if args.suite_path != () { suite_key = args.suite_path; }
    if args.mode != () { mode = args.mode; }
    if args.task != () { task_override = args.task; }
    if args.skip_implement == true { skip_implement = true; }
    if args.max_tasks != () { max_tasks = args.max_tasks; }
    if args.stop_on_verify_fail == true { stop_on_verify_fail = true; }
    if args.min_budget_remaining != () { min_budget_remaining = args.min_budget_remaining; }

    // Freeform slash-command host: args is often
    //   { "query": "suite=spe mode=until-done", "objective": "..." }
    // not { suite: "spe", mode: "until-done" }.
    let freeform = ();
    if args.query != () { freeform = args.query; }
    else if args.objective != () { freeform = args.objective; }
    else if args.text != () { freeform = args.text; }
    else if args.prompt != () { freeform = args.prompt; }

    if freeform != () && type_of(freeform) == "string" {
        let ff = freeform;
        if ff.contains("suite=spe") || ff.contains("suite:spe") || ff.contains("spe-README") || ff.contains("spe-readme") {
            suite_key = "spe";
        } else if ff.contains("suite=das") || ff.contains("suite:das") || ff.contains("das-README") {
            suite_key = "das";
        }
        // Path form: suite=docs/plans/.../spe-README.md already covered by spe-README contains
        if ff.contains("until-done") {
            mode = "until-done";
        } else if ff.contains("status-only") {
            mode = "status-only";
        } else if ff.contains("mode=next") {
            mode = "next";
        }
        // task=E1 or task=spe-e1
        if ff.contains("task=E1") || ff.contains("task=e1") || ff.contains("task=spe-e1") { task_override = "E1"; }
        else if ff.contains("task=L1") || ff.contains("task=l1") || ff.contains("task=spe-l1") { task_override = "L1"; }
        else if ff.contains("task=O1") || ff.contains("task=o1") || ff.contains("task=spe-o1") { task_override = "O1"; }
    }
}

if max_tasks < 1 { max_tasks = 1; }
if max_tasks > 32 { max_tasks = 32; }

// Completed suites live under archive/domainmodeling-completed-2026-08/ (2026-08-05).
// Default is historical DAS paths for path resolution only — do not treat as CURRENT.
// Live pick: docs/plans/v2-to-v3/master-roadmap.md
let suite_readme = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-README.md";
let parent_plan = "docs/plans/archive/domainmodeling-completed-2026-08/domain-analysis-future-state.md";
let inventory = "docs/plans/archive/domainmodeling-completed-2026-08/domain-analysis-simplification.md";
let suite_gate = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-gate.md";
let suite_label = "DAS";

if type_of(suite_key) == "string" {
    let sk = suite_key;
    sk.trim();
    // Normalize bare keys and full paths
    if sk == "spe" || sk.contains("spe-README") || sk.contains("spe-readme") {
        suite_readme = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/spe-README.md";
        parent_plan = "docs/plans/archive/domainmodeling-completed-2026-08/domain-surface-extensions-plan.md";
        inventory = "docs/plans/archive/domainmodeling-completed-2026-08/domain-surface-extensions-plan.md";
        suite_gate = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/spe-gate.md";
        suite_label = "SPE";
    } else if sk == "das" || sk.contains("das-README") || sk.contains("das-readme") {
        suite_readme = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-README.md";
        parent_plan = "docs/plans/archive/domainmodeling-completed-2026-08/domain-analysis-future-state.md";
        inventory = "docs/plans/archive/domainmodeling-completed-2026-08/domain-analysis-simplification.md";
        suite_gate = "docs/plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-gate.md";
        suite_label = "DAS";
    } else if sk == "dogfood" || sk.contains("dogfood-README") {
        suite_readme = "docs/plans/v2-to-v3/simple-agent-tasks/dogfood-README.md";
        parent_plan = "docs/plans/v2-to-v3/mcp-dogfood-protocol.md";
        inventory = "docs/plans/v2-to-v3/mcp-dogfood-protocol.md";
        suite_gate = "";
        suite_label = "dogfood";
    } else if sk == "amu" || sk.contains("amu-README") {
        suite_readme = "docs/plans/simple-agent-tasks/amu-README.md";
        parent_plan = "docs/plans/domainmodeling-cohesion-and-metadata-findings.md";
        inventory = "docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md";
        suite_gate = "docs/plans/simple-agent-tasks/amu-gate.md";
        suite_label = "amu";
    } else if sk == "p4" || sk.contains("p4-README") {
        suite_readme = "docs/plans/simple-agent-tasks/p4-README.md";
        parent_plan = "docs/plans/domain-dsl-absorption-proposals.md";
        inventory = "docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md";
        suite_gate = "docs/plans/simple-agent-tasks/p4-gate.md";
        suite_label = "p4";
    } else if sk == "coh" || sk.contains("coh-README") {
        suite_readme = "docs/plans/simple-agent-tasks/coh-README.md";
        parent_plan = "docs/plans/domainmodeling-decomposition-proposal.md";
        inventory = "docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md";
        suite_gate = "docs/plans/simple-agent-tasks/coh-gate.md";
        suite_label = "coh";
    } else if sk != "" {
        // Arbitrary suite README path: infer companion gate/plan from parent plan link later
        suite_readme = sk;
        suite_gate = "";
        parent_plan = "";
        inventory = "";
        suite_label = "custom";
    }
}

let loop_until_done = mode == "until-done";
let max_iterations = 1;
if loop_until_done {
    max_iterations = max_tasks;
}

let completed_ids = [];
let failed_ids = [];
let last_orient = ();
let last_impl = ();
let last_verify = ();
let last_record = ();
let stop_reason = "";
let suite_done = false;
let iteration = 0;
let force_task_used = false;

let report = "";
report += "# Plan orchestrator result\n\n";
report += "- **Suite label**: `" + suite_label + "`\n";
report += "- **Suite README**: `" + suite_readme + "`\n";
report += "- **Mode**: `" + mode + "`\n";
report += "- **suite_key resolved**: `" + suite_key + "`\n";
log("Suite resolved: label=" + suite_label + " readme=" + suite_readme + " mode=" + mode);
if loop_until_done {
    report += "- **max_tasks**: " + max_tasks.to_string() + "\n";
    report += "- **stop_on_verify_fail**: " + stop_on_verify_fail.to_string() + "\n";
}
report += "\n";

while iteration < max_iterations {
    iteration += 1;
    log("=== Iteration " + iteration.to_string() + "/" + max_iterations.to_string() + " ===");

    // Budget headroom: each iteration uses ~4 agents
    let b = budget();
    if b.remaining < min_budget_remaining {
        stop_reason = "agent budget remaining " + b.remaining.to_string() + " < min_budget_remaining " + min_budget_remaining.to_string();
        log(stop_reason);
        break;
    }
    if b.remaining < 4 {
        stop_reason = "agent budget remaining too low for another orient+implement+verify+record cycle";
        log(stop_reason);
        break;
    }

    // ========== Orient ==========
    phase("Orient");

    let orient_prompt = "";
    orient_prompt += "You are the orient step of plan-orchestrator for Poly suite **" + suite_label + "**.\n";
    orient_prompt += "Use read_file/grep — do not answer from memory. Re-read current disk state every time.\n";
    orient_prompt += "CRITICAL: Only pick tasks from THIS suite README. Do not report DAS complete if suite is SPE (or vice versa).\n\n";
    orient_prompt += "Read:\n";
    orient_prompt += "1) Suite README (authoritative pick order): " + suite_readme + "\n";
    if parent_plan != "" {
        orient_prompt += "2) Parent plan / design locks: " + parent_plan + "\n";
    }
    if inventory != "" && inventory != parent_plan {
        orient_prompt += "3) Inventory (skim if long): " + inventory + "\n";
    }
    if suite_gate != "" {
        orient_prompt += "4) Suite gate (optional bookkeeping): " + suite_gate + "\n";
    }
    orient_prompt += "\nMode: " + mode + " | iteration: " + iteration.to_string() + "\n";

    if task_override != () && force_task_used == false {
        orient_prompt += "User forced first task: " + task_override + "\n";
        orient_prompt += "Resolve that task file if it exists; else fall back to first unchecked.\n";
    } else {
        orient_prompt += "Pick the FIRST unchecked product task in the suite README pick order.\n";
        orient_prompt += "Open markers: Status `[ ]` or `[~]` on task rows/files. Optional gates (*.G, spe-gate until streams done) skip unless no product tasks remain.\n";
        orient_prompt += "For SPE: open heads are E1, L1, O1 in parallel — pick ONE head (prefer first open in table: 0 optional, then E1, L1, O1).\n";
        orient_prompt += "Do NOT block on soft prereqs in full-send — if a product task is open, pick it.\n";
        orient_prompt += "Do NOT open das-* tasks when suite is SPE. Do NOT open spe-* when suite is DAS.\n";
    }

    orient_prompt += "\nIf ALL product tasks in THIS suite are [x], set suite_complete=true, task_id=\"\", task_file=\"\", wave=\"\", objective=\"\", prereqs_met=true, blocked_reason=\"\".\n";
    orient_prompt += "Always set prereqs_met=true for an open product task in full-send (prereqs are soft guidance only).\n";
    orient_prompt += "open_task_count = number of still-open product tasks in THIS suite pick order.\n";
    orient_prompt += "List hard rules from the suite README (not from a different suite).\n";
    orient_prompt += "task_file must be a path under docs/plans/simple-agent-tasks/ matching this suite (e.g. spe-e1-*.md).\n";

    let orient_label = "orient:" + iteration.to_string();
    let orient = agent(orient_prompt, #{
        label: orient_label,
        capability_mode: "read-only",
        output_schema: orient_schema,
    });

    if orient == () || !orient.success || orient.output == () {
        stop_reason = "orient agent failed at iteration " + iteration.to_string();
        failed_ids.push("orient@" + iteration.to_string());
        break;
    }

    let o = orient.output;
    last_orient = o;

    if task_override != () && force_task_used == false {
        force_task_used = true;
    }

    if o.suite_complete == true {
        suite_done = true;
        stop_reason = "suite complete — no open product tasks";
        log(stop_reason);
        break;
    }

    if o.task_id == () || o.task_id == "" {
        stop_reason = "orient returned empty task_id without suite_complete";
        break;
    }

    log("Picked " + o.task_id + " (" + o.task_file + ") wave=" + o.wave);

    if mode == "status-only" {
        let status_path = write_scratch_file("plan-orchestrator-status.md", json_encode(o));
        complete(#{
            summary: "Status only: next task is " + o.task_id,
            success: true,
            orient: o,
            path: status_path,
        });
    }

    if o.prereqs_met != true {
        stop_reason = "blocked: " + o.blocked_reason;
        log(stop_reason);
        failed_ids.push(o.task_id);
        break;
    }

    if skip_implement == true {
        complete(#{
            summary: "Orient only (skip_implement). Next: " + o.task_id,
            success: true,
            orient: o,
        });
    }

    // ========== Implement ==========
    phase("Implement");

    let impl_prompt = "";
    impl_prompt += "You are the implementer for Poly **" + suite_label + "** suite task " + o.task_id + ".\n";
    impl_prompt += "Read and execute ONLY this task file: " + o.task_file + "\n";
    impl_prompt += "Re-read hard rules from suite README: " + suite_readme + "\n";
    if parent_plan != "" {
        impl_prompt += "Parent plan / design locks: " + parent_plan + "\n";
    }
    impl_prompt += "\nObjective: " + o.objective + "\n\n";
    impl_prompt += "Rules:\n";
    impl_prompt += "- Minimal diffs; match Agents.md / docs/CORE.md.\n";
    impl_prompt += "- Respect file ownership in the task (do not thrash other SPE streams).\n";
    impl_prompt += "- Analysis = facts + diagnostics only; no Syntax IR mid-pipeline.\n";
    impl_prompt += "- Prefer delete dual path over documenting it when the task allows.\n";
    impl_prompt += "- Fail closed when analysis is present and required metadata is missing.\n";
    impl_prompt += "- Stay inside this task's acceptance criteria only.\n";
    impl_prompt += "- Run the smallest relevant build/tests; put commands in tests_run.\n";
    impl_prompt += "- Use tools; do not claim success without evidence.\n";
    impl_prompt += "- If blocked, success=false and list blockers.\n";

    let impl = agent(impl_prompt, #{
        label: "implement:" + o.task_id,
        capability_mode: "all",
        output_schema: implement_schema,
    });

    if impl == () || !impl.success || impl.output == () {
        stop_reason = "implement agent failed for " + o.task_id;
        failed_ids.push(o.task_id);
        break;
    }

    let im = impl.output;
    last_impl = im;
    log("Implement success=" + im.success.to_string() + " files=" + im.files_changed.len().to_string());

    if im.success != true {
        stop_reason = "implement reported success=false for " + o.task_id;
        failed_ids.push(o.task_id);
        // still verify+record for honesty
    }

    // ========== Verify ==========
    phase("Verify");

    let verify_prompt = "";
    verify_prompt += "Adversarial verifier for DAS task " + o.task_id + " (" + o.task_file + ").\n";
    verify_prompt += "Read-only tools. Assume the code may be wrong.\n\n";
    verify_prompt += "Implementer success=" + im.success.to_string() + " summary: " + im.summary + "\n";
    verify_prompt += "Files (re-check with git status/diff yourself):\n";
    for f in im.files_changed {
        verify_prompt += "- " + f + "\n";
    }
    verify_prompt += "\nCheck task acceptance criteria. Fail closed on missing evidence.\n";
    verify_prompt += "Sibling-path: dual paths both correct or incomplete.\n";
    verify_prompt += "severity: none|nit|suggestion|bug|blocker\n";

    let ver = agent(verify_prompt, #{
        label: "verify:" + o.task_id,
        capability_mode: "read-only",
        output_schema: verify_schema,
    });

    let verify_pass = false;
    let ver_out = ();
    if ver != () && ver.success && ver.output != () {
        ver_out = ver.output;
        last_verify = ver_out;
        if ver.output.pass == true && im.success == true {
            verify_pass = true;
        }
    }

    if !verify_pass {
        log("Verify did not pass for " + o.task_id);
        failed_ids.push(o.task_id);
    }

    // ========== Record ==========
    phase("Record");

    let record_prompt = "";
    record_prompt += "Update DAS suite docs for task " + o.task_id + ".\n";
    record_prompt += "Task file: " + o.task_file + "\n";
    record_prompt += "Suite README: " + suite_readme + "\n";
    record_prompt += "Suite gate: " + suite_gate + "\n\n";
    record_prompt += "Implement success: " + im.success.to_string() + "\n";
    record_prompt += "Verify pass: " + verify_pass.to_string() + "\n";
    if ver_out != () {
        record_prompt += "Verify severity: " + ver_out.severity + "\n";
        record_prompt += "Verify evidence: " + ver_out.evidence + "\n";
    }
    record_prompt += "\nActions:\n";
    record_prompt += "1) If implement+verify ok: Status [x], check AC, Progress notes.\n";
    record_prompt += "2) Else: leave open/[~], note failures; do not mark complete.\n";
    record_prompt += "3) Update suite README status table for this task: " + suite_readme + "\n";
    record_prompt += "4) Do NOT mark suite gate complete unless all product tasks are truly [x].\n";
    record_prompt += "5) next_task_id = first still-open product task after your edits (or empty if none).\n";
    record_prompt += "6) Only edit THIS suite's task files (SPE vs DAS — do not cross-suite).\n";
    record_prompt += "Markdown only; no production code.\n";

    let rec = agent(record_prompt, #{
        label: "record:" + o.task_id,
        capability_mode: "read-write",
        output_schema: record_schema,
    });

    let rec_out = ();
    if rec != () && rec.success && rec.output != () {
        rec_out = rec.output;
        last_record = rec_out;
    }

    // Per-task report section
    report += "## Iteration " + iteration.to_string() + ": " + o.task_id + "\n\n";
    report += "- wave: " + o.wave + "\n";
    report += "- file: `" + o.task_file + "`\n";
    report += "- implement: " + im.success.to_string() + "\n";
    report += "- verify: " + verify_pass.to_string() + "\n";
    report += "- summary: " + im.summary + "\n";
    report += "- tests: " + im.tests_run + "\n";
    if rec_out != () {
        report += "- task_status: " + rec_out.task_status + "\n";
        report += "- next_task_id: " + rec_out.next_task_id + "\n";
    }
    report += "\n";

    if verify_pass {
        completed_ids.push(o.task_id);
    }

    // Stop conditions for loop
    if !loop_until_done {
        stop_reason = "mode=next — single task finished";
        break;
    }

    if !verify_pass && stop_on_verify_fail {
        stop_reason = "stop_on_verify_fail: " + o.task_id + " did not pass verify";
        break;
    }

    if im.success != true && stop_on_verify_fail {
        stop_reason = "stop_on_verify_fail: implement failed for " + o.task_id;
        break;
    }

    // If record says no next task, re-orient next iteration will set suite_complete
    // Continue loop to confirm via orient
}

if stop_reason == "" {
    if iteration >= max_iterations && loop_until_done && !suite_done {
        stop_reason = "hit max_tasks=" + max_tasks.to_string() + " without suite complete";
    } else if suite_done {
        stop_reason = "suite complete";
    } else {
        stop_reason = "loop ended";
    }
}

report += "## Summary\n\n";
report += "- **stop_reason**: " + stop_reason + "\n";
report += "- **iterations**: " + iteration.to_string() + "\n";
report += "- **suite_complete**: " + suite_done.to_string() + "\n";
report += "- **completed**: ";
if completed_ids.len() == 0 {
    report += "(none)\n";
} else {
    report += "\n";
    for id in completed_ids {
        report += "  - " + id + "\n";
    }
}
report += "- **failed/incomplete**: ";
if failed_ids.len() == 0 {
    report += "(none)\n";
} else {
    report += "\n";
    for id in failed_ids {
        report += "  - " + id + "\n";
    }
}
report += "\n";

if suite_done {
    report += "Suite product tasks appear complete. Fill `" + suite_gate + "` evidence if not already done.\n";
} else if loop_until_done {
    report += "Re-run with `mode: until-done` to continue, or fix blockers then resume.\n";
    report += "Optional: raise `max_tasks` or `agent_budget` if budget/max_tasks stopped the run.\n";
}

let path = write_scratch_file("plan-orchestrator-report.md", report);

let overall = suite_done || (completed_ids.len() > 0 && failed_ids.len() == 0 && !loop_until_done);
if loop_until_done {
    overall = suite_done;
}

let summary = stop_reason;
if completed_ids.len() > 0 {
    summary = completed_ids.len().to_string() + " completed; " + stop_reason;
}

complete(#{
    summary: summary,
    success: overall,
    mode: mode,
    suite_complete: suite_done,
    stop_reason: stop_reason,
    iterations: iteration,
    completed: completed_ids,
    failed: failed_ids,
    path: path,
    last_orient: last_orient,
    last_implement: last_impl,
    last_verify: last_verify,
    last_record: last_record,
});
