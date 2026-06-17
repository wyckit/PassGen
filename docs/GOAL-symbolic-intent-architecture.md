# GOAL — Reposition PassGen as the reference implementation of Symbolic Intent Architecture (SIA)

> **One line:** PassGen looks like a password generator; it is really the smallest honest
> demo of a safer pattern for AI tools. Lead with the architecture, keep the engine unchanged.

**First action:** create a tasklist (TaskCreate) from the work plan and execute top-down. Store
key decisions in engram (`PassGen` namespace, `decision`/`architecture`) via a `sonnet` agent.

## Thesis (use verbatim)
- **Slogan:** "Don't execute language. Execute verified intent."
- **One sentence:** Language proposes intent; symbolic systems validate it; deterministic tools
  execute it; verifiers audit it.
- **Public name:** Symbolic Intent Architecture (SIA). TLM is our *implementation* of the
  symbolic layer — introduce SIA first, TLM second.
- **Pipeline:** prompt -> language resolver -> TLM graph -> typed intent -> validator -> tool
  -> verifier -> audit.

## Why now
Live 2026 concerns: tool-call hijacking, prompt injection/"promptware", MCP risk, agent
authorization, neuro-symbolic/local/explainable AI. SIA's answer: raw language never becomes
executable authority — it becomes inspectable structure validated before any tool runs
(fail-closed).

## Work plan (one task each)
1. **CLI 5-panel trace** (`--trace` / `/trace`): render Prompt -> Resolved Intent -> Validation
   -> Execution -> Verification, ASCII; doubles as the GIF script. Two scenarios: success, and
   fail-closed rejection ("4-char password with 10 uppercase" -> REJECTED, tool HALTED, verify
   BYPASSED). Reuse the existing surface (resolve -> validate-before-generate -> check); add
   xUnit for both.
2. **README repositioning:** open "PassGen looks like a password generator. It is really a small
   architecture demo." Add bad-pattern-vs-SIA diagram, the 5-panel pipeline, the fail-closed
   contrast, the slogan. Keep quick-start.
3. **docs/SYMBOLIC-INTENT-ARCHITECTURE.md:** the paradigm; analogy (LLM=interpreter, TLM=memory,
   validator=law, tool=machine, verifier=inspector); generalize to SQL / workflows / DevOps;
   where LLMs still fit (proposal & planning only).
4. **THREAT_MODEL.md:** STRIDE focus on tampering + info disclosure via prompt injection; show
   injected text is parsed only as *proposed* constraints and dies at validation; MCP
   typed-contract angle.
5. **SECURITY.md:** reporting policy + stance (CSPRNG default, seeded=insecure, fail-closed,
   no network/telemetry).
6. **docs/NLU-MATRIX.md:** 50+ phrasings normalizing to identical symbolic shapes (from the
   test vocabulary).
7. **Update docs/ARCHITECTURE.md** with SIA + the emergence note: "Emergence belongs in proposal
   and planning. Authority belongs in validation and execution." + the learning loop (phrase ->
   proposed concept -> tests -> review -> versioned TLM).
8. **Repo polish:** GitHub description -> SIA; Actions CI (`dotnet test`) + badge; tag v0.1.0;
   demo GIF from trace; bad-vs-good diagram image.
9. **Content drafts** (user posts): social script; article "Why AI Agents Need Symbolic
   Execution Rails"; Show HN + Reddit/LinkedIn posts.

## Acceptance criteria
- `passgen --trace "<req>"` shows all five panels; the impossible request is REJECTED, tool
  never invoked; tests cover both.
- README leads with SIA (diagram + slogan); passwords framed as the demo domain.
- THREAT_MODEL / SECURITY / SIA / NLU-MATRIX docs exist.
- CI green badge; v0.1.0 release; GIF in README. 327+ tests pass; 7/7 .tlmz verify.

## Constraints / non-goals
- No neural/cloud/Ollama LLM — the TLM graph stays the model. Keep `.tlmz` byte-compatible
  (data = additive only). Don't change CSPRNG/entropy; don't touch `rsrm/` or sage-rsrm.
- `.ps1` ASCII-only; close any running passgen before build/publish. HN/Reddit tone: humble.

## Payoff
The password generator is not the product; the architecture is. PassGen = the minimal,
inspectable reference for SIA: language becomes verified intent before anything executes.
