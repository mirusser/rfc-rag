# Answer Generation with Optional Answering

The MCP server serves two deployment profiles: retrieval-only (no chat model configured) and answer-enabled (ChatModel set). When ChatModel is unset, `ask_rfc` is disabled and the server surface is limited to search and retrieval tools. When ChatModel is set, the `EvidenceBudgetChars` setting controls how much context is sent to the model for generation, bounding token spend and latency per query. This split lets the same codebase power both a lightweight retrieval server and a full Q&A server without different builds or feature flags.

## Considered Options

- **Always generate (error if no model)** — simpler implementation, no conditionals in the tool dispatch path, but forces every deployment to configure a chat model even when only retrieval tools are needed.
- **Optional answering with runtime config (current)** — ChatModel presence gates the generative tool at startup, retrieval-only deployments need no model config, and the code path is tested both ways. `EvidenceBudgetChars` provides an explicit generation budget that the Context Assembler enforces before the model call.
- **Lazy fallback (attempt generation, fall back silently on failure)** — unpredictable behavior: a deployment with no model would silently serve degraded responses instead of failing fast with a clear error.

Optional answering is chosen for deployment flexibility. Retrieval-only deployments require no model API key, no generation budget tuning, and no wasted startup validation against a model endpoint.
