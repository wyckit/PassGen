# Short video / Shorts / X script (draft)

~45–60 seconds. Screen-record `passgen --trace` for the second half. Render the GIF with
`vhs docs/demo.tape` if you want a clean capture.

---

**Hook (on camera or big text):**
"I don't think AI agents should be trusted with tools directly. So I built this."

**Problem (fast, over b-roll of tool icons — email, code, DB, deploy):**
"We keep handing AI agents tools. Email. Code. Databases. Deployments. But language is messy,
and prompt injection is real. Right now the model reads some text and basically *decides the
action* from it. Whoever controls the text controls the action."

**Turn (cut to terminal):**
"So I built the smallest demo I could of a different way. It's a password generator — but that's
not the point."

**Demo 1 — success (show `--trace`):**
"I ask in plain English. Watch what happens. The language becomes *typed intent* — length,
numbers, symbols, no look-alikes. A validator checks it's even possible. Only *then* does the
generator run. Then it verifies the output against what I asked. Five steps. Language never
touched the generator directly."

**Demo 2 — the punchline (show the rejection):**
"Now watch me ask for something impossible — a 4-character password with 10 uppercase letters.
Rejected. At validation. The generator is never even called. It's not trying to be agreeable.
It's trying to be correct."

**Thesis (text on screen):**
"Don't execute language. Execute verified intent."

**Close:**
"The password generator is small. The architecture is the point. Link in bio."

---

### One-liner variants for X / threads
- "AI agents shouldn't let a convincing sentence trigger an action. Language should *propose*;
  a validator should *decide*; a deterministic tool should *execute*. Tiny demo, no LLM: [link]"
- "Built a password generator to make one point about AI safety: don't execute language,
  execute verified intent. `--trace` shows language becoming typed, validated intent before
  anything runs. [link]"
- "Prompt injection works because language *is* the authority. Flip it: language proposes,
  symbols validate, deterministic tools execute, verifiers audit. Reference impl (no LLM):
  [link]"
