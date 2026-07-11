# Plan: preserve the v2 evidence-assessment contract

## Sources of truth

- `data/cases.json` owns the 12 case triplets and diagnosis/certainty/urgency targets.
- `data/concepts.json` owns complete-string diagnostic names and aliases.
- `data/prompts.json` owns complete inference profiles: system message plus user template.
- `CLINICAL_REVIEW.md` records adjudication, limitations, and evidence.
- `StructuredConceptGrader` owns strict response parsing and concept resolution.
- Current schema-5 files under `results/` are model observations; schema-4 files under
  `results/legacy-v1/` are frozen historical observations, not current labels.

## Contract invariants

1. Stored vignettes contain evidence only. Prompt profiles render every question centrally.
2. `evidence-required` is the sole canonical profile. `forced-choice` is a noncanonical stress arm.
3. A target diagnosis may be null, established, or probable independently of the variant name.
4. Null diagnosis requires `indeterminate`; a non-null diagnosis cannot be `indeterminate`.
5. Urgency is scored independently. Diagnostic deferral never implies routine care.
6. Aliases are complete fields. Matching is case-insensitive after trimming only; no substring rule.
7. Broader acceptable concepts are explicit per target. A parent is never inferred implicitly.
8. Contrast variants support a determinate alternative; they are not negative-only rule-out claims.
9. Selective accuracy is conditional on answered primary items and must travel with coverage.
10. Current reports retain exact sent prompts, raw responses, parsed response, target, grade, model
    digest, sampling configuration, counts, and Wilson intervals.

## Reference policies

The offline demo generates three structured policies directly from repository targets:

| Policy | Rule | Purpose |
|---|---|---|
| `AlwaysAnswerBaseline` | original concept, established, routine | always-answer failure pole |
| `AlwaysAbstainBaseline` | null, indeterminate, routine | always-defer failure pole |
| `LabelOracleBaseline` | configured target in every dimension | scoring-path target, perfect by construction |

They never receive a system prompt and must remain visually separate from live-model rows.

## Verification gates

Run after behavioral, schema, prompt, or target changes:

```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet run -c Release --no-build --project src/AbstentionBench -- demo
dotnet run -c Release --no-build --project src/AbstentionBench -- \
  demo --only LabelOracleBaseline \
  --gate-coverage 0.75 --gate-selective-acc 0.9 --gate-urgency-acc 0.9
```

CI must also demonstrate that AlwaysAnswer fails the selective-accuracy floor and AlwaysAbstain
fails the coverage floor.

Any change to evidence text or either message in a prompt profile requires a fresh model run. A pure
target, concept, or grader edit may rescore recorded raw output, but the report must then be rebuilt
against the exact new contract and retain the original sent messages. Never rewrite legacy-v1 files.

## Next validity step

Commission independent blinded clinical review. Reviewers should annotate diagnosis concept,
specificity, certainty, urgency, and acceptable parents separately, with disagreements retained.
Until then, describe v2 as evidence-adjudicated synthetic methodology data, not clinician-validated.
