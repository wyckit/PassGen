# Architecture — PassGen / RSRM password system

A self-contained system that answers in natural language and generates passwords,
built on a **symbolic knowledge graph (TLMs)** plus a **deterministic engine**, with
the LLM kept *optional and external*. This document explains the pieces, the request
lifecycle, the file formats, the security model, and — most importantly — **how to
reuse this pattern for other programs**.

---

## 1. The core idea

Most "AI" features put a language model in the critical path. This system inverts that:

```
   ┌─────────────────────────────────────────────────────────────────┐
   │  Knowledge  =  compiled symbolic TLM graph   (auditable data)     │
   │  Capability =  deterministic engine          (auditable code)     │
   │  Language   =  thin host: rule parser OR an LLM  (swappable)       │
   └─────────────────────────────────────────────────────────────────┘
```

- **Knowledge** lives in *TLM* files — a graph of concepts and relations, compiled to
  a compact `.tlmz` artifact. It is data, versioned and checksummed, not weights.
- **Capability** is a plain deterministic program (here: the random-string generator).
  Given a structured spec it produces a verifiable result. No model, no randomness in
  the logic, fully testable.
- **Language** is a thin layer that turns user English into the structured spec. It can
  be a deterministic rule parser (what ships here, **no LLM**) or a real LLM later —
  both emit the *same* structured arguments, so nothing downstream changes.

The payoff: the answer is **explainable** (you can trace which concepts/relations fired),
**reproducible** (the engine is deterministic), and **safe** (a model can't hallucinate a
password — the engine generates and validates it).

---

## 2. Components

```
sage-rsrm-version/
├─ PassGen.Tlm/            (1) TLM format + compiler/decompiler/validator
├─ PassGen.Tlm.Cli/        (1) `tlm` CLI: compile / decompile / validate / verify
├─ PassGen.Engine/   (2) deterministic engine + RandomStringTool + entropy
├─ PassGen.App/           (3) the PassGen host: NL parser + KnowledgeBase + chat loop
├─ dataset/                  (4) the knowledge: 7 linked TLMs (source + compiled)
(the external sage-rsrm runtime can also load the same .tlmz — not vendored here)
```

### (1) PassGen.Tlm — the knowledge format

A **TLM** (Tokenized Language Model fragment) is a `TlmPackage`:

| Part | Meaning |
|------|---------|
| `Manifest.Metadata` | `TlmId`, `Role` (Foundation/Logic/…/Overlay), `Priority`, `Version`, `Checksum`, `HotSwapPolicy`, `StabilityScore` |
| `Manifest.Imports`  | dependency edges to other TLMs (a DAG) |
| `Concepts`          | nodes: `{ Id, Label, Category, Description, Aliases, Properties }` |
| `Relations`         | edges: `{ SourceId, TargetId, Type, Strength, … }` |
| `Dimensions / Policies / Cues / FitSignals / Generators` | optional structured extras |

A compiled `.tlmz` artifact is:

```
[16-byte envelope: "TLMZ" magic + uint16 major + uint16 minor + uint32 flags + uint32 reserved]
[Brotli-compressed UTF-8 compact JSON of the TlmPackage]
```

The `Checksum` is `SHA-256` of the compact JSON with the checksum field cleared. The
model classes here are **byte-faithful ports of RSRM's**, so the produced `.tlmz` is
*byte-identical* to what the real RSRM compiler emits — these artifacts load and validate
inside the live RSRM runtime unchanged (verified against real RSRM artifacts, including a
169k-concept dictionary).

`tlm` CLI verbs: `compile` (source→.tlmz), `decompile` (.tlmz→json), `validate`
(checksum + health/dangling checks), `verify` (lossless round-trip), `list`, `stats`.

### (2) PassGen.Engine — the deterministic capability

- `ConstraintSpec` — the contract: length (exact / min / max), per-class allow + min/max
  for the four classes (upper, lower, numeric, symbol), explicit include/exclude chars.
- `StringGenerator.Generate(spec, seed?)` — places class minimums first, fills the rest
  **uniformly over the union of allowed characters**, shuffles, and guarantees every
  minimum/maximum/exclusion. `seed == null` ⇒ cryptographic RNG; an int seed ⇒ reproducible.
- `SpecValidator` — rejects infeasible specs up front; `CheckString` verifies output.
- `Entropy` — exact `log2(number of distinct valid strings)` via big-integer combinatorics,
  with a closed-form fast path (`charset^length`) when no per-class limits are set.
- `RandomStringTool` — exposes the engine as an **LLM-callable function** (`SchemaJson` +
  `Execute(GenerateArgs)`), mirroring how an LLM tool-call would drive it.

### (3) PassGen.App — the thin language host

- `SpecParser` — **deterministic, rule-based** English → `GenerateArgs` (the exact same
  object an LLM would emit). Handles length, per-class min/max, "only/no" restrictions,
  "N of each", "no ambiguous", quoted include/exclude, number-words. **No LLM.**
- `KnowledgeBase` — loads the 7 `.tlmz`, answers questions by keyword scoring + 1-hop
  relation expansion over the graph (a deterministic analogue of spreading activation).
- `Program` — a REPL that routes each line to *generate* or *answer*, dispatches via
  `RandomStringTool.Execute`, and prints the password + spec + entropy + verification.

### (4) The dataset — 7 linked TLMs

`rs-char-classes` (alphabet + every character), `rs-constraint-spec` (the schema),
`rs-operations` (only/exclude/min/max/…), `rs-entropy` (strength model + reference points),
`rs-generation` (the algorithm + RNG policies), `rs-nl-vocabulary` (English → intent cues),
and `rs-bundle` (an Overlay index that `Imports` the other six). 178 concepts, 229 relations.

---

## 3. Request lifecycle

```
 "give me a 16 char password, 2 uppercase, no ambiguous"
        │
        ▼  SpecParser  (deterministic; an LLM could replace this and emit the same thing)
 GenerateArgs { length.exact=16, classes.uppercase.min=2, exclude_ambiguous=true }
        │
        ▼  RandomStringTool.Execute
 ConstraintSpec ──► SpecValidator.Validate ──► StringGenerator.Generate (CSPRNG)
        │                                              │
        │                                              ▼
        │                                    "ktQ_+EVq8?Zy7GbK"
        ▼                                              │
 Entropy.Bits / CrackTime  ◄───────────────────────────┘
        │
        ▼  SpecValidator.CheckString  (proves the output satisfies the spec)
 password + spec summary + "97.7 bits (very strong)" + check: OK
```

A **question** ("what reduces entropy?") instead routes to `KnowledgeBase.Recall`, which
scores concepts by keyword overlap, pulls their relations, and prints the matching graph
fragment from the `.tlmz` bundle — no generation, no model.

---

## 4. Security model

- **Default = secure.** Generation uses `RandomNumberGenerator.GetInt32` (OS CSPRNG),
  which is **unbiased** (rejection sampling — no modulo bias), with a Fisher-Yates shuffle.
- **`/seed` = insecure.** An explicit seed switches to a reproducible PRNG and is flagged
  `[INSECURE]` on every line — for tests only.
- **Strength is measured, not guessed.** Reported bits = exact `log2` of the output space.
  Under 80 bits is flagged `[WEAK]` with advice; ≥ 80 bits reads "effectively uncrackable".
- **Exposure controls.** `/copy` (clipboard), `/mask` (hide on screen), `/clear` (wipe).
- **At-rest encryption.** `protect-tlms.ps1` encrypts the `.tlmz` with AES-256-CBC +
  HMAC-SHA256 (encrypt-then-MAC), key derived by PBKDF2-SHA256 (200k iters) from a
  passphrase + random per-file salt. Wrong passphrase or tampering is rejected before decrypt.

---

## 5. How to reuse this for other programs

The reusable pattern is **"compiled symbolic knowledge + deterministic tool + thin,
LLM-optional language host."** Three independent reuse paths:

### A. Reuse the TLM format as a knowledge store for any app
`PassGen.Tlm` is a standalone, dependency-free compiler/decompiler for a concept/relation
graph. Author `*.source.json` (lists of concepts + relations), `tlm compile` them to
`.tlmz`, and load them anywhere with `TlmCompiler.Deserialize`. Because the bytes match
RSRM exactly, the same artifacts also drop into the full RSRM cognitive runtime. Use it for
any domain you want as an *auditable, versioned, checksummed* knowledge bundle instead of a
model or a database.

```
author concepts/relations → tlm compile → ship .tlmz → load + query (keyword/graph walk)
```

### B. Turn any deterministic capability into an LLM-callable tool
`RandomStringTool` is the template: define a JSON **function schema** (`SchemaJson`) plus a
typed `Execute(args)` that runs pure, verifiable logic and returns a structured result.
- An LLM fills the arguments from the user's words (function calling), **or**
- a deterministic parser fills them (what ships here) — identical contract.

Swap the random-string engine for *your* capability (a calculator, a query builder, a config
generator, a code transformer). The host, validation, and "the model never produces the
answer directly — it only fills arguments" guarantee all carry over.

### C. The thin host pattern
`PassGen.App` shows the whole loop in ~200 lines: intent routing (do vs. ask), a rule
parser standing in for an LLM, a knowledge lookup over TLMs, and a chat REPL. To build a new
assistant: keep the host shape, replace `SpecParser` (your NL→args) and the tool, and point
`KnowledgeBase` at your `.tlmz` bundle. Add an LLM later by having it emit the same `args`.

### Checklist for a new domain
1. **Model the knowledge** as concepts + relations → `*.source.json` TLMs; `tlm compile`.
2. **Write the deterministic engine** for the capability + a `SpecValidator`-style check.
3. **Expose it as a tool** (function schema + `Execute`).
4. **Write a host**: a rule parser (or LLM) → tool args; a `KnowledgeBase` over your TLMs.
5. **Ship** with `make-publish.ps1` (app + only the `.tlmz`); optionally `protect-tlms.ps1`.

---

## 6. Command reference

```
# build + verify the dataset
./build-dataset.ps1

# TLM tooling
dotnet run --project PassGen.Tlm.Cli -- compile|decompile|validate|verify|list|stats --root dataset

# run the assistant (interactive chat, no LLM)
./passgen.ps1                         # waits for each line
./passgen.ps1 <english request>       # one-shot
./passgen.ps1 -Build                  # force rebuild first

# distributable
./make-publish.ps1                 # -> publish/ (app + compiled .tlmz + docs + crypto)
./protect-tlms.ps1 -Mode encrypt|decrypt
```

> Build note: a running `passgen`/`run` session locks the built DLLs; rebuilds will fail to
> replace them until you `/exit`. Then `./passgen.ps1 -Build`.
