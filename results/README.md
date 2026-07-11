# Result artifacts

Current schema-5 observations:

- `llama3.2-3b-v2.json` — canonical `evidence-required` profile
- `llama3.2-3b-prompt-sweep-v2.json` — canonical versus noncanonical `forced-choice`

Each report contains the exact system message, rendered user message, raw JSON response, parsed
diagnosis/certainty/urgency, target, dimension-level grade, model digest, sampling settings, and
aggregate counts. Tests replay every current transcript through the repository dataset and concept
catalog so target, alias, grader, or metric drift cannot leave a stale score behind.

`legacy-v1/` contains the frozen schema-4 observations from the rejected binary-abstention and
negative-flip construct. Their raw replies remain valid observations for those exact prompts. Their
clinical labels and derived claims are not v2 results and are intentionally not replayed against the
replacement dataset.
