EXECUTE: TLM-driven NLU for PassGen — run to completion, do not stop until DONE.

Project: C:\Software\research\randomstringllm\PassGen (.NET 10 + PowerShell, Windows).
FIRST read docs/GOAL-tlm-driven-nlu.md in full — it is the authoritative spec. This is the execution mandate.

OBJECTIVE: Make the TLM knowledge graph the language model. Build a deterministic, TLM-driven NLU resolver in the engine that turns free English into the existing GenerateArgs by interpreting the compiled TLM bundle. NO neural model, NO external/Ollama/cloud LLM. Coverage grows by editing TLM DATA, not code.

DO NOT STOP until every DONE criterion passes, shown with real command output. Work autonomously through all phases; fix what breaks; re-run until green. If blocked, diagnose and resolve — never hand back partial work.

PHASES (detail in the doc):
1) Enrich TLM data in dataset/tools/author_sources.py: add class-word Aliases to rs-char-classes (uppercase<-upper/capital/caps; lowercase<-lower; numeric<-digit/digits/number/numbers/numeric; symbol<-special/punctuation; plus group terms letters->upper+lower, alphanumeric->upper+lower+numeric). Expand rs-nl-vocabulary cue Triggers with synonyms and give EVERY cue a precise Signal. Run `python dataset/tools/author_sources.py` then `./build-dataset.ps1` (must stay 7/7 verify, byte-clean).
2) Build PassGen.App/TlmNlu.cs: load the compiled bundle via PassGen.Tlm.TlmCompiler; build trigger->signal and class-alias indexes; tokenize input; fire cues whose triggers match; bind the number/class/chars each cue needs by token proximity (chars CASE-PRESERVED from original text); emit GenerateArgs. Wire Program.cs to use TlmNlu first, regex SpecParser as fallback.
3) Add an xUnit paraphrase test (the hand-written cases in the doc); make all pass.

SIGNAL GRAMMAR (Cue.Signal): length.exact|length.min|length.max; class.min|class.max|class.exact (+ a class word); each.min; only.<csv>; deny.<class>|allow.<class>; no_ambiguous; exclude_chars|include_chars. Numbers are read by the firing cue's role, never invented. "only N <class>" => class.max.

DONE = ALL true, demonstrated with output:
- DATA-ONLY EXTENSION PROVEN: add a NEW synonym (e.g. "mixed case", "no look-alikes") ONLY in author_sources.py, recompile, and the resolver understands it with ZERO C# change.
- Paraphrase test set passes (hand-written, not generated).
- Per-class min/max/exact + ranges: "at most 2 lowercase", "2 to 4 uppercase", "exactly 1 of each", "only 2 digits" (=max) all correct.
- Include/exclude CASE-PRESERVED: "exclude 0 O 1 l I" keeps O and I; 'include "@#"' adds @ and #.
- Generator still uses the CSPRNG; SpecValidator.CheckString passes on outputs; no LLM anywhere.
- Graceful fallback if the bundle is missing/encrypted (no crash).

VERIFY WITH:
- ./build-dataset.ps1                                   (expect 7/7 verify)
- dotnet test PassGen.Engine.Tests/PassGen.Engine.Tests.csproj   (all pass)
- dotnet PassGen.App/bin/Debug/net10.0/passgen.dll "<phrase>"    (spot-check each paraphrase: password + spec + check OK)

CONSTRAINTS / NON-GOALS: no neural or external LLM of any kind; do not change CSPRNG or entropy semantics; do not touch the rsrm/ runtime or external sage-rsrm references; keep .tlmz byte-compatible with RSRM (only ADD concepts/relations/cues/aliases).

GOTCHAS: a running passgen/run REPL locks the Debug DLLs — builds fail "locked by dotnet.exe <pid>"; ensure no such process before building, or build to a temp `-o` dir. Windows PowerShell 5.1 mangles non-ASCII in .ps1 — keep scripts ASCII.

Report progress at each phase; finish ONLY when every DONE item is satisfied.
