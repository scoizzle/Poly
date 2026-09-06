#!/usr/bin/env python3
"""PR51 dogfood: MCP simulate create Type vs create-in via Interpreter + bound Store."""
from __future__ import annotations

import json
import subprocess
import threading
import time
from pathlib import Path

MCP = "/home/box/Poly/Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll"
PROBE_DIR = Path("/home/box/Poly/docs/probes/dogfood")
OUT = Path("/tmp/poly-dogfood-pr51-reprobe-89935a56.jsonl")

class McpClient:
    def __init__(self):
        self.proc = subprocess.Popen(
            ["dotnet", MCP],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            bufsize=0,
        )
        self._id = 0
        self._stderr_lines: list[str] = []
        t = threading.Thread(target=self._drain_stderr, daemon=True)
        t.start()
        time.sleep(0.5)

    def _drain_stderr(self):
        assert self.proc.stderr
        for line in iter(self.proc.stderr.readline, b""):
            self._stderr_lines.append(line.decode("utf-8", "replace").rstrip())

    def _read_msg(self, timeout=90.0):
        assert self.proc.stdout
        buf = b""
        deadline = time.time() + timeout
        while time.time() < deadline:
            chunk = self.proc.stdout.read(1)
            if not chunk:
                if self.proc.poll() is not None:
                    raise RuntimeError(
                        f"MCP exited {self.proc.returncode}; stderr tail: {self._stderr_lines[-30:]}"
                    )
                time.sleep(0.01)
                continue
            buf += chunk
            if buf.endswith(b"\n"):
                line = buf.decode("utf-8", "replace").strip()
                buf = b""
                if not line:
                    continue
                msg = json.loads(line)
                if "id" not in msg and msg.get("method"):
                    continue
                return msg
        raise TimeoutError(f"timeout; stderr: {self._stderr_lines[-30:]}")

    def send(self, method, params=None, notify=False):
        self._id += 1
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            msg["id"] = self._id
        raw = (json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8")
        assert self.proc.stdin
        self.proc.stdin.write(raw)
        self.proc.stdin.flush()
        if notify:
            return None
        return self._read_msg()

    def initialize(self):
        r = self.send(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "poly-dogfood-pr51-89935a56", "version": "1.0"},
            },
        )
        self.send("notifications/initialized", notify=True)
        return r

    def tools_list(self):
        return self.send("tools/list", {})

    def call(self, name, arguments):
        return self.send("tools/call", {"name": name, "arguments": arguments})

    def close(self):
        try:
            self.proc.terminate()
        except Exception:
            pass


def unwrap(resp):
    out = {"protocol": None, "tool": None, "is_error": False, "raw": resp}
    if resp is None:
        return out
    if "error" in resp:
        out["protocol"] = "jsonrpc-error"
        out["is_error"] = True
        out["tool"] = resp["error"]
        return out
    result = resp.get("result") or {}
    out["is_error"] = bool(result.get("isError"))
    content = result.get("content") or []
    texts = []
    for c in content:
        if isinstance(c, dict) and c.get("type") == "text":
            texts.append(c.get("text") or "")
    blob = "\n".join(texts)
    parsed = None
    if blob:
        try:
            parsed = json.loads(blob)
        except json.JSONDecodeError:
            parsed = {"unparsed_text": blob}
    out["protocol"] = "tools/call"
    out["tool"] = parsed if parsed is not None else result
    out["text"] = blob
    return out


def slim(unw):
    t = unw.get("tool")
    if isinstance(t, dict):
        return {
            "success": t.get("success"),
            "message": t.get("message"),
            "isError": unw.get("is_error"),
            "protocol": unw.get("protocol"),
            "data": t.get("data"),
            "diagnostics": t.get("diagnostics"),
            "sessionId": t.get("sessionId"),
        }
    return {"protocol": unw.get("protocol"), "tool": t, "isError": unw.get("is_error")}


def sid(unw):
    t = unw.get("tool") or {}
    if isinstance(t, dict):
        return t.get("sessionId")
    return None


def inst_id(unw):
    t = unw.get("tool") or {}
    data = (t or {}).get("data") or {}
    inst = data.get("instance") or {}
    return inst.get("instanceId") or data.get("instanceId") or data.get("returnInstanceId")


def return_inst(unw):
    t = unw.get("tool") or {}
    data = (t or {}).get("data") or {}
    return data.get("returnInstanceId") or data.get("returnInstance") or (
        (data.get("instance") or {}).get("instanceId")
    )


def policy_result(unw):
    t = unw.get("tool") or {}
    data = (t or {}).get("data") or {}
    if isinstance(data, dict):
        if "result" in data:
            return data.get("result")
        if "passed" in data:
            return data.get("passed")
    return None


def main():
    client = McpClient()
    log = []

    def rec(label, unw, extra=None):
        row = {"label": label, **slim(unw)}
        if extra:
            row.update(extra)
        log.append(row)
        ok = row.get("success")
        msg = (row.get("message") or "")[:180]
        print(f"\n=== {label} success={ok} ===")
        print(msg)
        if extra:
            print("extra:", json.dumps(extra, default=str)[:500])
        return row

    try:
        init = client.initialize()
        print("initialized", str(init)[:300])

        tools = client.tools_list()
        names = []
        try:
            names = sorted(t["name"] for t in (tools.get("result") or {}).get("tools") or [])
        except Exception as e:
            print("tools/list parse err", e, str(tools)[:500])
        print("tools:", names)
        log.append({"label": "tools.list", "success": True, "tools": names})
        simulate_like = [n for n in names if "simulate" in n.lower() or n in (
            "invoke_action", "evaluate_policy", "create_instance", "list_instances", "get_instance"
        )]
        print("simulate-path tools:", simulate_like)

        # ── A: Type create ─────────────────────────────────────────
        poly = (PROBE_DIR / "simulate-create-type.poly").read_text()
        r = unwrap(client.call("create_domain_session", {"domainName": "SimulateCreateType"}))
        rec("A.create_session", r)
        sa = sid(r)
        r = unwrap(client.call("apply_dsl", {"sessionId": sa, "polyText": poly}))
        rec("A.apply_dsl", r)

        r = unwrap(client.call("create_instance", {
            "sessionId": sa, "entityName": "Patron",
            "propertiesJson": json.dumps({"Name": "Ada"}),
        }))
        rec("A.create_patron", r)
        patron_a = inst_id(r)

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sa, "entityName": "Patron", "policyName": pol, "instanceId": patron_a,
            }))
            rec(f"A.pre.{pol}", r, extra={"policyResult": policy_result(r)})

        r = unwrap(client.call("invoke_action", {
            "sessionId": sa, "instanceId": patron_a, "actionName": "AssessByType",
        }))
        rec("A.invoke_AssessByType", r, extra={"returnInstanceId": return_inst(r)})
        fine_a = return_inst(r)

        r = unwrap(client.call("list_instances", {"sessionId": sa, "entityName": "Fine"}))
        rec("A.list_Fine", r)
        r = unwrap(client.call("list_instances", {"sessionId": sa}))
        rec("A.list_all", r)

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sa, "entityName": "Patron", "policyName": pol, "instanceId": patron_a,
            }))
            rec(f"A.post.{pol}", r, extra={"policyResult": policy_result(r)})

        if fine_a:
            r = unwrap(client.call("get_instance", {"sessionId": sa, "instanceId": fine_a}))
            rec("A.get_fine", r)
        r = unwrap(client.call("get_instance", {"sessionId": sa, "instanceId": patron_a}))
        rec("A.get_patron", r)

        # ── B: create-in ───────────────────────────────────────────
        poly = (PROBE_DIR / "simulate-create-in.poly").read_text()
        r = unwrap(client.call("create_domain_session", {"domainName": "SimulateCreateIn"}))
        rec("B.create_session", r)
        sb = sid(r)
        r = unwrap(client.call("apply_dsl", {"sessionId": sb, "polyText": poly}))
        rec("B.apply_dsl", r)

        r = unwrap(client.call("create_instance", {
            "sessionId": sb, "entityName": "Patron",
            "propertiesJson": json.dumps({"Name": "Bea"}),
        }))
        rec("B.create_patron", r)
        patron_b = inst_id(r)

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sb, "entityName": "Patron", "policyName": pol, "instanceId": patron_b,
            }))
            rec(f"B.pre.{pol}", r, extra={"policyResult": policy_result(r)})

        r = unwrap(client.call("invoke_action", {
            "sessionId": sb, "instanceId": patron_b, "actionName": "AssessByRel",
        }))
        rec("B.invoke_AssessByRel", r, extra={"returnInstanceId": return_inst(r)})
        fine_b = return_inst(r)

        r = unwrap(client.call("list_instances", {"sessionId": sb, "entityName": "Fine"}))
        rec("B.list_Fine", r)
        r = unwrap(client.call("list_instances", {"sessionId": sb}))
        rec("B.list_all", r)

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sb, "entityName": "Patron", "policyName": pol, "instanceId": patron_b,
            }))
            rec(f"B.post.{pol}", r, extra={"policyResult": policy_result(r)})

        if fine_b:
            r = unwrap(client.call("get_instance", {"sessionId": sb, "instanceId": fine_b}))
            rec("B.get_fine", r)
        r = unwrap(client.call("get_instance", {"sessionId": sb, "instanceId": patron_b}))
        rec("B.get_patron", r)

        # ── C: combined sequential Type then create-in ─────────────
        poly = (PROBE_DIR / "simulate-create-create-in.poly").read_text()
        r = unwrap(client.call("create_domain_session", {"domainName": "SimulateCreateCreateIn"}))
        rec("C.create_session", r)
        sc = sid(r)
        r = unwrap(client.call("apply_dsl", {"sessionId": sc, "polyText": poly}))
        rec("C.apply_dsl", r)

        r = unwrap(client.call("create_instance", {
            "sessionId": sc, "entityName": "Patron",
            "propertiesJson": json.dumps({"Name": "Cy"}),
        }))
        rec("C.create_patron", r)
        patron_c = inst_id(r)

        r = unwrap(client.call("invoke_action", {
            "sessionId": sc, "instanceId": patron_c, "actionName": "AssessByType",
        }))
        rec("C.invoke_AssessByType", r, extra={"returnInstanceId": return_inst(r)})

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sc, "entityName": "Patron", "policyName": pol, "instanceId": patron_c,
            }))
            rec(f"C.afterType.{pol}", r, extra={"policyResult": policy_result(r)})

        r = unwrap(client.call("list_instances", {"sessionId": sc, "entityName": "Fine"}))
        rec("C.list_Fine_afterType", r)

        r = unwrap(client.call("invoke_action", {
            "sessionId": sc, "instanceId": patron_c, "actionName": "AssessByRel",
        }))
        rec("C.invoke_AssessByRel", r, extra={"returnInstanceId": return_inst(r)})

        for pol in ("HasFines", "HasFineCount", "NoFines"):
            r = unwrap(client.call("evaluate_policy", {
                "sessionId": sc, "entityName": "Patron", "policyName": pol, "instanceId": patron_c,
            }))
            rec(f"C.afterRel.{pol}", r, extra={"policyResult": policy_result(r)})

        r = unwrap(client.call("list_instances", {"sessionId": sc, "entityName": "Fine"}))
        rec("C.list_Fine_afterRel", r)
        r = unwrap(client.call("get_instance", {"sessionId": sc, "instanceId": patron_c}))
        rec("C.get_patron", r)

    finally:
        OUT.write_text(json.dumps(log, indent=2, default=str))
        print(f"\nWrote {OUT} ({len(log)} rows)")
        client.close()


if __name__ == "__main__":
    main()
