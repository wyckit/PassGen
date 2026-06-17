# PassGen

**A deterministic, auditable password generator with a knowledge graph for a brain — and no LLM.**

PassGen turns plain English (`"give me a 16 char password, 2 uppercase, no ambiguous"`)
into a validated password, and answers questions about how passwords and entropy work —
entirely from a compiled **TLM knowledge graph**. There is no neural model, no API call,
no cloud. The understanding lives in *data* (the TLM), the generation is a small
deterministic engine, and every result is verifiable.

```
 English ──► TLM-driven NLU ──► GenerateArgs ──► RandomStringTool ──► ConstraintSpec ──► StringGenerator ──► password
            (grammar built       (validated)                          (CSPRNG, uniform)
             from TLM vocab)
```

## Quick start

```powershell
.\passgen.ps1                                                       # interactive assistant
.\passgen.ps1 give me a 16 char password, 2 uppercase, no ambiguous # one-shot
.\passgen.ps1 what reduces password entropy                         # knowledge Q&A
```

```
password: ktQ_+EVq8?Zy7GbK
  spec:    len=16, allow: uppercase(min2)+lowercase+numeric+symbol, no-ambiguous
  entropy: 97.7 bits (very strong), charset 69, avg crack ~4.0e9 years
  cues:    exclude_ambiguous=true, uppercase.min=2, length.exact=16
  check:   OK -- satisfies the spec
```

It will also **tell you when a request doesn't make sense** — contradictory constraints
(`no letters, no digits, no symbols`), impossible minimums (`5 uppercase + 5 lowercase in 4 chars`),
or out-of-scope asks (custom charsets, passphrases, "count a whole letters group") get a clear
note instead of a silently-wrong string.

## How it works — "the TLM is the model"

The vocabulary and grammar are not hard-coded. They live in the TLM dataset:

- **`rs-char-classes`** holds class aliases (`digits`/`numbers`/`numeric` → numeric, …).
- **`rs-nl-vocabulary`** holds NL *cues* — each a `Trigger` phrase + a `Signal` (e.g.
  `q.min`, `target.length`, `only.<csv>`, `unsupported:<reason>`).
- **`TlmNlu`** (in `PassGen.Engine`) loads those cues and aliases at startup and builds its
  regex matchers *from* them. **Coverage grows by editing TLM data, not code.**

Generation is deterministic and auditable: CSPRNG by default (unbiased rejection sampling),
a seeded Mersenne-Twister only when you pass an explicit seed, flat-uniform fill, minimums
satisfied first, every result re-checked against the spec, and entropy reported as the exact
`log2(valid count)`.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design and a guide to reusing
this architecture for other domains.

## Layout

| Path | Purpose |
|------|---------|
| `PassGen.App/` | the assistant: REPL + one-shot, English → password + knowledge Q&A (no LLM) |
| `PassGen.Engine/` | deterministic engine — spec, alphabet, generator, validator, entropy, `RandomStringTool`, and `TlmNlu` (the TLM-driven resolver) |
| `PassGen.Tlm/` | self-contained TLM format library — compiler/decompiler, SHA-256 hasher, `.tlmz` envelope (**byte-compatible with live RSRM**) |
| `PassGen.Tlm.Cli/` | the `tlm` CLI: `author` / `compile` / `decompile` / `validate` / `verify` |
| `PassGen.Engine.Tests/` | xUnit suite — targeted cases + a coverage matrix generated from the TLM vocabulary |
| `PassGen.Engine.Demo/` | console smoke test |
| `dataset/` | the standalone RSRM TLM dataset (7 linked TLMs); see [`dataset/README.md`](dataset/README.md) |
| `docs/` | architecture + design docs |

## The TLM dataset

The whole random-string domain is encoded as a native **RSRM TLM knowledge graph**: 7 linked
TLMs (`rs-char-classes`, `rs-constraint-spec`, `rs-operations`, `rs-entropy`, `rs-generation`,
`rs-nl-vocabulary`, `rs-bundle`) compiled to `.tlmz` artifacts. The compiler is a byte-faithful
port of RSRM's model + hasher + envelope, so its checksums match RSRM's exactly — the same
`.tlmz` files load and validate inside live RSRM unchanged.

```powershell
.\build-dataset.ps1     # tlm author -> compile all -> decompile all -> verify round-trip
```

## Build / test / run

```bash
dotnet test PassGen.Engine.Tests           # full suite, net10.0
dotnet run  --project PassGen.Engine.Demo  # demo output + the tool schema
.\make-publish.ps1                         # assemble a self-contained publish/ bundle
```

> **Heads-up:** a running `passgen` (REPL or `passgen.exe`) locks the build DLLs. Exit any
> session before building or publishing.

## Conventions

net10.0 · nullable + implicit usings · file-scoped namespaces · `sealed record` + primary
constructors for contracts · xUnit. `.ps1` scripts are kept ASCII-only so Windows PowerShell
never mis-reads them.

## Distribution

`make-publish.ps1` produces `publish/` — a self-contained app (no .NET install needed) plus
**only** the compiled `.tlmz` data, a launcher, the architecture doc, and `protect-tlms.ps1`
for encrypting/decrypting the TLM data at rest. `publish/` is git-ignored; rebuild it on demand.

## License

See [`LICENSE`](LICENSE).
