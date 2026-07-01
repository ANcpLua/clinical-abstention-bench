# TASK — clinical-abstention-bench

## End goal
A public benchmark that scores whether medical AI **abstains when the input data is insufficient**
(calibrated abstention on degraded clinical inputs), built on the `ancplua.evaluation` fail-closed
engine. Intended as the concrete artifact behind an Anthropic *AI for Science* (Standard track)
application — and, longer-term, a benchmark others run their models through.

## Why it matters
Almost all medical-AI benchmarks score accuracy; abstention/calibration ("does it know when it
doesn't know?") is under-benchmarked. A confident wrong diagnosis is worse than an honest "I can't
tell." Patient-safety-relevant, biomedical-center, and it reuses assets already built
(`ancplua.evaluation.template`, `dicom-fhir-viewer`, the info-theory research framing).

## Status: v0 complete & verified — OPEN FOR HUMAN REVIEW (do not auto-merge)
The medical vignettes need Alexander's eyes before this is treated as canonical. Build + tests are
green; the content is what needs a human check.

## Progress
- [x] Dataset v0 — 12 textbook full/degraded case-pairs (`data/cases.json`)
- [x] Harness — load, run, score (answer vs bluff vs over-abstain), fail-closed gate
- [x] Metrics — abstention-recall, bluff-rate, answer-accuracy, over-abstention, honesty score
- [x] Deterministic offline demo models (BluffBot vs CalibratedBot) — no credentials
- [x] 14 unit + integration tests, green (`dotnet test`)
- [x] CI (public repo, GitHub-hosted, free) — build (warnings-as-errors), test, demo run
- [ ] **Human review of the 12 medical vignettes** ← next, Alexander
- [ ] v1: live LLM adapter (Anthropic/OpenAI via `Microsoft.Extensions.AI`) — `llm` mode is stubbed, fails closed
- [ ] v1: risk–coverage curve, AURC, ECE metrics
- [ ] v1: larger sourced dataset + LLM-judge grader (replace keyword matcher)
- [ ] v1: `dotnet new` template packaging
- [ ] Grounding research (novelty positioning vs existing abstention/selective-prediction work, dataset licensing, metric citations) — folds into the exposé

## Notes for the exposé (AI for Science / Standard)
- Frame: patient safety + trustworthy automated triage; "AI accelerates the work" = Claude builds the
  dataset/checks and is itself tested + used as judge in the benchmark.
- Red thread: same "output only what the evidence supports, else abstain" principle as the herbarium
  info-theory exposé and the eval engine's GateAsync.
