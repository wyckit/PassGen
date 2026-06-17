# Security Policy

## Reporting a vulnerability

Please report security issues privately rather than opening a public issue:

- Use **GitHub Security Advisories** ("Report a vulnerability" on the repository's Security tab), or
- email the maintainer (see the GitHub profile for `wyckit`).

Include a description, reproduction steps, and the impact you observed. You'll get an
acknowledgement, and a fix or mitigation will be coordinated before public disclosure.

## Design stance (security by construction)

PassGen is a reference implementation of **Symbolic Intent Architecture**; several properties
are structural rather than configurable:

- **Fail-closed.** Impossible or contradictory requests are rejected at validation
  (`SpecValidator.Validate`) **before** any string is generated. The tool never produces a
  "best effort" wrong answer. See [THREAT_MODEL.md](THREAT_MODEL.md).
- **Language is not authority.** Natural language is resolved into a typed `GenerateArgs`
  contract and validated before execution; free text never reaches the generator.
- **Cryptographic randomness by default.** Real passwords use the OS CSPRNG
  (.NET `RandomNumberGenerator`) with unbiased rejection sampling and a flat-uniform fill.
- **Seeded output is explicitly insecure.** Passing a seed switches to a reproducible,
  non-cryptographic generator for tests/audit only. The CLI labels every seeded result
  `[INSECURE]` and refuses to let it masquerade as a real secret.
- **No network, no telemetry.** PassGen makes no outbound connections and logs no generated
  values. Nothing leaves the machine.
- **Secret hygiene in the CLI.** `/mask` keeps generated values off-screen and copies them to
  the clipboard instead; `/clear` wipes the screen scrollback. The knowledge base and generator
  never persist secrets to disk.
- **Verified output.** Every generated value is re-checked against the resolved spec
  (`SpecValidator.CheckString`) and its entropy reported, so a weak or non-conforming result is
  surfaced, never hidden.

## Scope

In scope: the engine, resolver, validator, TLM format library, and CLI in this repository.

Out of scope: host/OS compromise (clipboard scraping, memory inspection), the trust of the
underlying OS CSPRNG, and policy/domain choices (the "safe symbol" set, the ambiguous-character
list) which are documented design decisions in `Alphabet`.

## Supported versions

This is a young project; security fixes target the latest `main` and the most recent tagged
release.
