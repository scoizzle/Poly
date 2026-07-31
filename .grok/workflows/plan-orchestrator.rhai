let meta = #{
    name: "plan-orchestrator",
    description: "Orchestrate the DAS suite: one task (next) or loop until plan complete (until-done)",
    when_to_use: "When running docs/plans/simple-agent-tasks/das-* (W0–W4). Use mode until-done to chain tasks until the suite is complete, blocked, or max_tasks/budget exhausted",
    phases: [
        #{ title: "Orient", detail: "read suite + future-state; pick next unchecked task" },
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
// suite: "das" (default) | path to suite README
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
    if args.suite != () { suite_key = args.suite; }
    if args.mode != () { mode = args.mode; }
    if args.task != () { task_override = args.task; }
    if args.skip_implement == true { skip_implement = true; }
    if args.max_tasks != () { max_tasks = args.max_tasks; }
    if args.stop_on_verify_fail == true { stop_on_verify_fail = true; }
    if args.min_budget_remaining != () { min_budget_remaining = args.min_budget_remaining; }
}

if max_tasks < 1 { max_tasks = 1; }
if max_tasks > 32 { max_tasks = 32; }

let suite_readme = "docs/plans/simple-agent-tasks/das-README.md";
let future_state = "docs/plans/domain-analysis-future-state.md";
let inventory = "docs/plans/domain-analysis-simplification.md";
let suite_gate = "docs/plans/simple-agent-tasks/das-gate.md";

if suite_key != "das" {
    if type_of(suite_key) == "string" {
        let sk = suite_key;
        sk.trim();
        if sk != "" && sk != "das" {
            suite_readme = sk;
        }
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
report += "- **Suite**: `" + suite_readme + "`\n";
report += "- **Mode**: `" + mode + "`\n";
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
    orient_prompt += "You are the orient step of plan-orchestrator for Poly's domain-analysis suite.\n";
    orient_prompt += "Use read_file/grep — do not answer from memory. Re-read current disk state every time.\n\n";
    orient_prompt += "Read:\n";
    orient_prompt += "1) " + suite_readme + "\n";
    orient_prompt += "2) " + future_state + "\n";
    orient_prompt += "3) " + inventory + " (skim if long)\n";
    orient_prompt += "4) " + suite_gate + "\n\n";
    orient_prompt += "Mode: " + mode + " | iteration: " + iteration.to_string() + "\n";

    if task_override != () && force_task_used == false {
        orient_prompt += "User forced first task: " + task_override + "\n";
        orient_prompt += "Resolve that task file if it exists; else fall back to first unchecked.\n";
    } else {
        orient_prompt += "Pick the FIRST unchecked task in suite pick order (W0 before W1, etc.).\n";
        orient_prompt += "Gate rows (W*.G) are optional bookkeeping — skip them; pick the next product task with Status [ ] or [~].\n";
    orient_prompt += "Do NOT block on wave gates or prereq gates — full-send mode: if a product task is open, pick it even if earlier gates are incomplete.\n";
    }

    orient_prompt += "\nIf ALL product tasks are [x], set suite_complete=true, task_id=\"\", task_file=\"\", wave=\"\", objective=\"\", prereqs_met=true, blocked_reason=\"\".\n";
    orient_prompt += "Always set prereqs_met=true for an open product task in full-send (prereqs are soft guidance only).\n";
    orient_prompt += "open_task_count = number of still-open product tasks in pick order.\n";
    orient_prompt += "List hard rules from the suite README.\n";

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
    impl_prompt += "You are the implementer for Poly DAS suite task " + o.task_id + ".\n";
    impl_prompt += "Read and execute ONLY this task file: " + o.task_file + "\n";
    impl_prompt += "Re-read hard rules from suite README and future-state as needed.\n\n";
    impl_prompt += "Objective: " + o.objective + "\n\n";
    impl_prompt += "Rules:\n";
    impl_prompt += "- Minimal diffs; match Agents.md / docs/CORE.md.\n";
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
    record_prompt += "3) Update das-README status table for this task.\n";
    record_prompt += "4) Do NOT mark wave gate complete unless all wave tasks are truly [x].\n";
    record_prompt += "5) next_task_id = first still-open product task after your edits (or empty if none).\n";
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
