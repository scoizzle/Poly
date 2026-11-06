# Designing MCP / Agent Tool Surfaces: A Cited Synthesis (2025–2026)

Scope: developer and domain tools (MCP servers, in-house agent tools), not consumer chat plugins.
As-of date: 2026-11-06.

Evidence legend used inline:
- **[S]** normative spec text or vendor documentation (authoritative for protocol behaviour, not for efficacy).
- **[E]** empirical measurement (benchmark, controlled test, or production metric with a stated method).
- **[P]** practitioner opinion / field report (useful, not proof).
- **[V]** vendor marketing or self-reported test with small n; treat as directional only.

---

## 1. Tool count vs selection accuracy and context cost; recommended ranges; when fewer tools hurt

### Takeaway
The evidence supports a **small, curated, outcome-level surface — roughly 5–15 tools for one domain server, with ~20 as a practical ceiling for weaker models**. But tool count is a proxy: what actually degrades selection is *schema tokens*, *name/description overlap*, and *argument complexity*. Fewer tools hurt when the surface no longer covers the domain and the model must guess or chain calls; that cost is real but is measured as coverage, not as "confusion."

### Cited Findings

**Context cost is linear and large**
- 58 tools across five servers (GitHub 35, Slack 11, Sentry/Grafana 5 each, Splunk 2) consumed **~55K tokens** before any conversation; Anthropic has observed **134K tokens** of tool definitions before optimization. [E/P — Anthropic, "Introducing advanced tool use", 2025-11-24](https://www.anthropic.com/research/advanced-tool-use)
- The official GitHub MCP server exposes **94 tools ≈ 17.6K tokens**; the official Atlassian server consumes **~10K tokens** for Jira + Confluence alone; combining several large servers easily exceeds **30K tokens per request**. [V — Atlassian, "MCP Compression", 2026-03-29](https://www.atlassian.com/blog/development/mcp-compression-preventing-tool-bloat-in-ai-agents)
- A widely repeated rule of thumb is **200–500 tokens per tool definition**, so 150 tools ≈ 30K–75K tokens. [V/secondary — Albato summary citing Lunar.dev, 2026](https://albato.com/blog/publications/embedded-mcp-context-bloat-hallucinations) — no primary measurement given; use only as an order-of-magnitude estimate.

**Selection accuracy falls with count and similarity**
- Speakeasy's Dog CEO test (one tool per breed): at **107 tools**, Claude Sonnet 3.5 hallucinated a non-existent "Golden Retriever" endpoint and Claude Desktop frequently aborted; qwen3-1.7b hallucinated tool names. At **40 tools** it called 3/4 correctly; at **20 tools** 19/20; at **10 tools** zero errors; **20 tools split across two focused servers** also produced zero errors. [V — Speakeasy, "Why less is more for MCP" (undated, accessed 2026-11-06)](https://www.speakeasy.com/mcp/tool-design/less-is-more)
- Speakeasy, citing Drew Breunig's review of two papers: for large models **30 tools** is the threshold where descriptions begin to overlap, **100+ tools** is near-certain failure, and retrieval to stay **under 30** produced a reported **3×** improvement; for smaller models **19 tools** was the sweet spot and **46** the failure point, with dynamic selection giving **44%** improvement. [V/secondary — Speakeasy summary of Breunig, 2025-06-26](https://www.dbreunig.com/2025/06/26/how-to-fix-your-context.html) — second-hand; the underlying papers are not named in the summary.
- AWS Prescriptive Guidance states the trade-off explicitly: **too few tools** and the model "might not be able to collect the right context… so it will take the best guess"; **too many** and it "may get confused about the right tool selection and sequence, leading to hallucinations." No number is given; the recommendation is to scope by workflow granularity, not endpoint count. [P — AWS Prescriptive Guidance, "MCP tool design strategy", accessed 2026-11-06](https://docs.aws.amazon.com/prescriptive-guidance/latest/mcp-strategies/mcp-tool-strategy.html)

**The strongest quantitative work treats shortlist depth as adaptive**
- arXiv 2605.24660 ("How Many Tools Should an LLM Agent See? A Chance-Corrected Answer", v2 2026-06-07) evaluates registries of **20–3,251 tools**. On BFCL (370 tools) a learned policy presents **~7 tools on average** and reaches **90.3%** coverage versus **90.8%** for always showing 50. On ToolBench (3,251 tools) a fixed shortlist of 5 gets higher aggregate coverage (**64.7% vs 61.9%**) but finds **0%** of hard queries (gold tool ranked 6–20), while the adaptive policy finds **16.7%** by searching deeper. Downstream, Claude Sonnet 4.6 selects the correct tool **93.1%** of the time with adaptive short lists vs **87.1%** with a fixed 5 — widening to **76.8% vs 60.9%** on medium queries. [E — arXiv 2605.24660, 2026-05-23 / rev. 2026-06-07](https://arxiv.org/abs/2605.24660)
- The same paper's framing is the cleanest statement of the trade-off: "Show too many tools and the model struggles to choose. Show too few and the correct tool may not appear." [E]

**Practitioner ranges**
- Phil Schmid: **5–15 tools per server**, one server one job, delete unused tools, split by persona (admin vs user). [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)
- Speakeasy: curate per use case; their own test showed 20 tools split into two 10-tool servers outperforming one 20-tool server. [V]
- A 2026 vendor summary claims a "practical ceiling of 15–20 tools" and gives a BFCL-by-tool-count table (e.g. 96% single-tool → 76% at 20+ for a frontier model). The model names and numbers could not be verified against BFCL and look speculative. [V — Presenc AI, 2026](https://presenc.ai/research/ai-agent-tool-calling-accuracy-benchmarks-2026) — **low confidence; do not cite as fact.**

**Real-world large-surface failure**
- MCP-Universe (231 tasks, 11 real servers, **133 tools**) reports frontier success rates of **43.72%** (GPT-5), **33.33%** (Grok-4), **29.44%** (Claude-4.0-Sonnet), and names "long-context" and "unknown-tools" as two of the benchmark's central challenges. [E — arXiv 2508.14704, Aug 2025](https://arxiv.org/abs/2508.14704)
- The OpenReview revision reports **GPT-5-High 44.16%** rather than 43.72% — the paper and the revision disagree slightly; cite whichever version you use and note it. [E — OpenReview forum 3857](https://openreview.net/forum?id=ffYd6uJpJE)

### Inferences
- Two different regimes are being measured. **Selection accuracy** improves as the candidate set shrinks; **coverage** improves as it grows. A fixed global number optimizes one at the expense of the other. The defensible design target is therefore "small enough to disambiguate, large enough that every real task has a first-class tool," plus a discovery path for the long tail.
- For a **domain-specific server**, 8–12 outcome tools is a safer target than 15–20: the domain vocabulary is narrower, so overlap is avoidable, and 8–12 keeps schema cost around 2–6K tokens at commonly reported per-tool costs.
- If a surface must exceed ~20 tools, the evidence favours adding a **discovery layer** (search/guide tool, deferred loading, compression) over deleting capability. That preserves coverage while restoring a small decision set.
- "Fewer tools hurt" is best operationalized as *coverage@k* (does the correct tool appear at all?), not as end-to-end success, because a missing tool can be worked around by chaining or code.

### Gaps
- No peer-reviewed study isolates tool count using 2026 frontier models with real domain servers; the cleanest numbers are either small vendor tests (Speakeasy, n≈4 prompts) or retrieval benchmarks (BFCL/ToolBench) that are not MCP servers.
- No source quantifies the *end-to-end* cost of removing one tool from a coherent domain set (the "fewer hurts" side) — only coverage.
- Per-tool token cost (200–500) is folklore; actual cost depends heavily on JSON Schema verbosity and description length.

---

## 2. Recommended description structure, with before/after examples

### Takeaway
The convergent structure is: **what it does → when to use it → when not to / what it does not return → parameter semantics and constraints → what comes back**, in 3–4+ sentences, with unambiguous parameter names and enums. Anthropic calls this "describe it to a new hire"; no official source mandates a "next step" field, but practitioners add one (what to call next, or what the result enables). Validated input examples are the highest-leverage addition for ambiguous formats.

### Cited Findings

**The canonical guidance**
- Anthropic's API docs: "Provide extremely detailed descriptions. This is by far the most important factor in tool performance." A description should explain **what the tool does, when it should be used (and when it shouldn't), what each parameter means and how it affects behaviour, and important caveats or limitations**; "aim for at least 3–4 sentences… more if the tool is complex." [S — Anthropic, "Define tools", living doc accessed 2026-11-06](https://platform.claude.com/docs/en/agents-and-tools/tool-use/implement-tool-use)
- Anthropic engineering: write descriptions "how you would describe your tool to a new hire"; make implicit context explicit (query formats, niche terminology, resource relationships); avoid ambiguity and enforce it with strict data models; name parameters unambiguously — `user_id`, not `user`. "Even small refinements to tool descriptions can yield dramatic improvements"; description refinements took Claude Sonnet 3.5 to SOTA on SWE-bench Verified. [P/E — Anthropic, "Writing effective tools for agents", 2025-09-11](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Phil Schmid: "Docstrings are instructions" — specify **when to use** ("Use when the user asks about order status"), **how to format arguments** ("Email must be lowercase"), and **what to expect back** ("Returns order ID and current status"). Error messages are context too. [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)
- Block: "Tool names, descriptions, and parameters are treated as prompts for the LLM"; use Pydantic (or equivalent) field descriptions so schema and documentation stay in sync. [P — Block Engineering, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)

**Before/after: description quality (Anthropic's own example)**
- Poor: `"Gets the stock price for a ticker."` with `ticker` having no description.
- Good: `"Retrieves the current stock price for a given ticker symbol. The ticker symbol must be a valid symbol for a publicly traded company on a major US stock exchange like NYSE or NASDAQ. The tool will return the latest trade price in USD. It should be used when the user asks about the current or most recent price of a specific stock. It will not provide any other information about the stock or company."` with `ticker` described as "The stock ticker symbol, e.g. AAPL for Apple Inc."
- Anthropic's verdict: the good version explains what it does, when to use it, what it returns, and what the parameter means; the poor one "leaves Claude with many open questions." [S — Anthropic, "Define tools"](https://platform.claude.com/docs/en/agents-and-tools/tool-use/implement-tool-use)

**Before/after: naming + description for searchability**
- Good: `search_customer_orders` — "Search for customer orders by date range, status, or total amount. Returns order details including items, shipping, and payment info."
- Bad: `query_db_orders` — "Execute order query."
- Rationale: tool search matches on names and descriptions, so vague names degrade discovery. [P — Anthropic, "Introducing advanced tool use", 2025-11-24](https://www.anthropic.com/research/advanced-tool-use)

**Before/after: outcome tool vs API wrapper (Phil Schmid, Gmail)**
- Before: `messages_list(query, max_results)` + `messages_get(message_id, format)` returning nested `payload.body.data`; `messages_send({"raw": base64url(RFC2822)})`; `drafts_create` with nested objects. The agent must build base64 MIME and parse nested JSON.
- After: `gmail_search(query, limit=10) -> [{id, subject, sender, date, snippet}]`, `gmail_read(message_id) -> {subject, sender, body, attachments}`, `gmail_send(to, subject, body, reply_to_id=None) -> {success, message_id}`. [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)

**Before/after: consolidation (Block)**
- Google Calendar v1 exposed `list_calendars`, `list_calendar_events`, `retrieve_timezone`, `retrieve_calendar_free_busy_slots` — four CRUD-shaped tools, no analytics, heavy chaining. v2 exposes one `query_database(sql, time_min, time_max)` over DuckDB tables, plus documented macros. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)
- Linear went from **30+ tools** to `get_issue_info(issue_id, info_category)` (enum-documented) and then to **two tools**, `execute_readonly_query` and `execute_mutation_query`, with the GraphQL schema moved into a separately fetchable `get_linear_graphql_schema` tool. [P — Block]

**Naming**
- Namespace by service and resource (`asana_search`, `jira_search`, `asana_projects_search`, `asana_users_search`). Anthropic found prefix- vs suffix-based namespacing has "non-trivial effects" on evaluations, and **effects vary by LLM** — choose by your own eval. [P/E — Anthropic, "Writing effective tools"](https://www.anthropic.com/engineering/writing-tools-for-agents)
- `{service}_{action}_{resource}` is the practitioner convention (`slack_send_message`, `linear_list_issues`, `sentry_get_error_details`). [P — Phil Schmid]

**Examples as description, not schema**
- `input_examples` improved accuracy from **72% → 90%** on complex parameter handling in Anthropic's internal testing; cost is **~20–50 tokens** for simple examples and **~100–200** for complex nested objects; recommended 1–5 examples, only where correct usage is not obvious from the schema. [E/P — Anthropic, "Introducing advanced tool use"](https://www.anthropic.com/research/advanced-tool-use)
- Three examples can teach format conventions, nested-object construction, and optional-parameter correlation simultaneously (the `create_ticket` example: dates `YYYY-MM-DD`, IDs `USR-XXXXX`, kebab-case labels). [P — same]

### Inferences
A defensible six-part template, synthesized from the above (this specific ordering is my inference, not a quoted standard):

1. **Identity/outcome** — one sentence, verb-first, names the object and the result ("Compiles a domain library into an executable artifact set").
2. **Use when** — the user-visible intent or phase that should trigger it.
3. **Do not use when / does not return** — the nearest competing tool and the boundary; this is the highest-value line because wrong-tool selection is the dominant failure.
4. **Inputs** — per-parameter meaning, units, formats, enums, defaults, and which combinations are valid.
5. **Returns** — shape, units, identifiers the caller will need next, and truncation/pagination behaviour.
6. **Next step / recovery** — what to call with the returned handle, or what to do on the common error.

Keep the prose under ~150–200 words per tool; push long schemas, DSL grammars, and worked examples into a guide/resource (see §5). Prefer enums and `Literal` types over free-text strings — every constrained choice removed from the model is a decision it cannot get wrong.

### Gaps
- No controlled study compares description templates head-to-head; Anthropic's evidence is that descriptions matter and that iteration helps, not that a specific six-part form wins.
- The "next step" element is practitioner framing; MCP's own docs only require enough detail for the model to choose and call correctly.
- Prefix vs suffix naming is explicitly model-dependent; no universal rule exists.

---

## 3. Response design: structured vs text, token efficiency, pagination, truncation, next affordances, error envelopes

### Takeaway
Return the **smallest high-signal payload that lets the model take the next action**. Prefer `structuredContent` validated by an `outputSchema`, with a text fallback for compatibility; default to concise and offer detail on request; paginate/truncate with explicit, machine-readable continuation metadata; report recoverable failures as tool results (`isError: true`) with a specific corrective action, and reserve protocol errors for things the model cannot fix.

### Cited Findings

**Signal over completeness**
- Return "only high signal information"; prioritize contextual relevance over flexibility; avoid low-level identifiers (`uuid`, `256px_image_url`, `mime_type`) in favour of fields like `name`, `image_url`, `file_type`. [P — Anthropic, "Writing effective tools"](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Resolving arbitrary alphanumeric UUIDs into semantically meaningful names (or even a 0-indexed scheme) "significantly improves Claude's precision in retrieval tasks by reducing hallucinations." [P/E — same]
- Offer a `response_format` enum (`concise` / `detailed`): in Anthropic's Slack example, detailed = **206 tokens**, concise = **72 tokens** (~⅓), with IDs available only in detailed so they can drive follow-up calls. [E — same]
- Response *structure* (XML vs JSON vs Markdown) affects evaluation performance and "there is no one-size-fits-all solution"; choose by eval. [P — same]
- Block's field guidance: prefer **Markdown or XML over raw JSON** because they are typically more token-efficient; if JSON is required, keep the schema simple and avoid long lists. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)

**Structured content and output schemas**
- MCP tool results may carry `content` (unstructured blocks) and `structuredContent` (any JSON value conforming to the tool's `outputSchema`). A tool returning structured content **SHOULD** also return the serialized JSON in a `TextContent` block for backward compatibility. [S — MCP spec, Tools, 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- If an output schema is provided, servers **MUST** return conforming structured results and clients **SHOULD** validate them. [S — same]
- GitHub's server now returns typed `structuredContent` with **compact DTOs** for modern protocol versions: directory entries keep file metadata and useful HTML/download links but **omit API and hypermedia routing URLs**; legacy clients keep byte-for-byte text. [S/vendor — GitHub MCP Server README, accessed 2026-11-06](https://github.com/github/github-mcp-server)

**Pagination, truncation, and budget**
- Implement "some combination of pagination, range selection, filtering, and/or truncation with sensible default parameter values" for anything that could consume context. Claude Code restricts tool responses to **25,000 tokens by default**. [P/E — Anthropic, "Writing effective tools"](https://www.anthropic.com/engineering/writing-tools-for-agents)
- When truncating, steer the agent: encourage many small targeted searches over one broad search, and make the truncation message tell it what to narrow. [P — same]
- Practitioner pagination contract: respect a `limit` (default **20–50**), return `has_more`, `next_offset`, `total_count`; never load all results into memory. [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)
- Block's three-way decision for oversized output: **raise a tool execution error**, **reduce with an explicit truncation note**, or **paginate** — chosen by semantics. Goose's file reader errors above **400KB** with a message telling the agent to use `head`/`tail`/`sed -n '500,1000p'`. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)

**Error envelopes**
- MCP defines two mechanisms: **protocol errors** (unknown tool, malformed request, server error) returned as JSON-RPC errors, and **tool execution errors** (API failure, input validation, business logic) returned in the result with `isError: true`. Clients **SHOULD** pass tool execution errors to the model "to enable self-correction"; protocol errors are "less likely to result in successful recovery." [S — MCP spec, Tools, 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- Anthropic contrasts an "unhelpful" error (opaque code/traceback) with a "helpful" one that communicates specific, actionable improvements; this is a prompt-engineering surface, not just diagnostics. [P — Anthropic, "Writing effective tools"](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Block: "Prefer actionable error messages that enable recovery by the agent, instead of vague prompt instructions like: 'Don't return large files!'" [P — Block]

**"Next affordances"**
- MCP's stateful-tool guidance makes the mechanism explicit: a creation tool returns a handle, and subsequent tools accept it; the model carries it forward. [S — MCP spec, "Stateful Tools", 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- Block's guidance for unavoidable chaining: "clearly outline the steps & dependencies in your tool's instructions and the output of previous steps are concise." [P — Block]
- Atlassian's compression interface exposes `get_tool_schema(tool_name)` → `invoke_tool(tool_name, tool_input)` → optional `list_tools()`, i.e. the response itself names the next call. [V — Atlassian, 2026-03-29](https://www.atlassian.com/blog/development/mcp-compression-preventing-tool-bloat-in-ai-agents)

### Inferences
- A response should answer three questions in order: **did it work**, **what is the result**, **what can I do next**. Identifiers needed for the next call belong in the result even in concise mode, or the agent is forced into a second lookup.
- "Structured vs text" is not either/or. The safe default is: `structuredContent` for the machine contract, plus a short human-readable text block that states the headline result and the next action. This also satisfies MCP's backward-compatibility recommendation.
- Fail-closed vs recoverable maps cleanly onto MCP's two error classes: malformed/unknown/unauthenticated requests are protocol errors (fail closed, do not invite retry); validation, business-rule, and transient upstream failures are execution errors with a hint (recoverable). A destructive operation that cannot be verified should fail closed even though it is technically an execution error.
- "Next affordances" should be *data*, not prose: a `next_cursor`, a `handle`, an `allowed_actions` array, or a `suggested_tool` field is cheaper and more reliable than a paragraph of instructions.

### Gaps
- No published benchmark isolates response format (JSON vs Markdown vs XML) or concise-vs-detailed on the same task; Anthropic explicitly says to evaluate per task.
- There is no standard schema for "next affordances" or an error envelope beyond `isError`; every server invents one.
- The 25K-token cap is Claude Code-specific; other hosts differ, so servers cannot assume it.

---

## 4. State and identity: implicit per-connection workspace vs explicit session IDs; idempotency; destructive annotations; multi-model handling

### Takeaway
**Do not build on implicit per-connection state.** The MCP specification (2026-07-28) says outright that MCP has no protocol-level session and that servers needing cross-call state should return an **explicit, opaque handle** and accept it on later calls. Tool *lists* must also be stable per credential, not per connection. Annotate every tool with the four risk hints, keep one risk level per tool, and validate the surface on at least two model sizes because selection behaviour is model-dependent.

### Cited Findings

**No implicit session; explicit handles**
- "MCP has no protocol-level session, so a server cannot rely on implicit per-connection state to relate one tool call to the next. Servers that need to maintain state across calls — a shopping cart, an open browser context, a database transaction — should do so by returning an explicit handle from a creation tool and accepting that handle as an argument on subsequent calls." [S — MCP spec, "Stateful Tools", 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- The spec's basket example: `create_basket` returns `{"basket_id": "bsk_a1b2c3"}`; `add_item` takes `basket_id`. "The model is responsible for carrying `basket_id` forward; the server stores the cart contents under that key." [S — same]
- Handle design rules from the spec: **authorization** (a handle is a name, not a capability — validate the caller against it every call), **opacity** (do not encode internal structure), **lifetime** (state retention in the creation tool's description, e.g. "baskets expire after 24 hours"), and **expiry errors** (an unknown/expired handle returns a tool execution error that says so, so the model can create a new one). [S — same]

**Tool lists must not be connection-scoped**
- `tools/list` "MUST NOT vary per-connection or as a side effect of other requests on the connection. The set MAY vary by the authorization presented on the request." [S — MCP spec, Tools, 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- Servers **SHOULD** return tools in deterministic order, "which improves LLM prompt cache hit rates when tools are included in model context." [S — same]
- `tools/list` supports pagination and caching (`ttlMs`, `cacheScope`), reinforcing that the list is a cacheable catalog, not a per-session view. [S — same]
- This directly rules out the "implicit per-connection workspace that changes which tools exist" pattern: it is both spec-noncompliant and cache-hostile. A workspace *identifier* passed as an argument is compliant; a workspace inferred from the socket is not.

**Idempotency and destructive annotations**
- `ToolAnnotations` defines `title`, `readOnlyHint` (default false), `destructiveHint` (default true), `idempotentHint` (default false), `openWorldHint` (default true). Defaults are deliberately cautious: an unannotated tool is assumed non-read-only, potentially destructive, non-idempotent, and open-world. [S — MCP blog, "Tool Annotations as Risk Vocabulary", 2026-03-16](https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/); schema at [2025-11-25 ToolAnnotations](https://modelcontextprotocol.io/specification/2025-11-25/schema#toolannotations)
- Client behaviours the hints enable: `readOnlyHint: true` → skip confirmation; `destructiveHint: true` → warn; `idempotentHint: true` → safe to retry on failure; `openWorldHint: true` → scrutinize output for untrusted content and flag a trust-boundary cross. [S — same]
- Clients **MUST** treat annotations as untrusted unless the server is trusted: "a malicious or misconfigured server could claim `readOnlyHint: true` while actually deleting files." [S — same; also MCP spec Tools security note]
- Block: each tool should stick to **one risk level** — read-only (low risk) or non-read (higher risk) — because mixed tools make permission decisions hard; bundle related read-only actions instead. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)
- Anthropic recommends marking idempotent, parallel-safe operations as such because programmatic orchestration benefits from safe retry. [P — Anthropic, "Introducing advanced tool use", 2025-11-24](https://www.anthropic.com/research/advanced-tool-use)
- GitHub's server operationalizes risk at the server level: `--read-only` skips all write tools even if explicitly requested; `--lockdown-mode` filters content from authors without push access and is explicitly "not an authorization boundary." [S/vendor — GitHub MCP Server README](https://github.com/github/github-mcp-server)

**Multi-model handling**
- Prefix vs suffix namespacing has "non-trivial effects" on tool-use evaluations and **effects vary by LLM**; Anthropic recommends choosing the scheme by evaluation. [P/E — Anthropic, "Writing effective tools"](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Small models fail earlier: qwen3-1.7b handled 10 and 20 tools cleanly but hallucinated at 40 and 107. [V — Speakeasy](https://www.speakeasy.com/mcp/tool-design/less-is-more)
- Harness as well as model matters: in MCP-Universe, GPT-OSS-120B's success rate moved from **11.26% to 25.54%** when switching from the OpenAI Agent SDK to direct function calling. [E — OpenReview review of MCP-Universe, 2025-10](https://openreview.net/forum?id=ffYd6uJpJE)
- Anthropic's deferred-loading feature is platform-specific (`defer_loading`, `tool_search_tool_regex_20251119`), so a server cannot assume every host supports it. [S — Anthropic, "Introducing advanced tool use"]

### Inferences
- Treat the **workspace/session handle as a first-class argument**, not ambient state: `create_workspace()` → `workspace_id`, or accept a stable caller-supplied key. Document its lifetime in the tool that creates it, and return a specific "expired/unknown handle" error.
- Idempotency needs more than a hint. Pair `idempotentHint` with an explicit **idempotency key** argument on writes, because the protocol has no built-in request deduplication.
- Annotation discipline: `readOnlyHint: true` for every search/read/list; `destructiveHint: false` for additive creates; `destructiveHint: true` for delete/overwrite/revoke/spend; `openWorldHint: false` for closed-domain tools. Test for contradictions (read-only + destructive is invalid).
- Do not vary the tool *list* by connection or by workspace. If capability must vary, vary it by credential/scope (allowed by spec) and keep a stable catalog.
- Validate the surface on a small model as well as a frontier one; a 15-tool surface that is clean on Claude can still fail on an 8B model, and multi-model hosts are common.

### Gaps
- MCP has no standard handle format, lifetime convention, or idempotency-key convention; the spec's guidance is explicitly non-normative.
- No study measures how often clients actually honour annotations; adoption appears uneven across hosts.
- There is no published guidance on tenancy isolation for multi-user remote servers beyond OAuth audience validation and per-request scoping (GitHub's security guide covers the auth mechanics but not tool-surface design). [P — GitHub, "How to build secure and scalable remote MCP servers", 2025-07-25](https://github.blog/ai-and-ml/generative-ai/how-to-build-secure-and-scalable-remote-mcp-servers/)

---

## 5. Progressive disclosure: server instructions, resources, prompts, guide tools, examples; always-on vs on-demand

### Takeaway
Always-on context should be a **short server instruction plus a handful of core tools**; everything else should be fetched on demand through a **guide/search tool, resources, or deferred loading**. Three independent implementations report **85%–98.7%** token reductions with no measured loss of task quality, and one reports accuracy *gains*. The most portable pattern for a domain server is a `guide`/`get_schema` tool, because it works regardless of host support for deferred loading or code execution.

### Cited Findings

**Deferred loading / tool search**
- Anthropic's Tool Search Tool keeps only the search tool (~500 tokens) plus deferred definitions out of context; the agent sees **3–5 relevant tools (~3K tokens)** after searching, for **~8.7K tokens total vs ~77K** in the traditional approach — an **85% reduction** while preserving 95% of the context window. [E — Anthropic, "Introducing advanced tool use", 2025-11-24](https://www.anthropic.com/research/advanced-tool-use)
- Internal MCP evaluations improved: **Opus 4 49% → 74%**, **Opus 4.5 79.5% → 88.1%**. [E — same]
- Recommended configuration: keep the **3–5 most-used tools always loaded**, defer the rest; use it when definitions exceed **10K tokens**, when selection accuracy is poor, or with **10+ tools**; less beneficial under 10 tools or when all tools are used every session. [P — same]
- Prompt caching is preserved because deferred tools are excluded from the initial prompt entirely. [P — same]

**Code execution / Code Mode**
- Presenting MCP servers as a filesystem of TypeScript modules lets the agent read only the definitions it needs: **150,000 → 2,000 tokens (98.7% saving)** in Anthropic's Google Drive → Salesforce example. [E — Anthropic, "Code execution with MCP", 2025-11-04](https://www.anthropic.com/engineering/code-execution-with-mcp)
- Alternative: a `search_tools` tool with a **detail-level parameter** (name only / name + description / full definition) so the agent controls how much schema it pays for. [P — same]
- Intermediate results can be filtered in the execution environment before entering context (10,000 spreadsheet rows → 5 logged rows), and PII can be tokenized so it never reaches the model. [P — same]
- Cloudflare's independent "Code Mode" converts the MCP schema into a TypeScript API and exposes **one tool that executes code** in an isolate; the agent handles "many more tools, and more complex tools" this way, and multi-call chains avoid round-tripping intermediate results. Caveat: as published in Sept 2025, "the entire API is loaded" into context — so the win is compactness and composition, not yet on-demand schema loading. [V — Cloudflare, 2025-09-26](https://blog.cloudflare.com/code-mode/)
- Code execution adds a sandbox, resource limits, and monitoring requirement that direct tool calls avoid. [P — Anthropic, "Code execution with MCP"]

**Compression / wrapper tools**
- Atlassian's `mcp-compressor` replaces a server's toolset with `get_tool_schema(tool_name)`, `invoke_tool(tool_name, tool_input)`, and optional `list_tools()`, with four verbosity tiers. On a 94-tool GitHub-style server: **17,600 → 3,900 (low) → 3,300 (moderate) → 2,200 (strong) → 500 (max)** tokens, a **70–97%** reduction. [V — Atlassian, 2026-03-29](https://www.atlassian.com/blog/development/mcp-compression-preventing-tool-bloat-in-ai-agents)
- Reported quality impact: "almost no impact on end-to-end quality" because exact schemas remain fetchable on demand. [V — same]
- Design principles: compression must **preserve structure, not remove it**; namespace compressed tools (`mcp__atlassian__*`); keep the wrapper stable for prompt caching; give the agent an explicit discovery path. [P — same]

**Server instructions, resources, prompts**
- MCP's `initialize` response can carry **server instructions**, which Goose uses to construct the system prompt; tool annotations feed its approval UI. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)
- MCP's primitives are tools (model-controlled), resources (application-driven context), and prompts (user/agent-invoked workflows); servers may also return resource links and embedded resources from tool results. [S — MCP spec, Tools, 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- Block's workaround for a large GraphQL schema: keep it out of the always-on instructions and expose `get_linear_graphql_schema`, called once at session start. [P — Block]
- Anthropic's Tool Use Examples are a middle tier: concrete validated inputs that teach conventions without a prose manual, at 20–200 tokens each. [E/P — Anthropic, "Introducing advanced tool use"]

**Skills as the outer tier**
- Agent Skills use filesystem-based progressive disclosure: metadata (~100 tokens) at startup, `SKILL.md` body (<5K tokens) when triggered, resources (scripts) as needed. Skills and MCP are complementary: Skills teach *when and how* to combine tools; MCP provides the structured interface. [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)
- MCP client guidance now documents "programmatic tool calling / code mode" and combining it with direct tool calls: "For a handful of tools, this is perfectly reasonable." [S — MCP, Client Best Practices, 2026-07-28](https://modelcontextprotocol.io/docs/2026-07-28/develop/clients/client-best-practices)

### Inferences
A four-tier disclosure model for a domain server (synthesis):

| Tier | Always in context? | Budget | Contents |
|---|---|---|---|
| L0 Server instructions | yes | ≤300 tokens | What the domain is, the 3–5 core verbs, the "start here" tool, hard constraints |
| L1 Core tools | yes | 3–5 tools | The handful used in nearly every session |
| L2 Guide / schema tool | tool is always on; payload on demand | 200–600 tokens per fetch | Grammar/DSL reference, per-tool detail, enum tables, worked examples |
| L3 Resources / prompts / examples | on demand | unbounded | Long docs, golden examples, templates, corpora |

- For a server whose meaning lives in a DSL or grammar (a compiler, query language, policy engine), L2 is essential and should be **sectioned** (fetch one heading, not the whole guide). This is the same shape as Atlassian's `get_tool_schema` and Block's `get_linear_graphql_schema`.
- Prefer the guide-tool pattern over code mode when the host may not have a sandbox; prefer deferred loading only when the host supports it. The guide tool is the only pattern that works on every MCP host today.
- Keep examples out of always-on context unless the schema is genuinely ambiguous; they are cheap per item but multiply across 15 tools.

### Gaps
- No head-to-head published comparison of guide-tool vs tool-search vs code-mode vs compression on the same domain and task suite.
- Server-instruction support and size limits vary by client; no standard budget exists.
- MCP resources are inconsistently surfaced by hosts, so relying on them for critical guidance is risky; a guide *tool* is more reliable than a resource.

---

## 6. Evaluating a tool surface: task suites, wrong-tool rate, calls/task, tokens, recovery; published harnesses

### Takeaway
Build a **task suite from real transcripts (20–50 tasks is enough to start)**, grade **outcomes rather than prescribed call sequences**, and track selection and cost together: **wrong-tool rate, calls per task, tokens per task, and recovery after an injected error**. Read the transcripts. The published MCP-specific harnesses (MCP-Universe, τ/τ²-bench) are useful external references but are cross-server and hard-task-biased, so they do not replace a server-specific suite.

### Cited Findings

**Anthropic's tool-evaluation loop**
- Generate tasks grounded in real usage, with verifiable outcomes; strong tasks require multiple tool calls ("potentially dozens"), weak tasks are single-lookup. [P — Anthropic, "Writing effective tools", 2025-09-11](https://www.anthropic.com/engineering/writing-tools-for-agents)
- Run programmatically with a simple agentic `while` loop per task; optionally specify expected tools, but avoid over-specifying because multiple paths can be valid. [P — same]
- Metrics beyond top-level accuracy: **total runtime of individual tool calls and tasks, total number of tool calls, total token consumption, and tool errors**. "Lots of redundant tool calls might suggest some rightsizing of pagination"; "lots of tool errors for invalid parameters might suggest tools could use clearer descriptions or better examples." [P/E — same]
- Read the agents' reasoning *and* the raw transcripts, because "what agents omit in their feedback… can often be more important than what they include." [P — same]
- Use a **held-out test set** so description tuning does not overfit; Claude Code can analyse concatenated transcripts and refactor tools. [P/E — same]
- Concrete description bug found this way: Claude appended `2025` to the web-search `query` parameter, biasing results; fixed in the description. [E — same]

**Anthropic's eval methodology (general, directly applicable)**
- Vocabulary: task, trial, grader, transcript, outcome, evaluation harness, agent harness, suite. [P — Anthropic, "Demystifying evals for AI agents", 2026-01-09](https://www.anthropic.com/research/demystifying-evals-for-ai-agents)
- **20–50 simple tasks drawn from real failures** is "a great start"; more mature agents need larger, harder suites. [P — same]
- A good task is one where two domain experts would independently reach the same pass/fail verdict; pair each task with a **reference solution**; a 0% pass@100 across many trials usually signals a broken task, not an incapable agent. [P — same]
- Build **balanced** sets: test where a behaviour should occur *and* where it should not (their web-search evals had to cover both undertriggering and overtriggering). [P — same]
- Do not grade the exact tool-call path: "we've found this approach too rigid… it's often better to grade what the agent produced, not the path it took." [P — same]
- Use **pass@k** for "one success is enough" and **pass^k** for consistency; e.g. a 75% per-trial rate gives (0.75)³ ≈ 42% pass^3. [E — same]
- Grade with code where possible, LLM rubrics where necessary, humans to calibrate; give LLM graders an "Unknown" escape hatch and grade dimensions with isolated judges. [P — same]
- Watch for **eval saturation**, and audit graders: Opus 4.5 scored 42% on CORE-Bench until rigid/ambiguous grading was fixed, then **95%**. [E — same]

**MCP-specific harnesses**
- **MCP-Universe** (Salesforce AI Research): 6 domains, 11 real MCP servers, 231 tasks, 133 tools; execution-based evaluators — **format** (agent-format compliance), **static** (time-invariant content), **dynamic** (live ground truth for time-sensitive tasks). Reports **success rate** and **average steps**; frontier models under 50%. Explicitly names **long-context** and **unknown-tools** as core challenges. [E — arXiv 2508.14704, Aug 2025](https://arxiv.org/abs/2508.14704); [leaderboard](https://mcp-universe.github.io/)
- Its reviewers flagged: no standard deviations or error bars; binary success rate masks partial progress; **no token, cost, or latency metrics**; error types not broken down (selection vs parameters vs planning vs output parsing). The authors added average evaluator score (AE) alongside success rate. [E — OpenReview reviews, Oct–Nov 2025](https://openreview.net/forum?id=ffYd6uJpJE)
- **τ-bench / τ²-bench**: multi-turn, simulated-user benchmarks for tool-using agents in retail/airline domains; cited by Anthropic as the reference for conversational-agent evals. [E — arXiv 2406.12045](https://arxiv.org/abs/2406.12045); [τ²-bench 2506.07982](https://arxiv.org/abs/2506.07982)
- **BFCL** and **ToolBench** are the retrieval/selection benchmarks used by the tool-count paper; BFCL is also used for downstream tool-choice validation. [E — arXiv 2605.24660]

**Wrong-tool rate as a first-class metric**
- Anthropic states that "the most common failures are wrong tool selection and incorrect parameters, especially when tools have similar names like `notification-send-user` vs `notification-send-channel`." [P — Anthropic, "Introducing advanced tool use", 2025-11-24](https://www.anthropic.com/research/advanced-tool-use)
- MCP-Universe's reviewers explicitly asked for a quantitative breakdown of incorrect tool selection, parameter misuse, and planning failures — evidence that this decomposition is not yet standard practice. [E — OpenReview]

### Inferences
A minimal, practical scorecard for one server (define these yourself; no standard exists):

| Metric | Definition | Why |
|---|---|---|
| **Selection accuracy@1** | First tool call is a valid tool for the task | Catches discoverability failures |
| **Wrong-tool rate** | First call is a *plausible but wrong* tool from the set | The dominant failure; distinct from "no tool" |
| **Coverage@k** | Correct tool appears within k candidates (if using discovery) | Separates retrieval from selection |
| **Calls per task** | Mean tool calls to completion | Detects missing consolidation |
| **Tokens per task** | Total tool-schema + result tokens | Detects response bloat |
| **Argument validity rate** | Calls accepted without schema/validation error | Detects description/schema ambiguity |
| **Recovery rate** | Task still succeeds after one injected tool error | Tests error-envelope quality |
| **pass@1 / pass^3** | Success on first trial / all three trials | Capability vs reliability |

- Run **3 trials** per task and report variance; a single run cannot distinguish a 70% from an 85% agent.
- Inject errors deliberately (invalid ID, expired handle, oversized result, rate limit) and measure whether the agent recovers — this is the only way to evaluate §3's error design.
- Keep a **held-out** set and re-run it after every description or schema change; description edits are code changes.
- Do not adopt MCP-Universe numbers as a quality target for your server: its tasks are chosen to be hard and its servers are third-party, so a domain server should expect much higher success and should measure its own coverage.

### Gaps
- No published, maintained harness specifically for evaluating a *single* MCP server's surface (MCP-Universe is cross-server; Anthropic's cookbook is a template, not a product).
- "Wrong-tool rate" is not a standardized metric with a standard denominator; the literature reports selection accuracy or success rate instead.
- No public data on how often production agents recover from MCP execution errors, or on the cost of a wrong tool call in tokens/latency.

---

## 7. Counterexamples: widely criticized MCP servers, what went wrong, and what changed

### Takeaway
The recurring anti-pattern is **1:1 REST wrapping**: one CRUD tool per endpoint, raw provider payloads, nested arguments, and no curation. The observed fixes are consistent: consolidate into intent/outcome tools, expose toolsets or per-tool allowlists, add an optional discovery/compression layer, return compact DTOs, and default to read-only.

### Cited Findings

**GitHub MCP Server — the canonical "too many tools" case**
- The official server exposed **94 tools ≈ 17.6K tokens**. [V — Atlassian, 2026-03-29](https://www.atlassian.com/blog/development/mcp-compression-preventing-tool-bloat-in-ai-agents)
- Current remediation in the server itself: `--toolsets` (enable only the groups you need) and `--tools` (individual allowlist); the README states that enabling only the needed toolsets "can help the LLM with tool choice and reduce the context size." [S/vendor — GitHub MCP Server README, accessed 2026-11-06](https://github.com/github/github-mcp-server)
- Additional hardening: `--read-only` skips write tools even when explicitly requested; `--lockdown-mode` filters content from authors without push access (explicitly "not an authorization boundary"); modern protocol versions return **compact DTOs** and `structuredContent`, omitting API/hypermedia URLs. [S/vendor — same]
- Secondary reporting says GitHub added toolsets and "semantic tool inclusion (via dynamic tool discovery)" after running into the problem internally. [P/secondary — Medium, "Let's just stop saying…", undated](https://medium.com/@scalablecto/lets-just-stop-saying-the-problem-with-mcp-is-context-bloat-ab051ac2341e) — corroborates the README but is not primary.

**Atlassian — measured bloat and a compression remedy**
- Jira + Confluence tools ≈ **10K tokens**; GitHub ≈ **17.6K**; multi-server setups ≈ **30K+**. Their fix wraps any server in `get_tool_schema` / `invoke_tool` / optional `list_tools`, reducing 17.6K to **500–3,900** tokens (70–97%) with "almost no impact" on quality. [V — Atlassian, 2026-03-29](https://www.atlassian.com/blog/development/mcp-compression-preventing-tool-bloat-in-ai-agents)

**Speakeasy — the "everything toolset" and Playwright proliferation**
- Anti-pattern illustration: a single "complete business management" server with **47 customer + 23 inventory + 31 financial + 18 reporting + 12 user-admin + 8 config = 139 tools**. [P — Speakeasy, "Design MCP tools", accessed 2026-11-06](https://www.speakeasy.com/mcp/tool-design)
- Companion report: reducing the Playwright MCP server from **26 tools to 8** "dramatically improves agent performance and reduces decision paralysis." [V — Speakeasy, "The Playwright proliferation problem"](https://www.speakeasy.com/blog/playwright-tool-proliferation) — vendor test, no full method published in the summary.

**Block — from CRUD wrappers to two tools**
- Google Calendar v1: four endpoint-shaped tools (`list_calendars`, `list_calendar_events`, `retrieve_timezone`, `retrieve_calendar_free_busy_slots`), no analytics, heavy chaining; v2: one `query_database` over DuckDB with documented macros. [P — Block, 2025-06-16](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers)
- Linear v1: **30+ tools**, 4–6 calls for "what is Bob working on"; v2 consolidated reads (`get_issue_info`); v3 reduced to **two tools** (`execute_readonly_query`, `execute_mutation_query`) plus a schema-fetch tool. [P — same]
- Stated reason: "LLMs are improving at planning but still it's hard for them to chain together 20 tool calls today." [P — same]

**Cloudflare — the extreme end**
- Full native MCP exposure reportedly reached **~1.17M tokens** pre-Code-Mode, exceeding any standard window; Code Mode replaced it with a single code-execution tool. [V/secondary — getunblocked summary, 2026-05-16](https://getunblocked.com/blog/mcp-tool-overload/) — the 1.17M figure is not in Cloudflare's own post, so treat as second-hand.
- Cloudflare's own framing: tool-calling tokens are "things LLMs have never seen in the wild," whereas code is abundant in training data; hence agents handle more and more complex tools when presented as a typed API. [V — Cloudflare, 2025-09-26](https://blog.cloudflare.com/code-mode/)

**Phil Schmid — the Gmail wrapper**
- The "before" server required the agent to construct `{"raw": base64url(RFC2822)}` and parse `payload.body.data`; generic names (`messages_list`, `messages_get`) collided with other servers. The "after" is three flat, outcome-named tools with curated returns. [P — Phil Schmid, 2026-01-21](https://www.philschmid.de/mcp-best-practices)

### Inferences
- The anti-pattern is not "many tools" per se; it is **many tools that mirror an API's resource model instead of the user's task model**. A 40-tool server of genuinely distinct outcomes is different from 40 CRUD verbs over five resources.
- The remediation order that appears in every case: (1) consolidate by workflow, (2) curate by persona/use case or toolset, (3) add discovery/compression only if the curated surface is still large, (4) compact the payloads.
- Read-only-by-default is a recurring safety and usability choice (GitHub `--read-only`, Block's one-risk-level rule). It also improves approval UX, which is a real adoption constraint.
- None of the counterexamples publish a before/after *task-success* number for the same suite; the evidence for improvement is mostly token counts, anecdote, and small vendor tests.

### Gaps
- No public, rigorous before/after evaluation of the GitHub MCP server's toolset redesign (success rate, wrong-tool rate, tokens/task).
- The "1.17M tokens" Cloudflare figure and the "Playwright 26→8" result are second-hand/vendor-reported.
- Little published evidence on how *users* experience large tool surfaces (approval fatigue, permission granularity), as opposed to model accuracy.

---

## 8. Synthesis: guidance for a minimal, token-friendly, outcome-oriented tool surface

> This section is inference and design judgement built on the cited findings above, not a summary of a single source. Treat the numbers as defaults to test, not laws.

### Takeaway
Ship **8–12 outcome-level tools plus one guide tool**, keep always-on context under ~1–2K tokens, return compact structured results with explicit next handles, make state explicit, annotate every tool, and validate with a 25–50-task suite that measures wrong-tool rate and tokens per task as first-class numbers.

### Cited Findings (anchors for each recommendation)
- 5–15 tools per server; one server, one job; delete unused; split by persona. [P — Phil Schmid](https://www.philschmid.de/mcp-best-practices)
- Consolidate chained operations into one tool (`schedule_event`, `search_logs`, `get_customer_context`; `track_latest_order`). [P — Anthropic](https://www.anthropic.com/engineering/writing-tools-for-agents); [P — Phil Schmid]
- 3–5 most-used tools always loaded; defer the rest; >10 tools or >10K tokens of definitions is the trigger for discovery. [P — Anthropic](https://www.anthropic.com/research/advanced-tool-use)
- 85% token reduction from tool search; 98.7% from code execution; 70–97% from compression. [E/V — Anthropic, Atlassian]
- Explicit handles, not implicit sessions; tool lists stable per credential. [S — MCP 2026-07-28]
- Four annotation hints with cautious defaults; one risk level per tool. [S — MCP annotations]; [P — Block]
- Descriptions: what/when/when-not/params/returns, 3–4+ sentences; examples for ambiguity (72%→90%). [S — Anthropic docs]; [E — Anthropic advanced tool use]
- Concise vs detailed ≈ 72 vs 206 tokens; 25K response cap in Claude Code; paginate with `has_more`/`next_offset`. [E/P — Anthropic]; [P — Phil Schmid]
- Grade outcomes not paths; 20–50 tasks; pass@k and pass^k; read transcripts; held-out set. [P — Anthropic evals]
- Wrong tool selection and wrong parameters are the most common failures. [P — Anthropic advanced tool use]

### Inferences — a concrete default surface

**Count and shape**
1. **8–12 tools** for one domain. Above 15, either split by persona/phase or add a guide/discovery tool; do not simply expose more.
2. Every tool is an **outcome**, not an endpoint. If two tools are always called in sequence, merge them or return the second call's data from the first.
3. One **guide/schema tool** (`get_guide(section?)`, `get_tool_schema(name)`) is always on; its payload is on demand and sectioned.
4. Keep a **3–5 tool core** that is always loaded (the verbs used in nearly every session) and make everything else discoverable.

**Naming**
5. `{domain}_{verb}_{object}`, verb-first and outcome-named: `compile_library`, `analyze_domain`, `lower_module`, `run_simulation` — not `parse_then_lower`.
6. Namespace by service/domain even inside one server, so the server composes with others. Verify prefix vs suffix on your own eval; the effect is model-dependent.
7. Reserve one verb per intent: `get_`/`list_`/`search_` for reads, `create_`/`update_`/`delete_` for writes, `run_`/`compile_` for computations. Do not use `get_` for something that mutates.

**Descriptions (template)**
8. Six lines: *outcome → use when → do not use when / does not return → inputs → returns → next step*. Cap at ~150–200 words; push length into the guide.
9. Name every parameter semantically (`workspace_id`, `library_id`, `section`, not `id`/`data`/`opts`); use enums/`Literal` for every closed set; state units and formats.
10. Add 1–3 `input_examples` only where the schema is ambiguous (nested objects, format-sensitive strings); skip them for obvious single-parameter tools.

**Responses**
11. Return `structuredContent` + a one-to-three-line text summary. Default concise; offer `response_format: concise|detailed` only when the detailed form carries IDs the next call needs.
12. Never return raw provider payloads. Drop URLs, MIME types, internal UUIDs, and hypermedia. Keep stable, human-meaningful identifiers and the fields needed for the next action.
13. Paginate with `limit` (default 20), `has_more`, `next_cursor`/`next_offset`, `total_count`. On truncation, say *what was cut* and *how to narrow*.
14. Error envelope: `{ code, message, hint, retryable, field? }`. Validation/business errors → `isError: true` with a corrective hint; unknown tool / malformed request / auth → protocol error (fail closed). Never return a stack trace.
15. Include the next affordance as data: the handle, the cursor, or an `allowed_actions` list.

**State and safety**
16. No implicit per-connection workspace. Take an explicit `workspace_id`/`session_id`, or return one from a `create_*` tool and document its lifetime.
17. `idempotentHint: true` + an explicit `idempotency_key` argument on every write that can be retried.
18. Annotate all four hints on every tool; enforce one risk level per tool; default new tools to `readOnlyHint: false, destructiveHint: true` until reviewed.
19. Provide a read-only mode and per-tool/per-group allowlists at the server level, mirroring GitHub's `--read-only` and `--toolsets`.

**Progressive disclosure budget**
20. Server instructions ≤300 tokens: domain, 3–5 core verbs, "start with `get_guide`", hard constraints.
21. Guide tool payload sectioned by heading; never dump the whole grammar. Return a table of contents plus the requested section.
22. Put long examples, golden corpora, and templates in resources, not in instructions. Do not rely on resources for critical guidance — host support is uneven.
23. Do not adopt code mode unless you can provide a sandbox; do not rely on host-side deferred loading; the guide tool is the portable baseline.

**Evaluation**
24. 25–50 tasks from real transcripts, plus 10–15 deliberately negative tasks (where the agent should *not* call a tool). Reference solution per task.
25. Report: selection accuracy@1, wrong-tool rate, calls/task, tokens/task, argument-validity rate, recovery rate after one injected error, pass@1 and pass^3. Three trials.
26. Held-out set; re-run on every description/schema change. Read 10 transcripts per iteration.
27. Compare against a **baseline surface** (the naive CRUD version) on the same suite — that is the only credible evidence that the redesign helped.

### Gaps
- The 8–12 figure is a synthesis, not a measured optimum; it sits between Phil Schmid's 5–15 and the ~20 ceiling implied by Speakeasy/Anthropic's discovery trigger.
- No source gives a token budget for server instructions or a guide payload; the ≤300-token and sectioned-guide recommendations are design judgement.
- There is no published evidence that a six-part description template outperforms a four-part one; the template is a checklist for completeness, not a proven intervention.
- The relationship between tool-surface design and *user* approval fatigue is unstudied in public sources.
