# Known Quirks — rfcs_rag MCP Server

Documented 2026-06-15 after probing the server's 12 tools against 9,769 indexed RFCs.

## 1. Early RFCs: present in listing, "not indexed" on retrieval

**Symptom**: `list_indexed_rfcs` returns RFCs 1–12 (and likely others in the 1–20 range), but `get_rfc(1)` and `get_rfc(2)` respond with `"RFC N is not indexed."` Meanwhile, `get_rfc_metadata(1)` works fine (title, date "7 April 1969", author "Steve Crocker").

**Root cause**: The earliest RFCs were retroactively numbered from informal ARPANET working notes (1969). Their raw text files aren't included in the RAG embedding pipeline, though some metadata was parsed from whatever source was available.

**Impact**: ❌ Negligible. Affects <0.2 % of the corpus (~15 out of 9,769). Every substantive protocol RFC (791, 793, 1945, 2068, 2616, 7230, 8095, 9111, etc.) is fully indexed and retrievable.

---

## 2. Dirty metadata fields (date, category, authors, issn)

**Symptom**: Fields returned by `get_rfc_metadata` are frequently empty, malformed, or contain raw HTTP headers:

| RFC | `date` field | `category` field | `issn` field |
|---|---|---|---|
| 1 | `"7 April 1969"` (clean) | `""` | `""` |
| 2 | `""` | `""` | `""` |
| 793 | `""` | `""` | `""` |
| 1945 | `""` | `"Informational R. Fielding"` | `""` |
| 2068 | `"Wed, 15 Nov 1995 06:25:24 GMT\n   Last-modified: ...\n   Content-type: multipart/byteranges..."` | `"Standards Track J. Gettys"` | `""` |
| 7230 | `"Mon, 27 Jul 2009 12:28:53 GMT"` (looks OK) | `"Standards Track June 2014"` | `"2070-1721"` |
| 9111 | `""` | `"Standards Track J. Reschke, Ed."` | `"2070-1721 greenbytes\nJune 2022"` |
| 8677 | `""` | `"Informational D. Purkayastha ... November 2019"` | `"2070-1721 A. Rahman ..."` |

**Root cause**: The metadata parser reads the header block of RFC `.txt` files, which have no consistent format across 55 years. Some files include raw HTTP response headers (Date, Last-Modified, Content-Type) that the parser doesn't filter. Author names bleed into the `category` and `issn` fields because the RFC header layout isn't uniform.

**Key insight — does it matter?** See the impact map below. Metadata fields are **only** exposed through `get_rfc_metadata`. Every core RAG feature works off clean section text.

| Feature | Uses metadata (affected) | Uses section text (clean) |
|---|---|---|
| `search_rfc` | ❌ No | ✅ Yes |
| `get_rfc_section` | ❌ No | ✅ Yes |
| `get_rfc` / `get_rfc_full` | ❌ No | ✅ Yes |
| `get_rfc_toc` | ❌ No | ✅ Yes |
| `search_abnf` | ❌ No | ✅ Yes |
| `search_normative` | ❌ No | ✅ Yes |
| `find_updates_obsoletes` | ❌ No | ✅ Yes |
| `get_rfc_metadata` | ✅ Yes — **this is where it shows** | ❌ N/A |

**Impact**: ❌ None on search, retrieval, or analysis. Cosmetic only in the metadata endpoint. If a downstream depends on clean `date`/`category`/`authors` fields, the parser needs fixing.

---

## 3. Null `status` in some search results

**Symptom**: `search_rfc` returns a `status` field per result (e.g., `{"category": "current", "obsoletedBy": [], "updatedBy": []}`). For perhaps 20 % of RFCs, this field is `null`.

Examples of RFCs with null status from actual queries: 1945, 3875, 8677, 9111. Examples with populated status: 8095, 2896, 2594, 2935, 5080, 9110, 9213.

**Root cause**: The RFC status line (Standards Track / Informational / Experimental / BCP) is text-parsed from the RFC header. Some RFCs use non-standard formatting, are April Fools jokes (RFC 6919), or have unusual header layouts that defeat the parser. This is the same text-parser family as quirk #2.

**Impact**: ❌ None. Search ranking, section excerpts, URLs, and source paths are all correct regardless of whether `status` is populated. The field is presentation-only metadata appended to search results.

---

## Summary

| # | Quirk | Area | Affects core RAG? |
|---|---|---|---|
| 1 | Early RFCs (1–20) listed but not indexed | `get_rfc` | ❌ |
| 2 | Dirty metadata fields | `get_rfc_metadata` only | ❌ |
| 3 | Null status on some search results | `search_rfc` status field | ❌ |

All three are data-quality issues in the **RFC text-header parser** — a secondary layer. The embedding pipeline, vector search, section retrieval, ABNF grammar extraction, and normative keyword search are unaffected and production-grade.
