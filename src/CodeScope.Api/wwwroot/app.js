const elements = {
  form: document.querySelector('#scan'),
  path: document.querySelector('#path'),
  scanButton: document.querySelector('#scanButton'),
  status: document.querySelector('#status'),
  progressCard: document.querySelector('#progressCard'),
  progressStage: document.querySelector('#progressStage'),
  progressMessage: document.querySelector('#progressMessage'),
  progressBar: document.querySelector('#progressBar'),
  progressMetrics: document.querySelector('#progressMetrics'),
  progressError: document.querySelector('#progressError'),
  cancel: document.querySelector('#cancel'),
  dashboard: document.querySelector('#dashboard'),
  history: document.querySelector('#history'),
  refreshHistory: document.querySelector('#refreshHistory'),
  projects: document.querySelector('#projects'),
  query: document.querySelector('#query'),
  search: document.querySelector('#search'),
  results: document.querySelector('#results'),
  relationsCard: document.querySelector('#relationsCard'),
  relationsTitle: document.querySelector('#relationsTitle'),
  relations: document.querySelector('#relations'),
  closeRelations: document.querySelector('#closeRelations'),
  sqlQuery: document.querySelector('#sqlQuery'),
  searchSql: document.querySelector('#searchSql'),
  sqlResults: document.querySelector('#sqlResults'),
  sqlReferences: document.querySelector('#sqlReferences'),
  viewDocumentation: document.querySelector('#viewDocumentation'),
  downloadDocumentation: document.querySelector('#downloadDocumentation')
};

let currentId = null;
let pollingGeneration = 0;

const terminalStatuses = new Set(['completed', 'failed', 'cancelled']);
const statusLabels = {
  pending: 'En attente',
  running: 'En cours',
  completed: 'Terminée',
  failed: 'Échec',
  cancelled: 'Annulée'
};
const metricLabels = {
  projects: 'Projets',
  files: 'Fichiers',
  classes: 'Classes',
  interfaces: 'Interfaces',
  methods: 'Méthodes',
  lines: 'Lignes de méthodes',
  maxComplexity: 'Complexité maximale',
  relations: 'Relations sémantiques',
  calls: 'Appels distincts',
  sqlObjects: 'Objets SQL',
  sqlReferences: 'Références SQL',
  endpoints: 'Endpoints API',
  packages: 'Packages NuGet'
};
const riskLabels = {
  low: 'Faible',
  medium: 'Modéré',
  high: 'Élevé',
  critical: 'Critique'
};

elements.form.addEventListener('submit', async event => {
  event.preventDefault();
  resetResults();
  setStatus("Ajout de l'analyse à la file…");
  elements.scanButton.disabled = true;

  try {
    const analysis = await api('/api/analyses', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ path: elements.path.value })
    });
    currentId = analysis.id;
    elements.progressCard.hidden = false;
    await followAnalysis(analysis.id);
  } catch (error) {
    setStatus(error.message, true);
    elements.scanButton.disabled = false;
  }
});

elements.cancel.addEventListener('click', async () => {
  if (!currentId) return;
  elements.cancel.disabled = true;
  try {
    await api(`/api/analyses/${currentId}/cancel`, { method: 'POST' });
    setStatus("Annulation demandée…");
  } catch (error) {
    setStatus(error.message, true);
    elements.cancel.disabled = false;
  }
});

elements.search.addEventListener('click', search);
elements.query.addEventListener('keydown', event => {
  if (event.key === 'Enter') search();
});
elements.refreshHistory.addEventListener('click', loadHistory);
elements.closeRelations.addEventListener('click', () => { elements.relationsCard.hidden = true; });
elements.results.addEventListener('click', event => {
  const button = event.target.closest('button[data-symbol-id]');
  if (button) showImpact('CodeSymbol', button.dataset.symbolId, button.dataset.symbolName);
});
elements.searchSql.addEventListener('click', loadSqlObjects);
elements.sqlQuery.addEventListener('keydown', event => {
  if (event.key === 'Enter') loadSqlObjects();
});
elements.sqlResults.addEventListener('click', event => {
  const button = event.target.closest('button[data-sql-object-id]');
  if (!button) return;
  if (button.dataset.sqlAction === 'impact')
    showImpact('SqlObject', button.dataset.sqlObjectId, button.dataset.sqlObjectName);
  else
    showSqlReferences(button.dataset.sqlObjectId, button.dataset.sqlObjectName);
});
elements.viewDocumentation.addEventListener('click', () => {
  if (currentId) window.open(`/api/analyses/${currentId}/documentation`, '_blank', 'noopener');
});
elements.downloadDocumentation.addEventListener('click', () => {
  if (!currentId) return;
  const link = document.createElement('a');
  link.href = `/api/analyses/${currentId}/documentation/export`;
  link.click();
});

elements.history.addEventListener('click', async event => {
  const button = event.target.closest('button[data-action]');
  if (!button) return;
  const id = button.dataset.id;
  if (button.dataset.action === 'open') await openAnalysis(id);
  if (button.dataset.action === 'delete') await deleteAnalysis(id);
});

async function followAnalysis(id) {
  const generation = ++pollingGeneration;
  while (generation === pollingGeneration) {
    const snapshot = await api(`/api/analyses/${id}/progress`);
    renderProgress(snapshot);
    const normalizedStatus = normalizeStatus(snapshot.status);

    if (terminalStatuses.has(normalizedStatus)) {
      elements.scanButton.disabled = false;
      elements.cancel.disabled = true;
      if (normalizedStatus === 'completed') {
        setStatus('Analyse terminée.');
        await loadAnalysis(id);
      } else {
        setStatus(snapshot.error || snapshot.message, normalizedStatus === 'failed');
      }
      await loadHistory();
      return;
    }

    await delay(500);
  }
}

async function openAnalysis(id) {
  pollingGeneration++;
  currentId = id;
  resetResults();
  try {
    const snapshot = await api(`/api/analyses/${id}/progress`);
    elements.progressCard.hidden = false;
    renderProgress(snapshot);
    const normalizedStatus = normalizeStatus(snapshot.status);
    if (normalizedStatus === 'completed') {
      await loadAnalysis(id);
      setStatus('Analyse chargée.');
    } else if (!terminalStatuses.has(normalizedStatus)) {
      elements.scanButton.disabled = true;
      await followAnalysis(id);
    } else {
      setStatus(snapshot.error || snapshot.message, normalizedStatus === 'failed');
    }
  } catch (error) {
    setStatus(error.message, true);
  }
}

async function loadAnalysis(id) {
  const analysis = await api(`/api/analyses/${id}`);
  currentId = id;
  renderProjects(analysis.projects || []);
  await renderDashboard(id);
  elements.query.disabled = false;
  elements.search.disabled = false;
  elements.sqlQuery.disabled = false;
  elements.searchSql.disabled = false;
  elements.viewDocumentation.disabled = false;
  elements.downloadDocumentation.disabled = false;
  await loadSqlObjects();
}

async function renderDashboard(id) {
  const dashboard = await api(`/api/analyses/${id}/dashboard`);
  elements.dashboard.innerHTML = Object.entries(dashboard)
    .map(([key, value]) => `<div class="metric"><strong>${Number(value).toLocaleString('fr-FR')}</strong><span>${escapeHtml(metricLabels[key] || key)}</span></div>`)
    .join('');
}

function renderProjects(projects) {
  elements.projects.className = '';
  elements.projects.innerHTML = projects.length
    ? projects.map(project => `
      <article class="project">
        <h3>${escapeHtml(project.name)} <small>${escapeHtml(project.targetFramework || '')}</small></h3>
        <p>${project.symbols.length} symbole(s) · ${project.references.length} référence(s) projet · ${project.packages.length} package(s)</p>
        ${project.packages.length ? `<details><summary>Packages NuGet</summary><ul>${project.packages
          .map(packageReference => `<li>${escapeHtml(packageReference.name)}${packageReference.version ? ` <small>${escapeHtml(packageReference.version)}</small>` : ''}</li>`)
          .join('')}</ul></details>` : ''}
      </article>`).join('')
    : '<p class="empty">Aucun projet .csproj détecté.</p>';
}

function renderProgress(snapshot) {
  const normalizedStatus = normalizeStatus(snapshot.status);
  elements.progressStage.textContent = statusLabels[normalizedStatus] || snapshot.stage;
  elements.progressMessage.textContent = snapshot.message;
  elements.progressError.textContent = snapshot.error || '';
  elements.cancel.disabled = normalizedStatus !== 'pending' && normalizedStatus !== 'running';

  const total = Number(snapshot.totalProjects || 0);
  const processed = Number(snapshot.projectsProcessed || 0);
  if (normalizedStatus === 'completed') {
    elements.progressBar.className = '';
    elements.progressBar.style.width = '100%';
  } else if (total > 0 && processed > 0) {
    elements.progressBar.className = '';
    elements.progressBar.style.width = `${Math.min(95, Math.round(processed / total * 100))}%`;
  } else {
    elements.progressBar.className = 'indeterminate';
    elements.progressBar.style.width = '';
  }

  elements.progressMetrics.innerHTML = [
    `${processed}/${total || '?'} projet(s)`,
    `${snapshot.filesProcessed || 0} fichier(s)`,
    `${snapshot.symbolsFound || 0} symbole(s)`,
    `${snapshot.warnings || 0} avertissement(s)`
  ].map(value => `<span>${escapeHtml(value)}</span>`).join('');
}

async function loadHistory() {
  try {
    const analyses = await api('/api/analyses');
    if (!analyses.length) {
      elements.history.className = 'empty';
      elements.history.textContent = 'Aucune analyse enregistrée.';
      return;
    }

    elements.history.className = '';
    elements.history.innerHTML = analyses.map(analysis => {
      const normalizedStatus = normalizeStatus(analysis.status);
      const folder = analysis.rootPath.split(/[\\/]/).filter(Boolean).pop() || analysis.rootPath;
      return `<article class="history-item history-actions">
        <div class="history-main" title="${escapeHtml(analysis.rootPath)}">
          <strong>${escapeHtml(folder)}</strong>
          <small>${formatDate(analysis.createdAt)}</small>
        </div>
        <span class="status-pill status-${escapeHtml(normalizedStatus)}">${escapeHtml(statusLabels[normalizedStatus] || normalizedStatus)}</span>
        <div>
          <button type="button" data-action="open" data-id="${analysis.id}">Ouvrir</button>
          <button type="button" class="secondary" data-action="delete" data-id="${analysis.id}">Supprimer</button>
        </div>
      </article>`;
    }).join('');
  } catch (error) {
    elements.history.className = 'error';
    elements.history.textContent = error.message;
  }
}

async function deleteAnalysis(id) {
  if (!window.confirm('Supprimer définitivement les résultats de cette analyse ?')) return;
  try {
    await api(`/api/analyses/${id}`, { method: 'DELETE' });
    if (currentId === id) {
      currentId = null;
      pollingGeneration++;
      elements.progressCard.hidden = true;
      resetResults();
    }
    await loadHistory();
  } catch (error) {
    setStatus(error.message, true);
  }
}

async function search() {
  if (!currentId || !elements.query.value.trim()) return;
  elements.results.innerHTML = '<p class="empty">Recherche…</p>';
  try {
    const query = encodeURIComponent(elements.query.value.trim());
    const [symbols, endpoints] = await Promise.all([
      api(`/api/analyses/${currentId}/search?q=${query}`),
      api(`/api/analyses/${currentId}/endpoints?q=${query}`)
    ]);
    const symbolResults = symbols.length
      ? `<h3>Symboles C#</h3>${symbols.map(symbol => `<div class="symbol symbol-row">
          <div>
            <strong>${escapeHtml(symbol.name)}</strong> · ${escapeHtml(symbol.kind)}
            ${symbol.container ? `<span class="container">dans ${escapeHtml(symbol.container)}</span>` : ''}<br>
            <small>${escapeHtml(symbol.filePath)}:${symbol.line}</small>
          </div>
          <button type="button" class="subtle" data-symbol-id="${symbol.id}" data-symbol-name="${escapeHtml(symbol.name)}">Voir l'impact</button>
        </div>`).join('')}`
      : '';
    const endpointResults = endpoints.length
      ? `<h3>Endpoints API</h3>${endpoints.map(endpoint => `<div class="endpoint symbol-row">
          <div><span class="http-method">${escapeHtml(endpoint.httpMethod)}</span> <strong>${escapeHtml(endpoint.route)}</strong><br>
            <small>${escapeHtml(endpoint.handlerDisplay)} · ${escapeHtml(endpoint.filePath)}:${endpoint.line} · confiance ${escapeHtml(normalizeStatus(endpoint.confidence))}</small>
          </div>
          ${endpoint.codeSymbolId ? `<button type="button" class="subtle" data-symbol-id="${endpoint.codeSymbolId}" data-symbol-name="${escapeHtml(endpoint.handlerDisplay)}">Voir l'impact</button>` : ''}
        </div>`).join('')}`
      : '';
    elements.results.innerHTML = symbolResults || endpointResults
      ? symbolResults + endpointResults
      : '<p class="empty">Aucun résultat.</p>';
  } catch (error) {
    elements.results.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

async function showImpact(kind, elementId, elementName) {
  if (!currentId || !elementId) return;
  elements.relationsCard.hidden = false;
  elements.relationsTitle.textContent = `Impact de ${elementName || 'cet élément'}`;
  elements.relations.innerHTML = '<p class="empty">Calcul de l’impact…</p>';
  elements.relationsCard.scrollIntoView({ behavior: 'smooth', block: 'start' });
  try {
    const report = await api(`/api/analyses/${currentId}/impact?kind=${encodeURIComponent(kind)}&elementId=${encodeURIComponent(elementId)}&depth=2`);
    const risk = normalizeStatus(report.risk);
    const reasons = (report.reasons || []).map(reason => `<li>${escapeHtml(reason)}</li>`).join('');
    const nodes = (report.nodes || []).map(node => `<article class="relation impact-node">
      <div>
        <span class="depth">Niveau ${node.depth}</span>
        <strong>${escapeHtml(node.name)}</strong>
        <span class="relation-kind">${escapeHtml(node.relationship)}</span>
      </div>
      <small>${escapeHtml(node.kind)} · confiance ${escapeHtml(normalizeStatus(node.confidence))}${node.filePath ? ` · ${escapeHtml(node.filePath)}` : ''}</small>
    </article>`).join('');
    elements.relations.innerHTML = `
      <div class="impact-summary">
        <span class="risk risk-${escapeHtml(risk)}">Risque ${escapeHtml(riskLabels[risk] || report.risk)} · score ${report.score}</span>
        <span>${report.projects.length} projet(s)</span>
        <span>${report.files.length} fichier(s)</span>
        <span>${report.tests.length} test(s)</span>
      </div>
      <ul class="impact-reasons">${reasons}</ul>
      ${report.truncated ? '<p class="warning">Résultat limité pour préserver la lisibilité.</p>' : ''}
      ${nodes || '<p class="empty">Aucune dépendance dans la profondeur demandée.</p>'}`;
  } catch (error) {
    elements.relations.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

async function loadSqlObjects() {
  if (!currentId) return;
  elements.sqlResults.className = '';
  elements.sqlResults.innerHTML = '<p class="empty">Chargement…</p>';
  elements.sqlReferences.hidden = true;
  try {
    const query = encodeURIComponent(elements.sqlQuery.value.trim());
    const sqlObjects = await api(`/api/analyses/${currentId}/sql?q=${query}`);
    elements.sqlResults.innerHTML = sqlObjects.length
      ? sqlObjects.map(sqlObject => `<article class="sql-object symbol-row">
          <div>
            <strong>${escapeHtml(sqlObject.name)}</strong> · ${escapeHtml(sqlObject.kind)}<br>
            <small>${escapeHtml(sqlObject.filePath)}:${sqlObject.line}</small>
          </div>
          <div class="result-actions">
            <button type="button" class="subtle" data-sql-object-id="${sqlObject.id}" data-sql-object-name="${escapeHtml(sqlObject.name)}" data-sql-action="references">Références</button>
            <button type="button" class="subtle" data-sql-object-id="${sqlObject.id}" data-sql-object-name="${escapeHtml(sqlObject.name)}" data-sql-action="impact">Impact</button>
          </div>
        </article>`).join('')
      : '<p class="empty">Aucun objet SQL détecté.</p>';
  } catch (error) {
    elements.sqlResults.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

async function showSqlReferences(objectId, objectName) {
  if (!currentId || !objectId) return;
  elements.sqlReferences.hidden = false;
  elements.sqlReferences.innerHTML = '<p class="empty">Chargement…</p>';
  try {
    const references = await api(`/api/analyses/${currentId}/sql-references?objectId=${encodeURIComponent(objectId)}`);
    elements.sqlReferences.innerHTML = `<h3>Références de ${escapeHtml(objectName)}</h3>` + (references.length
      ? references.map(reference => `<article class="relation">
          <div><strong>${escapeHtml(reference.sourceDisplay)}</strong>
            <span class="relation-kind">${escapeHtml(normalizeStatus(reference.operation))}</span>
            <strong>${escapeHtml(reference.targetDisplay)}</strong>
          </div>
          <small>confiance ${escapeHtml(normalizeStatus(reference.confidence))} · ${escapeHtml(reference.filePath)}:${reference.line}</small>
        </article>`).join('')
      : '<p class="empty">Aucune référence détectée.</p>');
  } catch (error) {
    elements.sqlReferences.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

function resetResults() {
  elements.dashboard.innerHTML = '';
  elements.projects.className = 'empty';
  elements.projects.textContent = 'Sélectionnez une analyse terminée.';
  elements.results.innerHTML = '';
  elements.relationsCard.hidden = true;
  elements.relations.innerHTML = '';
  elements.sqlResults.className = 'empty';
  elements.sqlResults.textContent = 'Sélectionnez une analyse terminée.';
  elements.sqlReferences.hidden = true;
  elements.sqlReferences.innerHTML = '';
  elements.query.disabled = true;
  elements.search.disabled = true;
  elements.sqlQuery.disabled = true;
  elements.searchSql.disabled = true;
  elements.viewDocumentation.disabled = true;
  elements.downloadDocumentation.disabled = true;
}

function setStatus(message, isError = false) {
  elements.status.textContent = message || '';
  elements.status.className = isError ? 'error' : '';
}

async function api(url, options) {
  const response = await fetch(url, options);
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('json') ? await response.json() : await response.text();
  if (!response.ok) throw new Error(body.error || body.title || body || `Erreur HTTP ${response.status}`);
  return body;
}

function normalizeStatus(status) { return String(status || '').toLowerCase(); }
function delay(milliseconds) { return new Promise(resolve => setTimeout(resolve, milliseconds)); }
function formatDate(value) { return new Date(value).toLocaleString('fr-FR'); }
function escapeHtml(value) {
  return String(value == null ? '' : value).replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;'
  })[character]);
}

loadHistory();
