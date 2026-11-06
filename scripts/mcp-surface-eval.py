#!/usr/bin/env python3
"""Deterministic scenario harness for the Poly MCP v2 surface.

Runs the fifteen reference scenarios over real stdio and
grades the outcomes with code. Historical v1 comparison results are in
reports/mcp-surface-eval.json from before the v1 surface was removed.

Usage: python3 scripts/mcp-surface-eval.py [--json OUT]
"""

from __future__ import annotations

import json
import select
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BIN = ROOT / "Poly.Mcp/bin/Debug/net10.0/Poly.Mcp"


class Client:
    def __init__(self, surface: int):
        self.proc = subprocess.Popen(
            [str(BIN)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True, bufsize=1)
        self.next_id = 0
        self.calls = 0
        self.chars = 0
        self.structured = 0
        self._rpc("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "surface-eval", "version": "1"},
        })
        self._notify("notifications/initialized")

    def _send(self, message):
        self.proc.stdin.write(json.dumps(message) + "\n")
        self.proc.stdin.flush()

    def _notify(self, method):
        self._send({"jsonrpc": "2.0", "method": method})

    def _rpc(self, method, params, timeout=20):
        self.next_id += 1
        want = self.next_id
        self._send({"jsonrpc": "2.0", "id": want, "method": method, "params": params})
        end = time.time() + timeout
        while time.time() < end:
            ready, _, _ = select.select([self.proc.stdout], [], [], 0.2)
            if not ready:
                continue
            line = self.proc.stdout.readline()
            if not line.strip():
                continue
            obj = json.loads(line)
            if obj.get("id") == want:
                return obj
        raise TimeoutError(f"{method} timed out")

    def call(self, name, arguments):
        response = self._rpc("tools/call", {"name": name, "arguments": arguments})
        result = response.get("result", {})
        text = result["content"][0]["text"]
        self.calls += 1
        self.chars += len(text)
        structured = result.get("structuredContent")
        if structured is not None:
            self.structured += len(json.dumps(structured))
        return json.loads(text), len(text)

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=5)
        except Exception:
            self.proc.kill()


# ── surface adapters ─────────────────────────────────────────────

def open_ws(c: Client, surface: int, name: str) -> str:
    if surface == 2:
        return c.call("poly_open", {"name": name})[0]["state"]["workspace"]
    return c.call("create_domain_session", {"domainName": name})[0]["sessionId"]


def is_ok(response):
    return bool(response.get("ok", response.get("success", False)))


def define(c: Client, surface: int, ws: str, dsl: str, apply: bool = True):
    if surface == 2:
        return c.call("poly_define", {"workspace": ws, "poly": dsl, "apply": apply})[0]
    tool = "apply_dsl" if apply else "validate_dsl"
    return c.call(tool, {"sessionId": ws, "polyText": dsl})[0]


def check(c: Client, surface: int, ws: str):
    if surface == 2:
        return c.call("poly_check", {"workspace": ws})[0]
    return c.call("get_domain_analysis", {"sessionId": ws})[0]


def inspect(c: Client, surface: int, ws: str, select: str | None = None):
    if surface == 2:
        return c.call("poly_inspect", {"workspace": ws, "select": select, "detail": "full"})[0]
    if select is None:
        return c.call("get_domain_overview", {"sessionId": ws})[0]
    return c.call("get_entity_detail", {"sessionId": ws, "entityName": select})[0]


def export(c: Client, surface: int, ws: str, fmt: str) -> str:
    if surface == 2:
        data = c.call("poly_export", {"workspace": ws, "format": fmt, "inline": True})[0]["data"]
        return data.get("content") or Path(data["path"]).read_text()
    tool, prop = {
        "dsl": ("export_dsl", "poly"),
        "json-schema": ("export_domain_to_json_schema", "json"),
        "csharp": ("export_domain_to_csharp", "csharp"),
    }[fmt]
    return c.call(tool, {"sessionId": ws})[0]["data"][prop]


SIMPLE = """domain Simple
Product: entity {
  Name: Text
  Qty: Number range(0, 10)
  Draft: stage { Submit: action { transition to Active } }
  Active: stage {}
}
"""

INVALID = """domain Simple
Product: entity {
  Qty: Number range(0, 10)
  Raise: action { assign Qty to Qty + 250 }
}
"""


def task_define_valid(c, surface, ws):
    return is_ok(define(c, surface, ws, SIMPLE))


def task_invalid_then_fix(c, surface, ws):
    first = define(c, surface, ws, INVALID)
    second = define(c, surface, ws, SIMPLE)
    return (not is_ok(first)) and is_ok(second)


def task_check_valid(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    result = check(c, surface, ws)
    if surface == 2:
        return result["data"]["verdict"] == "proven"
    return result["data"]["errorCount"] == 0


def task_inspect_overview(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    data = inspect(c, surface, ws)["data"]
    names = data.get("entityNames") or [e["name"] for e in data.get("entities", [])]
    return "Product" in names


def task_inspect_entity(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    data = inspect(c, surface, ws, "Product")["data"]
    props = [p["name"] for p in data.get("properties", [])]
    return "Name" in props


def task_export_dsl(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    return "domain Simple" in export(c, surface, ws, "dsl")


def task_export_schema(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    return '"$schema"' in export(c, surface, ws, "json-schema")


def task_export_csharp(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    text = export(c, surface, ws, "csharp")
    return "class Product" in text or "record Product" in text


def task_simulate_lifecycle(c, surface, ws):
    define(c, surface, ws, SIMPLE)
    if surface == 2:
        data = c.call("poly_simulate", {"workspace": ws, "steps": json.dumps([
            {"op": "create", "entity": "Product", "properties": {"Name": "A", "Qty": 1}, "as": "p"},
            {"op": "invoke", "instanceId": "p", "action": "Submit"},
            {"op": "get", "instanceId": "p"},
        ])})[0]["data"]
        return data.get("failedAt") is None and "Active" in json.dumps(data["trace"][2]["data"])
    created = c.call("create_instance", {
        "sessionId": ws, "entityName": "Product",
        "propertiesJson": '{"Name":"A","Qty":1}'})[0]
    instance_id = created["data"]["instance"]["instanceId"]
    invoked = c.call("invoke_action", {
        "sessionId": ws, "instanceId": instance_id, "actionName": "Submit"})[0]
    return invoked["success"] and invoked["data"]["invokeActionResult"]["newStage"] == "Active"


def task_requiredness(c, surface, ws):
    define(c, surface, ws, """domain Req
Item: entity {
  RequiredFirst: Text
  Note: Text optional
  Kind: Text default("x")
}
""")
    data = json.loads(export(c, surface, ws, "json-schema"))
    required = data["$defs"]["Item"].get("required", [])
    return "RequiredFirst" in required and "Note" not in required and "Kind" not in required


def task_enum_terms(c, surface, ws):
    define(c, surface, ws, """domain Terms
Color: enum { Red term("red"), Green }
Item: entity { Shade: Color }
""")
    data = json.loads(export(c, surface, ws, "json-schema"))
    return data["$defs"]["Color"]["x-poly-enum"]["Red"]["term"] == "red"


def task_relationship(c, surface, ws):
    define(c, surface, ws, """domain Rel
Line: entity { Sku: Text }
Order: entity { Total: Number lines: many Line }
""")
    return "many Line" in export(c, surface, ws, "dsl")


def task_dry_run(c, surface, ws):
    define(c, surface, ws, SIMPLE, apply=False)
    data = inspect(c, surface, ws)["data"]
    return data["entityCount"] == 0


def task_two_workspaces(c, surface, ws):
    second = open_ws(c, surface, "Second")
    define(c, surface, ws, "domain First\nA: entity { X: Text }\n")
    define(c, surface, second, "domain Second\nB: entity { Y: Text }\n")
    first_data = inspect(c, surface, ws)["data"]
    second_data = inspect(c, surface, second)["data"]
    first_names = first_data.get("entityNames") or [e["name"] for e in first_data.get("entities", [])]
    second_names = second_data.get("entityNames") or [e["name"] for e in second_data.get("entities", [])]
    return first_names == ["A"] and second_names == ["B"]


def large_dsl(n: int = 10) -> str:
    lines = ["domain Large"]
    for i in range(n):
        lines.append(f"E{i}: entity {{ A{i}: Text B{i}: Number range(0, 100) C{i}: Boolean }}")
    return "\n".join(lines) + "\n"


def task_large_model(c, surface, ws):
    define(c, surface, ws, large_dsl())
    result = check(c, surface, ws)
    if surface == 2:
        return result["data"]["verdict"] == "proven"
    return result["data"]["errorCount"] == 0


TASKS = [
    ("define_valid", task_define_valid),
    ("invalid_then_fix", task_invalid_then_fix),
    ("check_valid", task_check_valid),
    ("inspect_overview", task_inspect_overview),
    ("inspect_entity", task_inspect_entity),
    ("export_dsl", task_export_dsl),
    ("export_json_schema", task_export_schema),
    ("export_csharp", task_export_csharp),
    ("simulate_lifecycle", task_simulate_lifecycle),
    ("requiredness", task_requiredness),
    ("enum_terms", task_enum_terms),
    ("relationship", task_relationship),
    ("dry_run", task_dry_run),
    ("two_workspaces", task_two_workspaces),
    ("large_model", task_large_model),
]


def run(surface: int):
    results = []
    for index, (name, task) in enumerate(TASKS):
        client = Client(surface)
        try:
            workspace = open_ws(client, surface, f"Eval{index}")
            try:
                ok = bool(task(client, surface, workspace))
            except Exception as exc:  # a failed scenario is a failed grade, not a crash
                ok = False
                results.append({"task": name, "ok": ok, "error": str(exc)})
            else:
                results.append({"task": name, "ok": ok})
            results[-1]["calls"] = client.calls
            results[-1]["chars"] = client.chars
            results[-1]["structured"] = client.structured
        finally:
            client.close()
    return results


def main() -> int:
    if not BIN.exists():
        print(f"missing {BIN}; build Poly.Mcp first", file=sys.stderr)
        return 1

    summary = {}
    print("v1 surface removed; measuring v2 only. Historical A/B: reports/mcp-surface-eval.json")
    for surface in (2,):
        rows = run(surface)
        summary[f"v{surface}"] = {
            "tasks": rows,
            "passed": sum(1 for row in rows if row["ok"]),
            "total": len(rows),
            "calls": sum(row["calls"] for row in rows),
            "chars": sum(row["chars"] for row in rows),
            "structured": sum(row["structured"] for row in rows),
        }
        print(f"\nv{surface}: {summary[f'v{surface}']['passed']}/{len(rows)} passed, "
              f"{summary[f'v{surface}']['calls']} calls, {summary[f'v{surface}']['chars']} text chars, "
              f"{summary[f'v{surface}']['structured']} structured chars")
        for row in rows:
            print(f"  {'PASS' if row['ok'] else 'FAIL'}  {row['task']:<20} "
                  f"calls={row['calls']:<3} chars={row['chars']:<6} structured={row['structured']}"
                  + (f"  {row.get('error', '')}" if row.get("error") else ""))

    out = Path(sys.argv[sys.argv.index("--json") + 1]) if "--json" in sys.argv else None
    if out:
        out.write_text(json.dumps(summary, indent=2))
        print(f"\nwrote {out}")
    return 0 if summary["v2"]["passed"] == summary["v2"]["total"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
