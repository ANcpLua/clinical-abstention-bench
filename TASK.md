# TASK — clinical-abstention-bench

## End goal

A public selective-prediction benchmark that evaluates whether clinical decision-support models
match diagnostic specificity to evidence, express calibrated certainty, and preserve urgent action
under diagnostic uncertainty.

## Status: v2 construct implemented and evidence-adjudicated

The requested construct review is complete. Three independent AI judges reviewed the priority cases
against primary studies and current professional guidance, then the live code, data, aliases, prompt
profiles, metrics, reports, tests, and model observations were migrated together.

This is an evidence-based AI panel adjudication, not clinician validation. External clinical review
remains a publication requirement.

## Decisions completed

- [x] Review c02, c03, c05, c06, c08, c10, c11, and c12.
- [x] Replace flat `acceptedAnswers` with a complete-string concept catalog plus explicit co-equal and
      parent concept acceptance.
- [x] Remove organism, aneurysm, adjectival, manifestation, and other unsupported aliases.
- [x] Reject universal `MustAbstain`: only c01/c05/c07/c10/c11 ablations are indeterminate.
- [x] Make c08 probable acute cystitis and c12 probable hypoglycemia; neither requires abstention.
- [x] Score urgency separately, including c06/c12 as emergencies despite diagnostic uncertainty.
- [x] Replace negative-only counterfactuals with positive alternative-supported contrasts.
- [x] Retire `EvidenceInsensitive` and the invalid `1 - original repetition` score.
- [x] Implement standard coverage and conditional selective accuracy; retain decision accuracy as a
      separate equal-cost task score.
- [x] Remove forced-choice language from canonical user prompts and retain it only as the
      `forced-choice` stress arm.
- [x] Make each prompt profile own both the system and user messages.
- [x] Require structured diagnosis/certainty/urgency JSON and record exact transcripts/provenance.
- [x] Freeze schema-4 observations under `results/legacy-v1/` and run fresh schema-5 experiments.
- [x] Verify build, repository-backed tests, offline policies, gates, and live artifact replay.

## Current evidence

- 12 cases × 3 variants = 36 structured items.
- 5 genuine diagnostic-deferral targets across the 24 primary full + ablated items.
- `llama3.2:3b @ evidence-required` answered all 24 primary items, including all five null targets;
  selective accuracy was 11/24 and contrast accuracy was 6/12.
- The forced-choice arm also answered all 24, so this small run showed no coverage effect. It had
  lower descriptive certainty, urgency, and paired-revision point estimates; intervals overlap.
- Repository-backed tests replay current result artifacts and recognize frozen v1 files as legacy.

## Remaining work

- [ ] Independent clinician review of every target, parent concept, certainty, and urgency label.
- [ ] Larger sourced and licensed dataset with blinded multi-reviewer adjudication and disagreement
      reporting.
- [ ] Validated semantic grader or adjudication process for correct concepts expressed as undeclared
      composites, while preserving unsupported-specificity detection.
- [ ] Paired bootstrap or exact paired inference for prompt/model comparisons once sample size grows.
- [ ] Additional models, sites, languages, and sampling settings.
- [ ] Risk–coverage curves/AURC once models expose a usable confidence or deferral score.

## Publication constraint

Do not describe this dataset as clinician-validated or the current point estimates as model-general.
The synthetic fixtures and AI evidence review are suitable for methodology demonstration only.
