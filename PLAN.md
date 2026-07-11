# Plan: keep clinical-abstention-bench defensible

This repository is a .NET 10 selective-prediction benchmark. Its engineering contract is
fail-closed: every requested item must produce a score, every requested model must be available, and
every requested gate must be enforced. Its clinical content is synthetic methodology data, not
patient data or medical advice.

## Operational sources of truth

- `data/cases.json` owns the twelve case labels, accepted forms, and three variants: `full`,
  `ablated`, and `counterfactual`.
- `data/prompts.json` owns the four system-prompt arms and the default selection.
- `LexicalGrader` owns the current scoring semantics; its name travels with every report.
- Committed files under `results/` are auditable run artifacts, not input data or a replacement for
  the case and prompt files.

The case file is authoritative for program execution, but it is **not clinically canonical** until
the human review in `TASK.md` is complete.

## Reference-policy architecture

The offline demo contains three deterministic policies generated from `data/cases.json`:

| policy | construction | purpose |
|---|---|---|
| `AlwaysAnswerBaseline` | always returns the case's original diagnosis label | degenerate always-answer pole |
| `AlwaysAbstainBaseline` | always returns `INSUFFICIENT INFORMATION` | degenerate always-decline pole |
| `LabelOracleBaseline` | returns the supported label for each variant | label-defined target |

`LabelOracleBaseline` is perfect by construction because it consults the labels. It demonstrates
that the metrics and gate can represent the intended target; it does not demonstrate that the case
labels, accepted forms, or counterfactual claims are clinically correct. Reference policies never
receive a system prompt and must remain visually and textually separate from live-model results.

Live adapters receive only the item key and prompt. Ground-truth answers and scoring metadata must
not enter an inference request. Ollama is the implemented live adapter. A cloud-provider adapter is
only a possible future feature after a provider and contract are chosen; there is no cloud-model CLI
mode today.

## Metric and report invariants

- Full items measure answer accuracy and over-abstention.
- Ablated items measure abstention recall and unsupported-answer rate.
- Selective accuracy covers the full and ablated arms only.
- Counterfactual items form a separate evidence-sensitivity probe. They must not be folded into
  selective accuracy, because that would make abstention the majority label and reward silence.
- Every rate carries its count and 95 % Wilson score interval on console, JSON, and HTML surfaces.
- Every report carries per-item prompts, raw responses, outcomes, the grader name, system prompt,
  model provenance, and run timestamp.
- Prompt sweeps preserve one result per model-and-prompt pair. Reference policies are unaffected by
  all four prompt arms because they never receive a system prompt.

## Verification gates

Run these after every behavioral or data-schema change:

```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet run -c Release --no-build --project src/AbstentionBench -- demo

dotnet run -c Release --no-build --project src/AbstentionBench -- \
  demo --only LabelOracleBaseline --gate 0.9 --gate-answer-acc 0.9
```

CI must also prove both failure directions:

- `AlwaysAnswerBaseline` fails an abstention-recall gate.
- `AlwaysAbstainBaseline` passes a recall-only gate but fails when the answer-accuracy floor is added.

Do not document a remembered test count; the executed test command is the evidence.

## Construct validity — human decision required

> ⚠️ The twelve counterfactual findings, the accepted answer forms, and any narrower
> `counterfactualExcludedAnswers` are pending human review. Green engineering checks and the label
> oracle's perfect score do not constitute clinical sign-off.

The review must explicitly resolve:

- c03: a normal ECG negates STEMI, not all acute coronary syndromes; decide how replies such as
  “unstable angina” and broader MI aliases should score.
- c06: the flipped hot-joint case may still be septic and is an emergency; decide whether abstention
  is the right benchmark target.
- c10: the examination and normal appendix on CT conflict; decide whether under-determined is a
  defensible label.
- c11: the label depends on accepting the early-CT sensitivity premise.
- c02, c05, and c12: these carry the reported evidence-insensitivity finding and require the hardest
  review.
- All ablated prompts: decide whether a competent clinician would genuinely decline, especially c08
  and c12.
- Forced-choice wording: decide whether to change the user prompts or retain `no-forced-choice` as a
  controlled comparison.

Any prompt edit creates a new experimental condition and requires a new model run. A label or grader
edit may permit rescoring an existing raw response, but the resulting report must remain bound to the
exact case data, system prompt, and grader that produced the score.

## Future work

- Replace or complement the lexical grader with a validated semantic judge behind `IGrader`.
- Expand to a larger, sourced, licensed, clinician-reviewed dataset.
- Add risk-coverage curves once the sample supports them.
- Sweep temperature as another controlled variable.
- Consider a cloud-provider adapter only after its provider contract is concrete.
- Package the harness as a `dotnet new` template.
