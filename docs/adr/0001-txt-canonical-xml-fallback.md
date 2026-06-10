# TXT is the canonical RFC source; XML is fallback-only

Modern RFCs are authoritatively published as RFC XML v3, so XML-first parsing would be the expected choice — we deliberately invert it. When both `rfcN.txt` and `rfcN.xml` exist in the mirror, only the `.txt` is parsed and indexed; `.xml` is used solely for RFC numbers that have no `.txt`. The TXT parser is this project's deep parser (sections, ABNF blocks, normative occurrences), while the XML parser yields only sections with weaker metadata, and letting both run produced a nondeterministic double-index race for duplicated numbers.

## Considered Options

- **XML as primary** — rejected: the XML parser extracts no ABNF blocks and no normative occurrences today, so XML winning degrades the index.
- **Metadata enrichment** (body from TXT, structured metadata overlaid from XML) — rejected for now: two parsers per RFC, precedence rules for conflicting values, and a wider resolver seam. Revisit only if TXT metadata extraction proves insufficient.
