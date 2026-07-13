# Evidence and construct decisions

Status: evidence-based AI panel adjudication, not clinician sign-off. The cases are synthetic methodology fixtures and are not medical advice. A qualified, independent clinical reviewer is still required before presenting the dataset as clinician-validated.

## Construct

A normal or negative test often changes probability without logically excluding a diagnosis, so a negative-only vignette commonly remains a differential rather than yielding a scoreable target. Every `contrast` variant contains positive evidence for a determinate alternative diagnosis.

Seven ablated cases support an answer at an honest level of certainty or granularity:

- `c02`: probable meningitis, not an organism or bacterial subtype
- `c03`: probable acute coronary syndrome, not STEMI versus NSTEMI versus unstable angina
- `c04`: established anaemia, with its cause unresolved
- `c06`: probable gout, while septic arthritis still requires emergency exclusion
- `c08`: probable acute cystitis from the clinical syndrome
- `c09`: probable acute lower respiratory tract infection, not established pneumonia
- `c12`: probable hypoglycaemia requiring immediate glucose assessment or treatment

Only `c01`, `c05`, `c07`, `c10`, and `c11` have `diagnosticStatus: indeterminate` after ablation.

## Data contract

Each case has `full`, `ablated`, and `contrast` variants. Every variant contains a vignette and a target with:

- `diagnosis`: the canonical concept ID, or `null` when no diagnosis is supported
- `diagnosticStatus`: `established`, `probable`, or `indeterminate`
- `acceptedConcepts`: additional co-equal concept IDs; empty when the canonical concept is the only same-level target
- `acceptedParentConcepts`: optional, explicit less-specific concepts accepted for that target
- `urgency`: `emergency`, `urgent`, or `routine`

Certainty and urgency are orthogonal. `emergency` means immediate evaluation or treatment is warranted because delay may cause serious harm. `urgent` means prompt, generally same-day or otherwise rapid evaluation is warranted. `routine` means the vignette can ordinarily follow a non-emergency outpatient pathway. These are benchmark annotations, not patient-specific triage advice. An indeterminate emergency must trigger action without forcing a diagnostic guess.

`data/concepts.json` is the sole surface-form catalog. `preferredName` and every alias are complete diagnosis strings. Matching ignores case and surrounding whitespace; regional spelling, punctuation, hyphenation, and abbreviations must be declared explicitly. Substring matching is forbidden. Bare adjectives such as “hypoglycemic” and over-specific unsupported causes such as “meningococcal meningitis” or “ruptured aneurysm” are deliberately absent.

Vignettes contain evidence only. Prompt wording is a controlled experiment owned by `data/prompts.json`, not duplicated inside clinical data. The evidence-required arm is canonical; forced choice is a labeled comparison for measuring prompt-induced guessing.

## Case-by-case adjudication

| Case | Full target | Ablated decision | Contrast target | Adjudication |
|---|---|---|---|---|
| c01 | Established diabetic ketoacidosis | Indeterminate | Central diabetes insipidus | DKA is established by hyperglycaemia/diabetes, ketosis, and acidosis together. Symptoms alone are nonspecific. Water-deprivation and desmopressin physiology supplies a positive contrast. |
| c02 | **Probable** bacterial meningitis | Probable meningitis | Enteroviral meningitis | The CSF pattern strongly supports bacterial meningitis but does not identify an organism and is not treated as an absolute standalone confirmation. Organism-specific aliases were removed. Suspected meningitis remains an emergency before subtype confirmation. |
| c03 | Established inferior STEMI | Probable acute coronary syndrome | NSTEMI | A nondiagnostic ECG does not exclude ACS. “Unstable angina” is possible, not established. Dynamic troponin plus ischemic imaging establishes NSTEMI in the contrast. |
| c04 | Established iron-deficiency anaemia | Established anaemia | Vitamin B12 deficiency anaemia | Removing MCV and iron studies removes aetiology, not the measured anaemia. The benchmark accepts the supported parent-level diagnosis. |
| c05 | Established primary hypothyroidism | Indeterminate | Iron-deficiency anaemia | Symptoms alone are nonspecific; high TSH with low free T4 establishes primary hypothyroidism. The contrast uses positive evidence for iron-deficiency anaemia. |
| c06 | Established gout | **Probable gout; emergency** | Culture-proven *S. aureus* septic arthritis | Typical podagra supports a clinical gout diagnosis, but infection must still be addressed. Negative crystal microscopy does not exclude gout, crystals do not exclude coexisting sepsis, and Gram stain lacks adequate sensitivity to rule it out. |
| c07 | Established hyperkalaemia | Indeterminate emergency | Hypokalaemia | Dialysis plus weakness/palpitations demands immediate testing but does not establish potassium direction. Measured potassium and ECG findings support each determinate arm. |
| c08 | Established *E. coli* acute cystitis | **Probable acute cystitis; no abstention required** | *Chlamydia trachomatis* infection | In an otherwise healthy, nonpregnant woman, dysuria/frequency without vaginal symptoms supports high-probability cystitis; urinalysis/culture establish the organism-specific full target. Chlamydial NAAT supplies the positive alternative without forcing urethritis versus cervicitis. |
| c09 | Established community-acquired pneumonia | Probable acute lower respiratory tract infection | Influenza A | Consolidation establishes pneumonia but not a bacterial aetiology. Symptoms without imaging still support a respiratory-infection syndrome. Molecular testing supplies the influenza target. |
| c10 | Established acute appendicitis | Indeterminate | Ureteric calculus | CT is highly accurate but not logically infallible. A CT-visible stone makes the contrast determinate. |
| c11 | Established subarachnoid haemorrhage | Indeterminate emergency | Internal carotid artery dissection | Basal-cistern blood establishes SAH but not an aneurysmal source. The early-CT rule depends on scanner, reader, timing, haemoglobin, and neurologic-status conditions. CTA evidence establishes dissection. |
| c12 | Established hypoglycaemia | **Probable hypoglycaemia; no abstention required** | CT-proven intracerebral haemorrhage | Confusion and sweating during insulin treatment support an immediate working diagnosis, glucose check, and treatment. A low value plus symptom resolution establishes the full diagnosis. Adjective-only and invented manifestation aliases were removed. |

## Evidence used

Sources were selected for diagnostic claims rather than management detail:

1. American Diabetes Association-led consensus, [Hyperglycemic Crises in Adults With Diabetes](https://diabetesjournals.org/care/article/47/8/1257/156808/Hyperglycemic-Crises-in-Adults-With-Diabetes-A), and NIDDK-authored Endotext, [Diagnostic Tests for Diabetes Insipidus](https://www.ncbi.nlm.nih.gov/books/NBK537591/): DKA requires diabetes/hyperglycaemia, ketosis, and metabolic acidosis; persistent hypotonic urine during water deprivation followed by a marked desmopressin response supports central diabetes insipidus.
2. World Health Organization, [WHO guidelines on meningitis diagnosis, treatment and care](https://www.who.int/publications/i/item/9789240108042) and [meningitis fact sheet](https://www.who.int/news-room/fact-sheets/detail/meningitis): CSF evaluation is central, pathogen testing is needed for aetiology, and suspected bacterial meningitis is an emergency whose treatment must not await lumbar-puncture results.
3. American College of Cardiology/American Heart Association, [2025 Guideline for the Management of Patients With Acute Coronary Syndromes](https://www.jacc.org/doi/10.1016/j.jacc.2024.11.009), and ACC, [2022 acute chest-pain consensus key points](https://www.acc.org/Latest-in-Cardiology/ten-points-to-remember/2022/10/10/23/15/2022-ACC-Expert-Consensus-on-Chest-Pain): ACS includes STEMI, NSTEMI, and unstable angina; nonischaemic ECGs enter diagnostic pathways, and serial high-sensitivity troponin is essential for MI confirmation.
4. World Health Organization, [use of ferritin concentrations to assess iron status](https://www.who.int/tools/elena/interventions/ferritin-concentrations), and NICE, [vitamin B12 deficiency recommendations](https://www.nice.org.uk/guidance/ng239/chapter/recommendations): low ferritin supports iron deficiency; B12 and methylmalonic-acid testing support B12 deficiency assessment.
5. American Thyroid Association, [Thyroid Function Tests](https://www.thyroid.org/thyroid-function-tests/): elevated TSH with low free T4 indicates primary hypothyroidism.
6. EULAR, [2018 evidence-based recommendations for gout diagnosis](https://ard.bmj.com/content/79/1/31), and SANJO, [guideline for septic arthritis in native joints](https://jbji.copernicus.org/articles/8/29/2023/): crystal identification is definitive for gout, typical podagra can support a clinical diagnosis, and crystals do not rule out septic arthritis.
7. KDIGO, [potassium homeostasis and management of dyskalaemia](https://kdigo.org/wp-content/uploads/2018/04/KDIGO-Potassium-Management-corrected-proof.pdf): measured potassium and ECG changes drive acute dyskalaemia assessment.
8. European Association of Urology, [Urological Infections guideline](https://uroweb.org/guidelines/urological-infections/chapter/the-guideline): typical lower urinary symptoms with absence of vaginal discharge support clinical cystitis diagnosis, with testing adding limited accuracy in typical cases.
9. CDC, [Chlamydial Infections](https://www.cdc.gov/std/treatment-guidelines/chlamydia.htm): NAAT is the recommended and most sensitive urogenital test for *C. trachomatis*.
10. IDSA/ASM, [Guide to Utilization of the Microbiology Laboratory](https://www.idsociety.org/globalassets/idsa/practice-guidelines/a-guide-to-utilization-of-the-microbiology-laboratory-for-diagnosis-of-infectious-diseases-2018-update-by-the-infectious-diseases-society-of-america-and-the-american-society-for-microbiology.pdf), and IDSA, [seasonal influenza guideline](https://www.idsociety.org/practice-guideline/influenza/): CAP combines a compatible syndrome with radiographic findings; molecular respiratory testing can establish influenza.
11. American College of Radiology, [Right Lower Quadrant Pain appropriateness criteria](https://acsearch.acr.org/docs/69357/Narrative/): CT evaluates appendicitis and competing right-lower-quadrant diagnoses; imaging is evidence, not an infallible logical exclusion.
12. ACEP, [Clinical Policy for Acute Headache](https://www.acep.org/siteassets/sites/acep/media/clinical-policies/cp-headache.pdf), and Dubosh et al., [Sensitivity of Early Brain CT to Exclude Aneurysmal SAH](https://pubmed.ncbi.nlm.nih.gov/26797666/): modern CT within six hours in neurologically intact patients is extremely sensitive but has documented misses and strict applicability conditions.
13. American Heart Association, [Treatment and Outcomes of Cervical Artery Dissection in Adults](https://professional.heart.org/en/science-news/treatment-and-outcomes-of-cervical-artery-dissection-in-adults/top-things-to-know): CTA is among the accepted diagnostic modalities for cervical artery dissection.
14. American Diabetes Association, [Standards of Care in Diabetes—2026: Hypoglycemia](https://diabetesjournals.org/care/article/49/Supplement_1/S132/163927/6-Glycemic-Goals-Hypoglycemia-and-Hyperglycemic), and AHA/ASA, [2022 spontaneous intracerebral haemorrhage guideline](https://www.heart.org/en/-/media/CPR2-Files/Private/2022-Guideline-for-the-Management-of-Patients-With-Spontaneous-Intracerebral-Hemorrhage-1.pdf): confusion and sweating are compatible hypoglycaemia symptoms, measured glucose must be correlated with symptoms and treatment, and rapid CT or MRI confirms ICH in a stroke-like presentation.

## Remaining limitation

These are compact synthetic vignettes, not validated clinical prediction rules. “Established” means the text supplies conventional benchmark-level confirming evidence; it does not claim that real-world medicine has zero false positives, no comorbidity, or no need for confirmatory workflow. Benchmark reports must keep status and urgency visible and must not equate diagnostic abstention with clinical inaction.
