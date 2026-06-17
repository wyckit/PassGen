# NLU Matrix — many phrasings, identical symbolic shapes

A central claim of [Symbolic Intent Architecture](SYMBOLIC-INTENT-ARCHITECTURE.md): the messy
variety of human language **collapses into a small set of inspectable symbolic shapes** before
anything executes. Below, radically different English phrasings normalize to the *same*
`GenerateArgs` / `ConstraintSpec`.

Every row here is also an assertion in `PassGen.Engine.Tests/NluCoverageTests.cs` (the
deterministic resolver, no LLM), so this table is executable truth, not aspiration. Run it with:

```bash
dotnet test PassGen.Engine.Tests --filter Coverage
```

Notation: `up`=uppercase `lo`=lowercase `nu`=numeric `sy`=symbol; `on/off` = class
allowed/denied; `min/max/exact` = per-class counts.

---

## 1. Length — one number, many ways to say it

All of these resolve to **`length.exact = 16`**:

| Phrasing | |
|----------|--|
| `16 characters` · `16 chars` · `16 long` | bare |
| `a 16 char password` · `a 16-character password` | embedded |
| `exactly 16 characters` | explicit |
| `sixteen characters` | spelled out |
| `length 16` · `length of 20` | noun-first |

Spelled-out and hyphenated numbers normalize too: `twenty-four characters` → `length.exact = 24`.

## 2. Bounds & ranges → `length.min` / `length.max`

| Phrasing | Symbolic shape |
|----------|----------------|
| `at least 12 characters` · `no fewer than 12` · `12 or more` · `12 min` | `length.min = 12` |
| `between 12 and 20 characters` · `12 to 20 chars` · `12-16 chars` · `from 8 to 16` | `length.min`+`length.max` |
| `at most 20 characters` · `max 20` · `20 max` | `length.exact = 20` (a pure ceiling collapses to a target) |

## 3. Per-class minimum → `class.min`

All resolve to **`uppercase.min = 2`** (with `length.exact = 16`):

`at least 2 uppercase` · `2 uppercase` · `at least 2 caps` · `2 capital letters` ·
`2 uppercase minimum` · `no fewer than 2 uppercase`

And **`numeric.min = 1`**: `at least one digit` · `must contain a digit` · `1+ digit`.
`exactly one of each` / `1 of each` → `min 1` on **all four** classes.

## 4. Per-class maximum → `class.max`

All resolve to **`symbol.max = 3`**:

`at most 3 symbols` · `no more than 3 symbols` · `up to 3 symbols` · `3 symbols maximum` ·
`3 or fewer symbols` · `3 max symbols`. Note `only 2 digits` → `numeric.max = 2` (a ceiling,
not a restriction).

## 5. Restriction ("only / letters / alphanumeric") → allowed-class set

| Phrasing(s) | Allowed classes |
|-------------|-----------------|
| `letters only` · `only letters` · `just letters` · `mixed case` | upper + lower |
| `alphanumeric` · `letters and numbers only` | upper + lower + numeric |
| `digits only` · `numbers only` | numeric |
| `uppercase only` · `all caps` | upper |
| `symbols only` | symbol |
| `uppercase and numbers only` | upper + numeric |

## 6. Denial ("no / without / exclude / drop") → class off

| Phrasing(s) | Effect |
|-------------|--------|
| `no symbols` · `no special characters` · `no punctuation` | `symbol = off` |
| `no digits` · `without numbers` | `numeric = off` |
| `no caps` · `exclude lowercase` | that class `off` |
| `drop symbols and digits` · `no symbols or digits` | both `off` |
| `no letters` | upper+lower `off` (numeric+symbol remain on) |

## 7. Ambiguous look-alikes → one boolean

Seven different phrasings all set **`exclude_ambiguous = true`** (drops `0 O o 1 l I`):

`no ambiguous` · `no ambiguous characters` · `unambiguous` · `readable` · `no look-alikes` ·
`no confusables` · `memorable`

## 8. Literal characters (case-preserved) → `exclude_chars` / `include_chars`

| Phrasing | Symbolic shape |
|----------|----------------|
| `exclude 0 O 1 l I` | exclude exactly `0 O 1 l I` (lowercase `o` *not* excluded — case kept) |
| `exclude "0Oo1lI"` | exclude all six |
| `must contain @ and #` | include `@ #` |
| `include "@#%"` | include `@ # %` |

## 9. Presets

`4 digit pin` → numeric only, `length 4`. `pin` (bare) → numeric only, `length 4`.
`6 digit pin` → numeric, `length 6`.

## 10. Compound sentences compose

| Phrasing | Symbolic shape |
|----------|----------------|
| `16 char password with at least 2 uppercase and no ambiguous` | `len 16` + `up.min 2` + ambiguous off |
| `min 4 uppercase, max 5 lowercase, no special characters, max length 17` | `len 17` + `up.min 4` + `lo.max 5` + `sy off` |
| `20 chars, 2 to 4 symbols, no look-alikes` | `len 20` + `sy.min 2` + `sy.max 4` + ambiguous off |

## 11. Out of scope → a graceful note, never a wrong answer

Some asks fall outside what the spec can express. Instead of guessing, the resolver attaches a
note (and still does what it can):

`must start with a capital` · `no repeated characters` · `a pronounceable password` ·
`a passphrase of 4 words` · `a 32 char hex string` · `base64` · `max 10 letters` (a count on a
whole multi-class group).

---

### Why this matters

Because the variety of language collapses to a handful of typed shapes **before** validation,
the surface an attacker (or an honest mistake) can reach is exactly those shapes — and every one
of them is checked by the validator. There is no phrasing that unlocks a capability the type
doesn't have. See [THREAT_MODEL.md](../THREAT_MODEL.md).
