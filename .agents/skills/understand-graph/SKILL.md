---
name: understand-graph
description: Use the Understand-Anything knowledge graph (.understand-anything/knowledge-graph.json) for architecture orientation, impact analysis, and semantic search before code tasks. Use when starting work in an unfamiliar area, estimating change blast radius, asking "what calls X / what does X depend on", or orienting to a layer before editing.
---

# Understand Graph

Complement to CodeGraph: use this for **semantic context and architecture** (why things exist, what layer something belongs to, cross-cutting relationships). Use CodeGraph for **symbol navigation** (exact call chains, live index, go-to-definition).

## When to load this skill

- Starting work in an area you haven't touched before
- Need to estimate blast radius before a change
- Want to know which layer owns a concern
- Asked "what handles X?" and grepping source would take several reads

## Workflows

### Orient before editing

```bash
# Which layer owns the file you're about to change?
jq '.layers[] | select(.nodeIds[] | contains("file:src/RfcRag/Search/SearchService.cs")) | {name, description}' \
  .understand-anything/knowledge-graph.json

# What does a file depend on?
jq '[.edges[] | select(.source == "file:src/RfcRag/Search/SearchService.cs" and .type == "depends_on") | .target]' \
  .understand-anything/knowledge-graph.json
```

### Impact analysis before committing

Run `/understand-diff` after staging your changes. It traverses the graph from your changed nodes and reports which other layers are affected — faster than reading test output to discover surprise breakage.

### Semantic question without grepping

Use `/understand-chat "<question>"` for natural-language queries:
- *"How does hybrid search work?"*
- *"What calls AnswerGenerator?"*
- *"Which files handle prompt injection?"*

Returns a graph-traversal answer in seconds. Reserve `codegraph_callers` / `codegraph_callees` for exact call-chain verification once you know where to look.

### Layer summary

```bash
jq '.layers[] | {name, fileCount: (.nodeIds | length), description}' \
  .understand-anything/knowledge-graph.json
```

The 12 layers in this repo: MCP Tools, Search, Indexing and Parsing, Answering, Data, Application Core, Tests, Infrastructure, CI/CD, Documentation, Project Configuration, Agent Skills.

### Tour for complete orientation

```bash
jq '.tour[] | {order, title, description}' .understand-anything/knowledge-graph.json
```

15 steps ordered by dependency — use when you need a full mental model, not just a targeted lookup.

## When NOT to use this

- **Exact symbol location** → use `codegraph_search`
- **Live call chain through new uncommitted code** → use `codegraph_callers` / `codegraph_callees`
- **Graph is stale** (last commit hash differs from `meta.json`) → run `/understand` to rebuild
