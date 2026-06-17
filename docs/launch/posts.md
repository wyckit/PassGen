# Launch posts (drafts)

Humble tone, especially on HN/Reddit. The repo and `docs/SYMBOLIC-INTENT-ARCHITECTURE.md` do
the heavy lifting; these just open the door. Swap in the demo GIF once rendered
(`vhs docs/demo.tape`).

---

## Show HN

**Title:** Show HN: PassGen – a password generator that demos "symbolic intent" for AI tools

**Body:**

This is a small prototype, but I'm using it to explore a larger pattern I keep wanting for AI
agents: natural language should compile into inspectable, typed intent *before* any tool runs —
and the tool should only ever see validated intent, never raw language.

PassGen is the smallest honest demo I could build. You ask in plain English ("20 chars, 3
digits, 2 symbols, no confusing characters"); a deterministic resolver turns that into a typed
constraint spec; a validator decides if it's even satisfiable; only then does a deterministic
generator run; and the output is re-checked against the original intent. `passgen --trace` shows
all five steps. Ask for something impossible ("4-char password with 10 uppercase") and it's
rejected at validation — the generator is never called.

There's no LLM in it at all. The "understanding" lives in a compiled knowledge graph (a TLM), so
the boundary I care about — language proposes, but only validated intent executes — holds even
with zero neural components. The point isn't the passwords; it's the separation. I wrote up how
it generalizes (SQL, workflows, DevOps) and how it maps to prompt injection / MCP tool risk in
the repo.

Happy to be told why this is naive or where it breaks down.

Repo: https://github.com/wyckit/PassGen

---

## Reddit — r/LocalLLaMA

**Title:** Do agents need symbolic rails? I built a tiny, fully-local, no-LLM demo

I've been chewing on the idea that the risky part of agents isn't the model, it's that **raw
language gets to be the authority** — a convincing sentence (from a user, a web page, a tool
result) more or less directly causes an action.

So I built a deliberately tiny demo of the opposite: language gets resolved into typed,
inspectable intent, a validator gates it, and only then does a deterministic tool run. No LLM,
no network — the "model" is a compiled symbolic graph, which was kind of the point (the boundary
holds with zero neural parts). `passgen --trace` prints the whole pipeline; impossible requests
are rejected before the tool is ever called.

Curious what this sub thinks: is "LLM proposes, symbolic layer validates, deterministic tool
executes" a useful framing, or am I just describing function-calling with extra steps?

https://github.com/wyckit/PassGen

---

## Reddit — r/cybersecurity

**Title:** Prompt injection convinced me tool calls need a validation boundary, not just guardrails

The thing that bothers me about current agent setups: injected text (retrieval poisoning, a
hostile web page, another tool's output) can become *authority* because the model both
interprets the request and decides the action. Guardrail models are still language judging
language.

I built a small reference implementation of a different boundary: language is only ever a
*proposal*; it's compiled into a typed contract, and a deterministic validator is the gate. The
demo domain is password generation (small, security-sensitive, easy to verify), but the threat
model write-up is the part I'd actually want feedback on — it argues injected language gets
demoted to a proposal and dies at validation, and there's no second tool to pivot to.

Threat model: https://github.com/wyckit/PassGen/blob/main/THREAT_MODEL.md

Tear it apart — where does this boundary leak in a real agent with many tools and state?

---

## Reddit — r/programming

**Title:** Plain English → typed constraints → deterministic execution → verification

Small project exploring a clean separation: a natural-language request is resolved into a typed
spec, validated for satisfiability, executed by a deterministic tool, and the output is verified
against the original spec. There's a `--trace` mode that prints all five stages, including the
fail-closed path where an impossible request is rejected before anything runs.

No LLM — the language layer reads a compiled knowledge graph — which makes the whole thing
deterministic and testable (there's a coverage matrix of ~80 phrasings normalizing to identical
specs).

https://github.com/wyckit/PassGen

---

## LinkedIn

As AI agents get the power to act — call tools, run code, move money, deploy infra — we're
leaning on a shaky assumption: that natural language is a safe authority layer. It isn't. A
convincing sentence shouldn't be able to *directly* trigger an action.

I built a small open-source reference implementation of a safer pattern I'm calling **Symbolic
Intent Architecture**:

  language proposes → symbols validate → deterministic tools execute → verifiers audit

The demo is a password generator (small and easy to verify), but the architecture is the point.
Language is resolved into typed, inspectable intent; a validator gates it; only validated intent
reaches the tool; the result is checked against the original request. Impossible or out-of-policy
requests are refused *before* anything runs — fail-closed.

This maps directly onto today's agent-security concerns: prompt injection, tool-call hijacking,
MCP risk, agent authorization. The separation between *understanding* and *authority* is the
whole idea.

Code, threat model, and a write-up of how it generalizes (SQL, workflows, DevOps):
https://github.com/wyckit/PassGen

#AIagents #AIsecurity #neurosymbolic #softwarearchitecture
