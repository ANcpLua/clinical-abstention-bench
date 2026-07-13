const DATA_URLS = {
  cases: "./data/cases.json",
  concepts: "./data/concepts.json",
  results: "./results/llama3.2-3b.json"
};

const VARIANTS = ["full", "ablated", "contrast"];
const state = {
  cases: [],
  concepts: new Map(),
  results: null,
  canonicalModel: null,
  caseId: "c06",
  variant: "ablated",
  query: ""
};

const elements = {
  answerableCount: document.querySelector("#answerable-count"),
  deferralCount: document.querySelector("#deferral-count"),
  metrics: document.querySelector("#headline-metrics"),
  caseSearch: document.querySelector("#case-search"),
  caseList: document.querySelector("#case-list"),
  caseId: document.querySelector("#case-id"),
  caseName: document.querySelector("#case-name"),
  vignette: document.querySelector("#case-vignette"),
  tabs: [...document.querySelectorAll("#state-tabs button")],
  targetStatus: document.querySelector("#target-status"),
  targetDiagnosis: document.querySelector("#target-diagnosis"),
  targetCertainty: document.querySelector("#target-certainty"),
  targetUrgency: document.querySelector("#target-urgency"),
  modelStatus: document.querySelector("#model-status"),
  modelDiagnosis: document.querySelector("#model-diagnosis"),
  modelCertainty: document.querySelector("#model-certainty"),
  modelUrgency: document.querySelector("#model-urgency"),
  adjudication: document.querySelector("#case-adjudication"),
  resultsBody: document.querySelector("#results-table tbody"),
  error: document.querySelector("#error-banner")
};

function text(element, value) {
  element.textContent = value;
}

function conceptName(id) {
  if (!id) return "Diagnostic deferral";
  return state.concepts.get(id)?.preferredName
    ?? id.split("_").map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(" ");
}

function rateText(rate) {
  return `${rate.successes}/${rate.total}`;
}

function percent(rate) {
  return `${Math.round(rate.value * 100)}%`;
}

function interval(rate) {
  const [low, high] = rate.ci95;
  return `95% CI ${Math.round(low * 100)}–${Math.round(high * 100)}%`;
}

function parseUrl() {
  const params = new URLSearchParams(location.search);
  const caseId = params.get("case");
  const variant = params.get("state");
  if (caseId && state.cases.some(item => item.id === caseId)) state.caseId = caseId;
  if (variant && VARIANTS.includes(variant)) state.variant = variant;
}

function writeUrl() {
  const url = new URL(location.href);
  url.searchParams.set("case", state.caseId);
  url.searchParams.set("state", state.variant);
  history.replaceState(null, "", url);
}

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} returned ${response.status}`);
  return response.json();
}

function renderCounts() {
  const answerable = state.cases.filter(item => item.variants.ablated.target.diagnosis !== null).length;
  text(elements.answerableCount, answerable);
  text(elements.deferralCount, state.cases.length - answerable);
}

function metricCard(label, rate, note, warning = false) {
  const article = document.createElement("article");
  article.className = `metric-card${warning ? " metric-warning" : ""}`;

  const title = document.createElement("span");
  title.textContent = label;
  const value = document.createElement("strong");
  value.textContent = rateText(rate);
  value.title = `${percent(rate)}; ${interval(rate)}`;
  const small = document.createElement("small");
  small.textContent = `${note} · ${interval(rate)}`;

  article.append(title, value, small);
  return article;
}

function renderMetrics() {
  const model = state.canonicalModel;
  elements.metrics.replaceChildren(
    metricCard("Deferrals recognized", model.abstentionRecall, "genuine null targets"),
    metricCard("Selective accuracy", model.selectiveAccuracy, "among answered items"),
    metricCard("Paired revisions", model.pairedRevisionAccuracy, "full → contrast"),
    metricCard("Undertriage", model.undertriageRate, "primary items", true)
  );
}

function caseSearchText(item) {
  return [
    item.id,
    ...VARIANTS.map(variant => conceptName(item.variants[variant].target.diagnosis)),
    ...VARIANTS.map(variant => item.variants[variant].vignette),
    item.adjudication
  ].join(" ").toLocaleLowerCase();
}

function renderCaseList() {
  const fragment = document.createDocumentFragment();
  const matches = state.cases.filter(item => caseSearchText(item).includes(state.query));

  for (const item of matches) {
    const button = document.createElement("button");
    button.type = "button";
    button.dataset.caseId = item.id;
    button.setAttribute("aria-current", String(item.id === state.caseId));

    const id = document.createElement("span");
    id.textContent = item.id;
    const name = document.createElement("span");
    name.textContent = conceptName(item.variants.full.target.diagnosis);
    button.append(id, name);

    button.addEventListener("click", () => {
      state.caseId = item.id;
      renderCaseList();
      renderCase();
      writeUrl();
    });
    fragment.append(button);
  }

  if (matches.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "No cases match that search.";
    empty.className = "finding-note";
    fragment.append(empty);
  }

  elements.caseList.replaceChildren(fragment);
}

function transcriptFor(caseId, variant) {
  return state.results.transcripts.find(entry =>
    entry.model === state.canonicalModel.modelName
      && entry.caseId === caseId
      && entry.variant === variant);
}

function setPill(element, label, kind = "") {
  element.className = `status-pill${kind ? ` ${kind}` : ""}`;
  text(element, label);
}

function renderCase() {
  const item = state.cases.find(candidate => candidate.id === state.caseId) ?? state.cases[0];
  state.caseId = item.id;
  const variant = item.variants[state.variant];
  const transcript = transcriptFor(item.id, state.variant);
  const target = variant.target;

  text(elements.caseId, item.id);
  text(elements.caseName, conceptName(item.variants.full.target.diagnosis));
  text(elements.vignette, variant.vignette);
  text(elements.targetDiagnosis, conceptName(target.diagnosis));
  text(elements.targetCertainty, target.diagnosticStatus);
  text(elements.targetUrgency, target.urgency);
  setPill(elements.targetStatus, target.diagnosis === null ? "defer" : "answer");
  text(elements.adjudication, item.adjudication);

  for (const tab of elements.tabs) {
    const selected = tab.dataset.variant === state.variant;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
  }

  if (transcript) {
    const response = transcript.parsedResponse;
    const grade = transcript.grade;
    const diagnosisCorrect = grade.diagnosisOutcome === "correct-diagnosis"
      || grade.diagnosisOutcome === "correct-deferral";
    const dimensionsCorrect = [diagnosisCorrect, grade.certaintyCorrect, grade.urgencyCorrect]
      .filter(Boolean).length;

    text(elements.modelDiagnosis, response.diagnosis ?? "Diagnostic deferral");
    text(elements.modelCertainty, response.certainty);
    text(elements.modelUrgency, response.urgency);
    const kind = dimensionsCorrect === 3 ? "is-correct" : dimensionsCorrect === 0 ? "is-wrong" : "is-partial";
    setPill(elements.modelStatus, `${dimensionsCorrect}/3 correct`, kind);
  } else {
    text(elements.modelDiagnosis, "No recorded response");
    text(elements.modelCertainty, "—");
    text(elements.modelUrgency, "—");
    setPill(elements.modelStatus, "missing", "is-wrong");
  }

}

function tableCell(rate) {
  const cell = document.createElement("td");
  cell.textContent = rateText(rate);
  const detail = document.createElement("small");
  detail.textContent = `${percent(rate)} · ${interval(rate)}`;
  cell.append(detail);
  return cell;
}

function renderResults() {
  const fragment = document.createDocumentFragment();
  for (const model of state.results.models) {
    const row = document.createElement("tr");
    const name = document.createElement("td");
    name.textContent = model.modelName.includes("evidence-required") ? "Evidence required" : "Forced choice";
    row.append(
      name,
      tableCell(model.coverage),
      tableCell(model.selectiveAccuracy),
      tableCell(model.abstentionRecall),
      tableCell(model.urgencyAccuracy),
      tableCell(model.pairedRevisionAccuracy)
    );
    fragment.append(row);
  }
  elements.resultsBody.replaceChildren(fragment);
}

function bindEvents() {
  elements.caseSearch.addEventListener("input", event => {
    state.query = event.currentTarget.value.trim().toLocaleLowerCase();
    renderCaseList();
  });

  for (const tab of elements.tabs) {
    tab.addEventListener("click", () => {
      state.variant = tab.dataset.variant;
      renderCase();
      writeUrl();
    });

    tab.addEventListener("keydown", event => {
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
      const direction = event.key === "ArrowRight" ? 1 : -1;
      const nextIndex = (VARIANTS.indexOf(state.variant) + direction + VARIANTS.length) % VARIANTS.length;
      state.variant = VARIANTS[nextIndex];
      renderCase();
      writeUrl();
      elements.tabs[nextIndex].focus();
    });
  }

  addEventListener("popstate", () => {
    parseUrl();
    renderCaseList();
    renderCase();
  });
}

async function start() {
  try {
    const [caseFile, conceptFile, results] = await Promise.all([
      fetchJson(DATA_URLS.cases),
      fetchJson(DATA_URLS.concepts),
      fetchJson(DATA_URLS.results)
    ]);

    state.cases = caseFile.cases;
    state.concepts = new Map(conceptFile.concepts.map(concept => [concept.id, concept]));
    state.results = results;
    state.canonicalModel = results.models.find(model => model.modelName.includes("evidence-required"));

    if (!state.cases.length || !state.canonicalModel) throw new Error("Required benchmark data is empty");

    parseUrl();
    bindEvents();
    renderCounts();
    renderMetrics();
    renderCaseList();
    renderCase();
    renderResults();
  } catch (error) {
    console.error(error);
    elements.error.hidden = false;
  }
}

start();
