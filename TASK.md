# TASK — clinical-abstention-bench

## End goal
A public **selective-prediction** benchmark that scores whether a clinical decision-support model
**abstains when the input evidence is insufficient**, built on the `ancplua.evaluation` fail-closed
engine. Intended as the concrete artifact behind an Anthropic *AI for Science* (Standard track)
application — and, longer-term, a benchmark others run their models through.

## Why it matters
Almost all medical-AI benchmarks score accuracy; abstention/calibration ("does it know when it
doesn't know?") is under-benchmarked. A confident wrong diagnosis is worse than an explicit "I can't
tell." Patient-safety-relevant, biomedical-center, and it reuses assets already built
(`ancplua.evaluation.template`, `dicom-fhir-viewer`, the info-theory research framing).

## Status: v0 complete & verified — OPEN FOR HUMAN REVIEW (do not auto-merge)
Build + tests are green; the **content and the construct** are what need a human check.

## Progress
- [x] Dataset v0 — 12 textbook full/ablated case-pairs (`data/cases.json`)
- [x] Harness — load, run, score, fail-closed gate
- [x] Metrics — abstention-recall, unsupported-answer rate, answer-accuracy, over-abstention, selective-accuracy
- [x] Deterministic offline baselines (AlwaysAnswerBaseline vs CalibratedBaseline) — no credentials
- [x] 15 unit + integration tests, green (`dotnet test`)
- [x] CI (public repo, GitHub-hosted, free) — build (warnings-as-errors), test, demo run
- [x] Terminology migrated to standard selective-prediction vocabulary (no adversarial/anthropomorphic naming)

## Known defects
- **`--gate` is unusable as documented.** `RunDemoAsync` always loads the baseline models and the gate
  checks *every* model in the run; `AlwaysAnswerBaseline` has 0 % abstention-recall by construction, so
  `demo --gate 0.9` always exits 1. Needs `--only <name>` / `--no-baselines` model selection so the gate
  can be pointed at a real model in CI. The example has been pulled from the README until it works.

## Next — construct validity (Alexander)
- [ ] **Human review of the 12 vignettes.** The bar is not just "is the medicine right" but
      **"would a competent clinician genuinely decline on the ablated prompt?"** Several may not clear
      it — c08 (dysuria + frequency in a young woman) is treatable empirically as uncomplicated
      cystitis per IDSA without a urinalysis, and c12 (insulin-treated diabetic, confused + diaphoretic)
      is treated as hypoglycemia on sight. If answering those is *correct medicine*, then scoring them
      as unsupported answers measures an artifact of our construction, not a calibration failure.
- [ ] **The prompts contradict the task.** Every ablated prompt still ends *"What is the single most
      likely diagnosis?"* — we ask for the top of the differential, then penalise the model for giving
      it. Reconcile the user prompt with the system prompt's abstention offer.

## v1 backlog
- [ ] Per-item transcripts + run provenance in the report (today's report.json is aggregates only, so
      the llama3.2:3b claim is unauditable and has no committed evidence behind it)
- [ ] Wilson confidence intervals on every rate (n = 12; 100 % carries a 95 % CI of ~[76 %, 100 %])
- [ ] `AlwaysAbstainBaseline` — abstention-recall is trivially maximised by always abstaining; the
      metric *does* punish this (50 % selective accuracy) but nothing in the repo demonstrates it
- [ ] **Counterfactual arm** — flip the decisive finding to point at a different diagnosis. Today a
      model that ignores the labs entirely and a model that reads them but is overconfident produce
      *identical* scorecards. This separates them, and it is the strongest scientific addition available.
- [ ] Synonym-aware + negation-aware grader (today "STEMI" scores wrong, "not DKA" scores right), then
      an LLM-judge grader behind an `IGrader` seam
- [ ] System prompt as a controlled variable (`data/prompts.json`, `--prompt <name>`) — abstention is
      prompt-sensitive, so the current llama result is a claim about one prompt, not about the model
- [ ] Live LLM adapter (Anthropic/OpenAI via `Microsoft.Extensions.AI`) — `llm` mode is stubbed, fails closed
- [ ] Larger sourced dataset; `dotnet new` template packaging
- [ ] Grounding research (novelty positioning vs existing abstention/selective-prediction work, dataset
      licensing, metric citations) — folds into the exposé

## Notes for the exposé (AI for Science / Standard)
- Frame: patient safety + trustworthy automated triage; "AI accelerates the work" = Claude builds the
  dataset/checks and is itself tested + used as judge in the benchmark.
- Red thread: same "output only what the evidence supports, else abstain" principle as the herbarium
  info-theory exposé and the eval engine's GateAsync.
