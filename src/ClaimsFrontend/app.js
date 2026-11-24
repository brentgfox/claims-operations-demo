const $ = id => document.getElementById(id);
let claims = [];
let selectedId = "CLM-2026-0042";
let loading = false;

async function request(path, options = {}) {
  const response = await fetch(path, options);
  const text = await response.text();
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    throw new Error(`Unexpected API response (HTTP ${response.status}). Check the API and frontend proxy.`);
  }
  if (!response.ok) {
    const validation = body.errors ? Object.values(body.errors).flat().join(" ") : "";
    const error = new Error(body.message || validation || body.title || `API request failed (HTTP ${response.status}).`);
    error.status = response.status;
    error.code = body.code;
    error.correlationId = body.correlationId || response.headers.get("X-Correlation-ID");
    throw error;
  }
  return body;
}

function setMessage(id, text) {
  $(id).textContent = text;
  $(id).classList.toggle("hidden", !text);
}

function showView(intake) {
  $("dashboard-view").classList.toggle("hidden", intake);
  $("intake-view").classList.toggle("hidden", !intake);
  $("nav-dashboard").classList.toggle("active", !intake);
  $("nav-intake").classList.toggle("active", intake);
  if (intake) $("intake-form").querySelector("input").focus();
}

function makeText(tag, text, className) {
  const element = document.createElement(tag);
  element.textContent = text;
  if (className) element.className = className;
  return element;
}

function renderList() {
  $("total-count").textContent = claims.length;
  $("waiting-count").textContent = claims.filter(claim => claim.status === "Awaiting routing").length;
  $("review-count").textContent = claims.filter(claim => claim.status === "In human review").length;
  const search = $("claim-search").value.toLowerCase();
  const matches = claims.filter(claim =>
    [claim.id, claim.workerReference, claim.employer].some(value => value.toLowerCase().includes(search)));
  const rows = $("claim-rows");
  rows.replaceChildren();
  for (const claim of matches) {
    const row = document.createElement("tr");
    row.classList.toggle("selected", claim.id === selectedId);
    const referenceCell = document.createElement("td");
    const link = makeText("button", claim.id, "claim-link");
    link.type = "button";
    link.addEventListener("click", () => {
      selectedId = claim.id;
      setMessage("route-error", "");
      setMessage("route-success", "");
      renderList();
      renderDetail();
    });
    referenceCell.append(link, makeText("small", claim.workerReference));
    const statusCell = document.createElement("td");
    statusCell.append(makeText("span", claim.status, `badge ${claim.status === "In human review" ? "review" : "waiting"}`));
    row.append(referenceCell, makeText("td", claim.employer), statusCell);
    rows.append(row);
  }
  if (!matches.length) {
    const row = document.createElement("tr");
    const cell = makeText("td", "No matching demo claims.");
    cell.colSpan = 3;
    row.append(cell);
    rows.append(row);
  }
}

function renderDetail() {
  const claim = claims.find(item => item.id === selectedId);
  const details = $("claim-details");
  details.replaceChildren();
  $("route-claim").classList.toggle("hidden", !claim);
  if (!claim) {
    $("detail-title").textContent = "Select a demo claim";
    details.append(makeText("p", "This record may have reset after a restart. Refresh and select a seeded claim.", "muted"));
    return;
  }
  $("detail-title").textContent = claim.id;
  const list = document.createElement("dl");
  for (const [label, value] of [
    ["Worker reference", claim.workerReference], ["Employer", claim.employer],
    ["Incident date", claim.incidentDate], ["Category", claim.incidentCategory]
  ]) list.append(makeText("dt", label), makeText("dd", value));
  details.append(list, makeText("p", claim.summary, "summary"));
  const journey = document.createElement("ol");
  journey.className = "journey";
  journey.append(makeText("li", "Intake recorded", "done"));
  journey.append(makeText("li", claim.status === "In human review" ? "Assigned to human review queue" : "Routing pending",
    claim.status === "In human review" ? "done" : "current"));
  journey.append(makeText("li", "Human reviewer evaluates the claim", "muted"));
  details.append(journey);
  if (claim.reviewQueue) details.append(makeText("p", `Review queue: ${claim.reviewQueue}`, "muted"));
  $("route-claim").disabled = loading || claim.status === "In human review";
  $("route-claim").textContent = claim.status === "In human review" ? "Assigned for human review" : "Route for human review";
}

async function refresh() {
  $("refresh").disabled = true;
  try {
    const [records, demo] = await Promise.all([request("/api/claims"), request("/api/demo")]);
    claims = records;
    $("api-version").textContent = `API ${demo.version}`;
    setMessage("global-error", "");
    renderList();
    renderDetail();
  } catch (error) {
    $("api-version").textContent = "Connection error";
    setMessage("global-error", error.message);
  } finally {
    $("refresh").disabled = false;
  }
}

$("route-claim").addEventListener("click", async () => {
  loading = true;
  renderDetail();
  setMessage("route-error", "");
  setMessage("route-success", "");
  try {
    const claim = await request(`/api/claims/${encodeURIComponent(selectedId)}/route`, { method: "POST" });
    claims = claims.map(item => item.id === claim.id ? claim : item);
    setMessage("route-success", "Claim routed successfully. A human reviewer performs the next step; no benefits decision was made.");
    renderList();
  } catch (error) {
    setMessage("route-error", [
      `Routing blocked${error.status ? ` · HTTP ${error.status}` : ""}. ${error.message}`,
      error.code ? `Code: ${error.code}` : "",
      error.correlationId ? `Correlation: ${error.correlationId}` : "",
      "Intake remains recorded. Try again after service recovery."
    ].filter(Boolean).join("\n"));
  } finally {
    loading = false;
    renderDetail();
  }
});

$("intake-form").addEventListener("submit", async event => {
  event.preventDefault();
  $("submit-intake").disabled = true;
  setMessage("intake-error", "");
  try {
    const input = Object.fromEntries(new FormData(event.currentTarget).entries());
    const claim = await request("/api/claims", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(input)
    });
    claims.unshift(claim);
    selectedId = claim.id;
    setMessage("route-error", "");
    setMessage("route-success", "Synthetic intake recorded. Route it to a human reviewer when ready.");
    renderList();
    renderDetail();
    showView(false);
  } catch (error) {
    setMessage("intake-error", error.message);
  } finally {
    $("submit-intake").disabled = false;
  }
});

for (const id of ["new-claim", "nav-intake"]) $(id).addEventListener("click", () => showView(true));
for (const id of ["nav-dashboard", "back-dashboard"]) $(id).addEventListener("click", () => showView(false));
$("refresh").addEventListener("click", () => {
  setMessage("route-error", "");
  setMessage("route-success", "");
  refresh();
});
$("claim-search").addEventListener("input", renderList);
const today = new Date().toISOString().slice(0, 10);
$("incident-date").value = today;
$("incident-date").max = today;
refresh();
