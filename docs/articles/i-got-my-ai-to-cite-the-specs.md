# RFC RAG: I got my AI to cite the specs instead of inventing them

*A local search server so an AI agent quotes real RFC sections, plus the April Fools RFC that broke my parser.*

Ask a coding assistant what RFC 9110 says about the `Retry-After` header and you get a confident paragraph. Sometimes it's even right. Other times it makes up a section number and moves on.

RFC RAG is my fix. It's a local MCP server (Model Context Protocol, the thing that lets an AI client call your tools) that indexes 9,769 RFCs and hands your agent the exact section it asked for, with a citation instead of a vibe.

## The job

You run it next to your editor. The agent calls a tool like `search_rfc` or `ask_rfc`, and the server searches the corpus two ways at once: pgvector for meaning, PostgreSQL full-text for exact keywords. It fuses both rankings with reciprocal rank fusion and returns the matching paragraph instead of the 200-page document around it.

Twelve tools ship with it. A few I like:

- `search_normative` finds the MUST / SHOULD / MAY language that defines a requirement. It matches uppercase only, because RFC 8174 says the lowercase versions carry no weight.
- `search_abnf` pulls the grammar blocks out of a spec.
- `find_updates_obsoletes` walks the graph of which RFC replaced which.
- `ask_rfc` answers a question in English, then a citation verifier checks every claim against the evidence it retrieved. If a claim has nothing backing it, the verifier flags it.

## The modern bits

It runs on .NET 10 and the C# Model Context Protocol SDK, so it drops into Claude or any MCP client with one config line. Embeddings go through Microsoft.Extensions.AI. Storage is PostgreSQL with pgvector, and Docker Compose wires it up with a sidecar, so setup is one `compose up`.

Indexing is incremental. Every section gets a SHA256, so the first run embeds the corpus and every run after that finishes in seconds. The embedding pipeline emits OpenTelemetry metrics. Golden-eval gates score citation precision and recall, and a hostile-injection fixture throws prompt injection at the answer pipeline to see if it holds.

## The duct-tape parts

My metadata parser reads the header block of each `.txt` file. Across 55 years with zero formatting consensus, that header block lies. RFC 2068 stuffs a raw HTTP `Date:` header into the date field, and about one in five results come back with a null status. RFC 6919, an April Fools joke, never stood a chance. None of it touches search, though. The mess stays in one endpoint, `get_rfc_metadata`, while every retrieval feature reads the clean section text.

## Try it

It's on GitHub under Apache-2.0.  
[rfc-rag-repo](https://github.com/mirusser/rfc-rag)  
Clone it, point it at an embedding key, run `compose up`, then add one line to your MCP client config. Ask your agent something that needs a real citation and watch it hand back a section number you can verify.

It's a research tool I run on my own machine, not a hosted service. RFCs are all it knows, and that's the point.
