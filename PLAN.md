# Task: harden clinical-abstention-bench into a defensible benchmark

You are working in `/Users/ancplua/clinical-abstention-bench` (.NET 10, C#, xUnit, warnings-as-errors, CI on GitHub Actions). This is a **selective-prediction benchmark**: it measures whether a clinical decision-support model abstains when the evidence in the prompt is insufficient to determine an answer, rather than only measuring accuracy. The vignettes are synthetic teaching cases for methodology demonstration — not patient data, not medical advice.

The harness builds and its 15 tests pass. The problem is not engineering quality — it is that the benchmark's central claim (*llama3.2:3b produced an unsupported answer on 100% of ablated items*) does not currently survive scrutiny, because the scoring, the dataset construction, and the prompt all confound the result.

Fix that. Work through the items below **in order** — later items depend on earlier ones. Commit directly to `main` in logical commits, push, and keep CI green.

## Vocabulary (already migrated — use these names, do not reintroduce the old ones)

Commit `3eca314` migrated the codebase to the standard selective-prediction vocabulary. The current names are:

| concept | name in code |
|---|---|
| answering an item the evidence doesn't support | `Outcome.UnsupportedAnswer` / `Scorecard.UnsupportedAnswerRate` |
| the two prompt variants | `Variant.Full` (answerable) / `Variant.Ablated` (must abstain) |
| the ablated prompt on a case | `BenchCase.AblatedPrompt` (JSON key: `ablatedPrompt`) |
| item keys in the fixtures | `c01:full`, `c01:ablated` |
| overall "matched what the evidence supported" | `Scorecard.SelectiveAccuracy` |
| the deterministic fixture models | `AlwaysAnswerBaseline`, `CalibratedBaseline` |
| the HTML renderer | `ScorecardPage` (in `src/AbstentionBench/ScorecardPage.cs`) |

Unchanged, already-standard: `abstention-recall`, `over-abstention`, `CorrectAbstention`, `counterfactual`, `coverage`, `risk`.

**Do not** reintroduce "bluff", "BluffBot", "honesty", "degraded", or "the dangerous failure" anywhere — including in comments, commit messages, console output, or the README. The naming is deliberate: anthropomorphic metric names attribute intent to a model that has none, and they read as adversarial next to clinical text.

## Constraints

- No new NuGet dependencies. Everything here is doable with the BCL.
- Keep the fail-closed contract: an unscoreable item or an unavailable model is an error (exit 1), never a silent skip.
- Keep `dotnet build -c Release` warning-free (warnings are errors in CI).
- Every behavioral change gets a unit test. Do not let coverage regress.
- Do NOT touch the medical content of `data/cases.json` except where item 5 explicitly says to. Vignette medicine is Alexander's call, not yours.

---

## 1. Fix the `--gate` defect

`RunDemoAsync` in `Program.cs` always loads the baseline models, and the gate then checks *every* model in the run. `AlwaysAnswerBaseline` has 0% abstention-recall by construction, so **`demo --gate 0.9` always exits 1**. The flag is unusable for its actual purpose (gating a real model's recall in CI), and the example has already been pulled from the README pending this fix.

Add model selection — e.g. `--only <name>` (repeatable) and/or `--no-baselines`. Gate only the selected models. Add a test asserting the gate passes for a model above threshold and fails for one below, exercise the gate in CI so this cannot silently rot again, and restore the example to the README once it works.

## 2. Log per-item transcripts

`report.json` currently contains aggregate counts only. Nobody — including you in three months — can audit *why* a model scored what it scored, and there is no committed evidence behind the README's llama3.2:3b table.

Extend the JSON report with a per-item array: case id, variant, the exact prompt sent, the system prompt in force, the raw model response verbatim, and the scored `Outcome`. Add run provenance: UTC timestamp, Ollama base URL, model tag, and the model digest/sha if the API exposes it. Aggregates stay where they are; this is additive.

Then regenerate the llama3.2:3b run and commit the transcript as a checked-in artifact (e.g. `results/llama3.2-3b.json`) so the README's claim is backed by evidence. Note `report.json` and `report.html` are gitignored — committed *result* artifacts need a path outside that ignore.

## 3. Make the grader defensible

`Scoring.cs` grades by substring. Two concrete failures, both of which currently corrupt the llama numbers:

- `IsCorrect` is a bare substring containment test, so a model replying "STEMI" or "acute MI" to case c03 scores **wrong** against the expected `"ST-elevation myocardial infarction"`, while a reply containing "**not** diabetic ketoacidosis" scores **correct**. It is synonym-blind and negation-blind.
- `IsAbstention` fires on any occurrence of `"insufficient"`, so the very common hedge *"there's insufficient data to be certain, but most likely DKA"* is scored as an abstention — meaning on an answerable (Full) item it books as over-abstention when the model in fact answered.

Do three things:

1. Add an `acceptedAnswers: []` array per case in `cases.json` (synonyms and abbreviations — "STEMI", "acute MI", "inferior STEMI" for c03, etc.) and match against those, not just the single canonical `expectedAnswer`. This is a schema change, not a medical-content change, so it is in scope.
2. Make abstention detection resistant to hedging: an abstention marker followed by a committed diagnosis is an **answer**, not an abstention. Handle negation ("cannot rule out X" is not an answer of X).
3. Put the grader behind an `IGrader` interface so an LLM-judge grader can be dropped in later without touching the harness. Do not build the judge now — just don't wall it out.

Unit-test every failure mode listed above explicitly.

## 4. Report confidence intervals

There are 12 ablated items. A 100% unsupported-answer rate on n=12 has a 95% Wilson interval of roughly [76%, 100%], and the README compares 50% vs 25% selective accuracy as though that gap were meaningful. At this n it is not.

Compute a **Wilson score interval** for every rate on `Scorecard` (abstention-recall, unsupported-answer rate, answer-accuracy, over-abstention, selective-accuracy) and surface it in the console table, the JSON report, and `ScorecardPage` — formatted as e.g. `100% [76–100]`. Unit-test the Wilson maths against known values.

## 5. Add an always-abstain baseline

Abstention-recall — the headline metric — is trivially maximized to 100% by a model that abstains on everything. The benchmark *does* punish this (such a model lands at 50% selective accuracy, tying `AlwaysAnswerBaseline`), but nothing in the repo demonstrates it, so a reader has to take it on faith.

Add a third deterministic baseline, `AlwaysAbstainBaseline`, to `data/demo-responses.json`, plus an integration test asserting it scores 100% abstention-recall, 0% answer-accuracy, and a selective accuracy equal to `AlwaysAnswerBaseline`'s. That test turns an implicit property of the metric into a demonstrated one.

## 6. Add the counterfactual arm — the important one

Today a model that **completely ignores the lab values** and pattern-matches the vignette gestalt scores 100% answer-accuracy on Full items and a 100% unsupported-answer rate on Ablated ones. That is exactly llama3.2:3b's scorecard shape. It is indistinguishable from a model that reads the evidence carefully and is merely overconfident — and those are different failure modes with different remedies.

Introduce a third `Variant.Counterfactual` alongside `Full` and `Ablated`. The counterfactual prompt keeps the vignette but **flips the decisive finding so it points at a different diagnosis** (c01: glucose 512 → glucose 88 with negative ketones; c07: potassium 7.2 → 4.1 with a normal ECG; and so on). If a model still answers the original diagnosis, it never read the finding — its score on the Full arm was memorization, not reasoning.

Scoring: on a counterfactual item, replying with the *original* diagnosis is a new outcome, `Outcome.EvidenceInsensitive`. Report an **evidence-sensitivity rate** per model. Wire it through `Item.FromCase`, the grader, `Scorecard`, all three report surfaces, and the baseline fixtures (this adds a `c01:counterfactual`-style key to every baseline in `demo-responses.json`).

You will need to author the counterfactual finding for each of the 12 cases. Draft them, but flag clearly in `TASK.md` that — like the original vignettes — the counterfactual medicine is **pending Alexander's human review** and is not canonical until he signs off.

## 7. Make the prompt a controlled variable

The system prompt is hardcoded at `OllamaModel.cs:11-14`. Abstention behavior is strongly prompt-sensitive, so the current llama result is a claim about *one* prompt, not about the model.

Lift the system prompt into configuration (`data/prompts.json`, with at least: abstention explicitly offered, abstention not mentioned, abstention strongly encouraged). Let `--prompt <name>` select one, record which was in force in the report, and support sweeping several so the report shows how much of the unsupported-answer rate is prompt-induced.

The README already notes that `ScriptedModel` is a fixture keyed on item id and never sees a system prompt. Keep that caveat accurate as prompt handling changes, and consider visually separating baseline rows from live-model rows in `ScorecardPage`.

---

## Out of scope — do not do these

- Do not wire the live Anthropic/OpenAI adapter (`llm` mode stays stubbed and failing closed).
- Do not build the LLM-judge grader — only make room for it (item 3.3).
- Do not add AURC / ECE / risk-coverage curves.
- Do not rewrite the medical content of the existing 12 vignettes.
- Do not act on the two **construct-validity** items in TASK.md (whether the ablated cases are genuinely under-determined; whether the *"single most likely diagnosis?"* phrasing contradicts the abstention instruction). Those are flagged for Alexander's human review and are his call, not yours. If your work surfaces more evidence about them, add it to TASK.md rather than changing the vignettes.

## Definition of done

- `dotnet build -c Release` clean, `dotnet test` green, CI green.
- No occurrence of the retired vocabulary anywhere in the repo.
- The `--gate` flag works, is documented in the README again, and is exercised in CI.
- `results/llama3.2-3b.json` exists with full per-item transcripts and provenance, and the README's llama table is regenerated from it — with confidence intervals, and with any number that turns out to have been a grader artifact rather than a model failure corrected and called out. Expect `answer-acc` to move: the v0 keyword matcher scores "STEMI" as wrong.
- The README states honestly what the benchmark can and cannot claim at n=12.
- `TASK.md` reflects the new state, with the counterfactual vignettes explicitly marked as awaiting human review.
