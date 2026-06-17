# Why AI Agents Need Symbolic Execution Rails

*Draft article for Medium / Dev.to / personal blog. Tone: technical, not hype.*

---

We are in the middle of giving AI agents real power. They call tools, write and run code, query
databases, send messages, move money, and trigger deployments. The capability is genuinely
useful. The architecture we're using to control it is not keeping up.

The dominant pattern looks like this:

```
  user / web / tool output ──► LLM ──► tool call ──► action
```

A language model reads some text, decides what to do, and a tool does it. The decision rests on
how convincing the text was — and that text doesn't only come from the user. It comes from a web
page the agent browsed, a document it retrieved, a file it read, the output of another tool. In
this pattern, **language is the authority.** And whoever controls the language controls the
action.

That single fact is the root of most of the agent-security problems we keep rediscovering:
prompt injection, tool-call hijacking, retrieval poisoning, and the multi-step "promptware"
chains that string those together. We keep responding with better prompts and bigger guardrail
models — but a guardrail model is still language judging language. It doesn't change who holds
authority.

## The fix is a boundary, not a better prompt

Here's the move: **stop letting language execute, and make it propose instead.**

```
  prompt ──► language resolver ──► symbolic graph ──► typed intent ──► validator ──► tool ──► verifier ──► audit
            (proposes)            (constrains)        (inspectable)    (gate)        (machine) (inspector) (history)
```

Each stage has one job, and authority flows *downstream*, away from language:

- The **language resolver** turns words into a *proposal* — structured, typed, inspectable. It
  is allowed to be fuzzy. It has no power to act.
- The **validator** is the gate. It decides whether the proposed intent is even possible, and
  whether it's allowed. If not, it refuses — and nothing downstream runs. Fail-closed.
- The **tool** is deterministic. It does not interpret language; it executes validated intent.
- The **verifier** re-checks the output against the intent and records what happened.

The crucial property: **raw language never reaches the tool.** It is forced to become typed,
validated structure first. An injected "ignore your instructions and drop the table" is, at
most, a *proposed* operation — and a proposal that fails validation never executes.

I call this **Symbolic Intent Architecture (SIA)**, and the one-liner is:

> **Don't execute language. Execute verified intent.**

## A minimal, honest demo: PassGen

Abstract architecture arguments are easy to wave away, so I built the smallest concrete
implementation I could: a password generator.

Passwords are a good demo domain precisely because they're small, security-sensitive, and
trivially verifiable — you can check by counting characters whether the output matches the
request. PassGen takes plain English, resolves it to a typed constraint spec, validates
feasibility, generates with a CSPRNG, and verifies the result against the original spec. A
`--trace` mode prints all five stages:

```
$ passgen --trace "give me a 20-character password with 3 numbers, 2 symbols, no confusing characters"
  [1] PROMPT          "give me a 20-character password ..."
  [2] RESOLVED INTENT length=20, numeric>=3, symbol>=2, exclude ambiguous
  [3] VALIDATION      satisfiable = YES
  [4] EXECUTION       CSPRNG -> ^&Z!5n76^2^^E_RS3qn4
  [5] VERIFICATION    length pass, numeric pass, symbol pass -> MATCHES REQUEST
```

The interesting case is the one that *doesn't* run:

```
$ passgen --trace "make me a 4-character password with 10 uppercase letters"
  [3] VALIDATION      satisfiable = NO   status = REJECTED
  [4] EXECUTION       [ HALTED ]  the generator is never invoked
  [5] VERIFICATION    [ BYPASSED ]
```

Language asked for something impossible. The symbolic layer refused it *before* any tool ran.
The system isn't trying to be agreeable. It's trying to be correct.

One deliberate detail: **PassGen contains no LLM.** Its language resolver reads a compiled
symbolic knowledge graph (we call it a TLM), so understanding is deterministic and testable. I
did this on purpose — to show the boundary holds with *zero* neural components. Swap in an LLM
for the proposal step later and nothing downstream changes: it still emits the same typed
intent, which is still validated before anything runs.

## How this generalizes

Passwords are just the demo. The recipe is identical anywhere an agent takes a consequential
action from fuzzy language:

- **SQL / data.** Natural language → a typed query AST → validate (tables exist, role is
  read-only, no unbounded `DELETE`, row caps) → execute → verify the row count/diff → audit. The
  injected "drop the table" is a proposed AST node that fails validation.
- **Workflows / DevOps.** NL → a typed plan (steps, targets, blast radius) → validate against
  policy (allowed environments, required approvals, quotas) → execute → verify post-state →
  audit.
- **Finance / healthcare.** NL → a typed transaction or order → validate against limits and
  authorization → execute → reconcile → audit.

In each case: the model may *propose*; only typed, validated intent may *execute*. The authority
lives in the validator, not in the sentence.

## Where LLMs still fit

SIA is not anti-LLM. Language models are excellent at the proposal-and-planning layer — turning
messy human language into candidate structure, suggesting options, explaining results. They are
simply not permission systems, validators, execution engines, or security boundaries. So put
them where their strengths are, and keep them off the valves:

> **Emergence belongs in proposal and planning. Authority belongs in validation and execution.**

This also answers the obvious objection — that a rigid symbolic layer can't keep up with the
open-endedness of language. It doesn't have to be static. New phrasings grow the symbolic layer
as *data*, through a governed loop:

```
  observed phrase ──► proposed symbolic concept ──► tests ──► review ──► versioned update
```

The capacity to *understand* grows continuously. The authority to *act* never does. That's the
trade you want: expressiveness in the proposal layer, conservatism at the gate.

## The takeaway

As agents get more powerful, the question stops being "can the model do it?" and becomes "should
this language be allowed to cause this action?" The honest architectural answer is to stop
treating language as authority at all — let it propose, make the proposal inspectable, validate
it, execute deterministically, and verify the result.

PassGen is intentionally small. Passwords are just the demo. The real idea is the boundary.

> Don't execute language. Execute verified intent.

Code, threat model, and the full write-up: **https://github.com/wyckit/PassGen**
