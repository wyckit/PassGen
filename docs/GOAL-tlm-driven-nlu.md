# GOAL — Make the TLM graph the language model (deterministic, no neural LLM)

> **One line:** Turn PassGen's natural-language understanding into a deterministic
> resolver that reads the TLM knowledge graph as its "model." The TLM *is* the LLM.
> Growing coverage = growing the TLM data (concepts/relations/cues), never code and
> never an external/neural model.

This document is the north-star prompt for the work. Hand it to any session/agent.

---

## Why (the thesis)

RSRM's premise is that a **symbolic concept/relation graph (a TLM)** can stand in for a
neural language model for bounded domains: tokens activate concepts, relations carry
activation to intents/operations/spec-slots, and a deterministic interpreter turns that
into action. For the password domain this is enough — and it sidesteps the two failure
modes we already hit here:

- **Templated-corpus generalization gap** — a trained parser was 100% in-distribution but
  2/8 on hand-written paraphrases.
- **Self-generated-corpus capability cap** — a model trained on its own outputs can't beat
  the deterministic core.

A graph has no capacity ceiling and no training: coverage is exactly what's authored in the
TLM, and every addition is data, inspectable and testable.

## What we are building

A **deterministic, TLM-driven NLU resolver** inside the engine that converts free English
into the existing `GenerateArgs` (the same structured args the generator already consumes),
by *interpreting the compiled TLM bundle*. The regex `SpecParser` becomes a thin fallback.

```
English ──► tokenize ──► fire TLM cues whose triggers match (graph activation)
                              │
                              ▼  each cue carries a Signal; bind its number/class/chars from context
                         GenerateArgs ──► RandomStringTool.Execute ──► deterministic generator + entropy
```

The resolver knows *nothing* hard-coded about vocabulary. It learns:
- **class words** from `rs-char-classes` concept **Aliases** (e.g. uppercase ← upper, capital, caps),
- **phrasings → actions** from `rs-nl-vocabulary` **Cues** (`Trigger` words → `Signal`).

## Definition of done (acceptance criteria)

1. **Data-driven:** adding a new synonym or phrasing requires editing only the TLM
   `*.source.json` (via `author_sources.py`) + `tlm compile` — **zero C# changes** — and the
   resolver immediately understands it. Demonstrate by adding e.g. "mixed case" and
   "no look-alikes" purely in data.
2. **Coverage:** passes a hand-written paraphrase test set (below) — not generated phrasings.
3. **Per-class min/max/exact** work for every class via synonyms, including
   `at most 2 lowercase`, `2 to 4 uppercase`, `exactly 1 of each`, `only 2 digits` (= max).
4. **Include/exclude characters are case-preserved** (the current lowercasing bug is gone):
   `exclude 0 O 1 l I` and `include "@#"` produce exactly those chars.
5. **Determinism & safety unchanged:** the engine still generates with the CSPRNG and
   `SpecValidator.CheckString` still passes; the LLM-free guarantees hold.
6. **Fallback:** if the TLM bundle is missing/encrypted, the resolver degrades gracefully
   (regex fallback or a clear message) — no crash.

## Signal grammar (stored in `Cue.Signal`, executed by the resolver)

| Signal | Needs | Effect on GenerateArgs |
|--------|-------|------------------------|
| `length.exact` / `length.min` / `length.max` | a number | sets `length.*` |
| `class.min` / `class.max` / `class.exact` | number + class word | sets that class's min/max (exact = min=max) |
| `each.min` | a number | sets min on all four classes ("N of each") |
| `only.<csv>` | — | restrict allowed classes (e.g. `only.uppercase+lowercase` for "letters") |
| `deny.<class>` / `allow.<class>` | (class from signal or context) | toggle a class allowed flag |
| `no_ambiguous` | — | `exclude_ambiguous=true` |
| `exclude_chars` / `include_chars` | following literal chars (case-preserved) | append to exclude/include |

Number↔cue and class↔cue binding is by token proximity. Numbers are read by the firing
cue's role (never invented). "only N <class>" maps to `class.max` (a ceiling).

## Work plan

**Phase 1 — enrich the TLM (data):**
- `rs-char-classes`: add Aliases to each class concept (upper/uppercase/capital/caps; lower/lowercase;
  digit/digits/number/numbers/numeric/numerals; symbol/symbols/special/punctuation) and the
  group terms `letters`→{upper,lower}, `alphanumeric`→{upper,lower,numeric}.
- `rs-nl-vocabulary`: expand phrase concepts + cue Triggers with synonyms ("no more than",
  "up to", "no fewer than", "mixed case", "memorable", "readable", "at least one of each",
  "between N and M", "PIN", …) and give every cue a precise `Signal` from the grammar above.
- Recompile + `tlm verify` (still 7/7, byte-clean).

**Phase 2 — build the resolver (engine):**
- New `TlmNlu` component that loads the bundle (concepts/relations/cues), builds a trigger→signal
  index and a class-alias map, tokenizes input, fires cues, binds numbers/classes/chars, emits
  `GenerateArgs`. Case-preserving char capture. Deterministic and unit-tested.
- Wire `PassGen.App` to use `TlmNlu` first, regex `SpecParser` as fallback.

**Phase 3 — prove it:**
- Paraphrase test harness (xUnit) over the set below; all must pass.
- Demonstrate the data-only extension (add a synonym in the TLM, recompile, it works) with no
  code change.

## Paraphrase test set (must pass — hand-written, not generated)

```
make me a 16 character password with at least 2 uppercase and no ambiguous   -> len16, upper.min2, no-ambiguous
a 20-char password, at most 2 lowercase, 3+ digits                           -> len20, lower.max2, numeric.min3
only 2 digits, 12 long                                                       -> len12, numeric.max2
2 to 4 symbols, 16 chars                                                     -> len16, symbol.min2,max4
exactly one of each, 12 characters                                           -> len12, each class min1
letters only, mixed case, 24 long                                            -> len24, only upper+lower
a memorable pin                                                              -> numeric only, len ~4-6
exclude 0 O 1 l I, 16 chars                                                  -> len16, exclude those exact chars (case kept)
must contain @ and #, no symbols otherwise? (define)                         -> include @,#
no look-alikes, at least 16                                                   -> length.min16, no-ambiguous
```

## Constraints / non-goals

- **No neural model. No external/cloud/Ollama LLM.** The TLM graph is the model.
- Don't change the generator's CSPRNG behavior or the entropy semantics.
- Don't touch the `rsrm/` runtime or the external `sage-rsrm` references.
- Keep `.tlmz` byte-compatibility with RSRM (data changes only add concepts/relations/cues).

## Files in scope

- `dataset/tools/author_sources.py` (enrich rs-char-classes + rs-nl-vocabulary), then recompile.
- New `PassGen.App/TlmNlu.cs` (or `PassGen.Tlm/` if generic) — the resolver.
- `PassGen.App/Program.cs` — use resolver, regex fallback.
- `PassGen.App/SpecParser.cs` — demote to fallback (keep).
- `PassGen.Engine.Tests` (or a new test project) — paraphrase harness.

## The payoff

After this, teaching PassGen new language is: open `author_sources.py`, add a cue trigger or a
class alias, `./build-dataset.ps1`, done. The vocabulary lives in the knowledge graph — the
TLM is the model, and it grows by authoring, not training.
