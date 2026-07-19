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
  impactDepth: document.querySelector('#impactDepth'),
  sqlQuery: document.querySelector('#sqlQuery'),
  searchSql: document.querySelector('#searchSql'),
  sqlResults: document.querySelector('#sqlResults'),
  sqlReferences: document.querySelector('#sqlReferences'),
  ormQuery: document.querySelector('#ormQuery'),
  searchOrm: document.querySelector('#searchOrm'),
  ormResults: document.querySelector('#ormResults'),
  ormProperties: document.querySelector('#ormProperties'),
  fileQuery: document.querySelector('#fileQuery'),
  fileCategory: document.querySelector('#fileCategory'),
  searchFiles: document.querySelector('#searchFiles'),
  fileTree: document.querySelector('#fileTree'),
  fileProperties: document.querySelector('#fileProperties'),
  graphKind: document.querySelector('#graphKind'),
  edgeFilter: document.querySelector('#edgeFilter'),
  zoomOut: document.querySelector('#zoomOut'),
  zoomIn: document.querySelector('#zoomIn'),
  resetGraph: document.querySelector('#resetGraph'),
  exportGraph: document.querySelector('#exportGraph'),
  graphStatus: document.querySelector('#graphStatus'),
  dependencyGraph: document.querySelector('#dependencyGraph'),
  cobolResults: document.querySelector('#cobolResults'),
  compareFrom: document.querySelector('#compareFrom'),
  compareTo: document.querySelector('#compareTo'),
  compare: document.querySelector('#compare'),
  comparison: document.querySelector('#comparison'),
  diagnosticSeverity: document.querySelector('#diagnosticSeverity'),
  diagnostics: document.querySelector('#diagnostics'),
  viewDocumentation: document.querySelector('#viewDocumentation'),
  downloadDocumentation: document.querySelector('#downloadDocumentation'),
  downloadPdf: document.querySelector('#downloadPdf'),
  downloadSarif: document.querySelector('#downloadSarif')
};

let currentId = null;
let pollingGeneration = 0;
let currentAnalysis = null;
let currentFiles = [];
let completedAnalyses = [];
let graphData = null;
let graphTransform = { x: 0, y: 0, scale: 1 };
let graphPositions = new Map();
let currentImpact = null;

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
  packages: 'Packages NuGet',
  properties: 'Propriétés C#',
  sqlColumns: 'Colonnes SQL',
  cobolSymbols: 'Symboles COBOL',
  diagnostics: 'Diagnostics',
  ormMappings: 'Entités ORM'
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
elements.impactDepth.addEventListener('change', () => {
  if (currentImpact) showImpact(currentImpact.kind, currentImpact.elementId, currentImpact.elementName);
});
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
elements.searchOrm.addEventListener('click', loadOrmMappings);
elements.ormQuery.addEventListener('keydown', event => { if (event.key === 'Enter') loadOrmMappings(); });
elements.ormResults.addEventListener('click', event => {
  const button = event.target.closest('button[data-orm-action]');
  if (!button) return;
  if (button.dataset.ormAction === 'properties') showOrmProperties(button.dataset.mappingId, button.dataset.entityName);
  if (button.dataset.ormAction === 'entity-impact') showImpact('CodeSymbol', button.dataset.elementId, button.dataset.entityName);
  if (button.dataset.ormAction === 'table-impact') showImpact('SqlObject', button.dataset.elementId, button.dataset.tableName);
});
elements.searchFiles.addEventListener('click', loadFiles);
elements.fileQuery.addEventListener('keydown', event => { if (event.key === 'Enter') loadFiles(); });
elements.fileCategory.addEventListener('change', loadFiles);
elements.fileTree.addEventListener('click', event => {
  const button = event.target.closest('button[data-file-index]');
  if (button) showFileProperties(Number(button.dataset.fileIndex));
});
elements.graphKind.addEventListener('change', loadGraph);
elements.edgeFilter.addEventListener('change', renderGraph);
elements.zoomIn.addEventListener('click', () => zoomGraph(1.2));
elements.zoomOut.addEventListener('click', () => zoomGraph(1 / 1.2));
elements.resetGraph.addEventListener('click', resetGraphView);
elements.exportGraph.addEventListener('click', exportGraphPng);
elements.compare.addEventListener('click', compareAnalyses);
elements.diagnosticSeverity.addEventListener('change', loadDiagnostics);
elements.viewDocumentation.addEventListener('click', () => {
  if (currentId) window.open(`/api/analyses/${currentId}/documentation`, '_blank', 'noopener');
});
elements.downloadDocumentation.addEventListener('click', () => {
  if (!currentId) return;
  const link = document.createElement('a');
  link.href = `/api/analyses/${currentId}/documentation/export`;
  link.click();
});
elements.downloadPdf.addEventListener('click', () => downloadCurrent('pdf'));
elements.downloadSarif.addEventListener('click', () => downloadCurrent('sarif'));

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
  currentAnalysis = analysis;
  currentId = id;
  renderProjects(analysis.projects || []);
  await renderDashboard(id);
  elements.query.disabled = false;
  elements.search.disabled = false;
  elements.sqlQuery.disabled = false;
  elements.searchSql.disabled = false;
  elements.ormQuery.disabled = false;
  elements.searchOrm.disabled = false;
  elements.viewDocumentation.disabled = false;
  elements.downloadDocumentation.disabled = false;
  elements.downloadPdf.disabled = false;
  elements.downloadSarif.disabled = false;
  elements.fileQuery.disabled = false;
  elements.fileCategory.disabled = false;
  elements.searchFiles.disabled = false;
  elements.diagnosticSeverity.disabled = false;
  [elements.graphKind, elements.edgeFilter, elements.zoomOut, elements.zoomIn, elements.resetGraph, elements.exportGraph]
    .forEach(element => { element.disabled = false; });
  await Promise.all([loadSqlObjects(), loadOrmMappings(), loadFiles(), loadGraph(), loadCobol(), loadDiagnostics()]);
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
    completedAnalyses = analyses.filter(analysis => normalizeStatus(analysis.status) === 'completed');
    updateComparisonSelectors();
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
  currentImpact = { kind, elementId, elementName };
  elements.relationsCard.hidden = false;
  elements.relationsTitle.textContent = `Impact de ${elementName || 'cet élément'}`;
  elements.relations.innerHTML = '<p class="empty">Calcul de l’impact…</p>';
  elements.relationsCard.scrollIntoView({ behavior: 'smooth', block: 'start' });
  try {
    const depth = Number(elements.impactDepth.value || 2);
    const report = await api(`/api/analyses/${currentId}/impact?kind=${encodeURIComponent(kind)}&elementId=${encodeURIComponent(elementId)}&depth=${depth}`);
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
    const paths = (report.criticalPaths || []).map(path => `<article class="critical-path"><span>Profondeur ${path.depth} · confiance ${escapeHtml(normalizeStatus(path.confidence))}</span><strong>${path.names.map(escapeHtml).join(' → ')}</strong></article>`).join('');
    elements.relations.innerHTML = `
      <div class="impact-summary">
        <span class="risk risk-${escapeHtml(risk)}">Risque ${escapeHtml(riskLabels[risk] || report.risk)} · score ${report.score}</span>
        <span>${report.projects.length} projet(s)</span>
        <span>${report.files.length} fichier(s)</span>
        <span>${report.tests.length} test(s)</span>
      </div>
      <ul class="impact-reasons">${reasons}</ul>
      ${paths ? `<h3>Chemins critiques</h3><div class="critical-paths">${paths}</div>` : ''}
      ${report.truncated ? '<p class="warning">Résultat limité pour préserver la lisibilité.</p>' : ''}
      ${nodes || '<p class="empty">Aucune dépendance dans la profondeur demandée.</p>'}`;
  } catch (error) {
    elements.relations.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

async function loadDiagnostics() {
  if (!currentId) return;
  elements.diagnostics.innerHTML = '<p class="empty">Chargement…</p>';
  try {
    const severity = elements.diagnosticSeverity.value ? `?severity=${encodeURIComponent(elements.diagnosticSeverity.value)}` : '';
    const diagnostics = await api(`/api/analyses/${currentId}/diagnostics${severity}`);
    elements.diagnostics.className = '';
    elements.diagnostics.innerHTML = diagnostics.length ? diagnostics.map(item => `<article class="diagnostic diagnostic-${normalizeStatus(item.severity)}"><div><strong>${escapeHtml(item.code)}</strong><span>${escapeHtml(item.stage)}</span><span>${escapeHtml(item.severity)}</span></div><p>${escapeHtml(item.message)}</p>${item.filePath ? `<small>${escapeHtml(item.filePath)}${item.line ? `:${item.line}` : ''}</small>` : ''}</article>`).join('') : '<p class="empty">Aucun diagnostic pour ce filtre.</p>';
  } catch (error) { elements.diagnostics.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`; }
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
    const [references, columns] = await Promise.all([
      api(`/api/analyses/${currentId}/sql-references?objectId=${encodeURIComponent(objectId)}`),
      api(`/api/analyses/${currentId}/sql-columns?objectId=${encodeURIComponent(objectId)}`)
    ]);
    const columnTable = columns.length
      ? `<h3>Colonnes</h3><div class="table-scroll"><table><thead><tr><th>#</th><th>Nom</th><th>Type</th><th>Nullable</th></tr></thead><tbody>${columns.map(column => `<tr><td>${column.ordinal}</td><td><strong>${escapeHtml(column.name)}</strong></td><td>${escapeHtml(column.dataType || '')}</td><td>${column.isNullable == null ? '?' : column.isNullable ? 'oui' : 'non'}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="empty">Aucune colonne structurée détectée.</p>';
    elements.sqlReferences.innerHTML = columnTable + `<h3>Références de ${escapeHtml(objectName)}</h3>` + (references.length
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

async function loadOrmMappings() {
  if (!currentId) return;
  elements.ormResults.className = '';
  elements.ormResults.innerHTML = '<p class="empty">Chargement…</p>';
  elements.ormProperties.hidden = true;
  try {
    const query = encodeURIComponent(elements.ormQuery.value.trim());
    const mappings = await api(`/api/analyses/${currentId}/orm?q=${query}`);
    elements.ormResults.innerHTML = mappings.length ? mappings.map(mapping => `<article class="orm-mapping symbol-row"><div><strong>${escapeHtml(mapping.entityName)}</strong><span class="mapping-arrow">→</span><strong>${escapeHtml(mapping.tableName)}</strong><br><small>${escapeHtml(mapping.source)} · confiance ${escapeHtml(normalizeStatus(mapping.confidence))} · ${escapeHtml(mapping.filePath)}:${mapping.line}</small></div><div class="result-actions"><button type="button" class="subtle" data-orm-action="properties" data-mapping-id="${mapping.id}" data-entity-name="${escapeHtml(mapping.entityName)}">Propriétés</button>${mapping.codeSymbolId ? `<button type="button" class="subtle" data-orm-action="entity-impact" data-element-id="${mapping.codeSymbolId}" data-entity-name="${escapeHtml(mapping.entityName)}">Impact C#</button>` : ''}${mapping.sqlObjectId ? `<button type="button" class="subtle" data-orm-action="table-impact" data-element-id="${mapping.sqlObjectId}" data-table-name="${escapeHtml(mapping.tableName)}">Impact SQL</button>` : ''}</div></article>`).join('') : '<p class="empty">Aucune correspondance EF Core détectée.</p>';
  } catch (error) { elements.ormResults.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`; }
}

async function showOrmProperties(mappingId, entityName) {
  elements.ormProperties.hidden = false;
  elements.ormProperties.innerHTML = '<p class="empty">Chargement…</p>';
  try {
    const properties = await api(`/api/analyses/${currentId}/orm-properties?entityMappingId=${encodeURIComponent(mappingId)}`);
    elements.ormProperties.innerHTML = `<h3>Propriétés de ${escapeHtml(entityName)}</h3>` + (properties.length ? `<div class="table-scroll"><table><thead><tr><th>Propriété C#</th><th>Colonne SQL</th><th>Source</th><th>Confiance</th></tr></thead><tbody>${properties.map(property => `<tr><td>${escapeHtml(property.propertyName)}</td><td>${escapeHtml(property.columnName)}</td><td>${escapeHtml(property.source)}</td><td>${escapeHtml(normalizeStatus(property.confidence))}</td></tr>`).join('')}</tbody></table></div>` : '<p class="empty">Aucune propriété mappée.</p>');
  } catch (error) { elements.ormProperties.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`; }
}

async function loadFiles() {
  if (!currentId) return;
  elements.fileTree.className = 'file-tree';
  elements.fileTree.innerHTML = '<p class="empty">Inventaire…</p>';
  try {
    const query = encodeURIComponent(elements.fileQuery.value.trim());
    const category = elements.fileCategory.value ? `&category=${encodeURIComponent(elements.fileCategory.value)}` : '';
    currentFiles = await api(`/api/analyses/${currentId}/files?q=${query}${category}`);
    renderFileTree(currentFiles);
  } catch (error) {
    elements.fileTree.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`;
  }
}

function renderFileTree(files) {
  if (!files.length) { elements.fileTree.innerHTML = '<p class="empty">Aucun fichier.</p>'; return; }
  const root = {};
  files.forEach((file, index) => {
    let cursor = root;
    const parts = file.relativePath.split('/');
    parts.forEach((part, partIndex) => {
      cursor[part] = cursor[part] || { children: {}, index: null };
      if (partIndex === parts.length - 1) cursor[part].index = index;
      cursor = cursor[part].children;
    });
  });
  const branch = (node, depth) => Object.keys(node).sort((a, b) => a.localeCompare(b, 'fr')).map(name => {
    const item = node[name];
    const children = Object.keys(item.children);
    if (item.index != null) {
      const file = files[item.index];
      return `<button type="button" class="file-node" data-file-index="${item.index}" title="${escapeHtml(file.relativePath)}"><span>${escapeHtml(name)}</span><small>${escapeHtml(file.category)} · ${formatBytes(file.size)}</small></button>`;
    }
    return `<details ${depth === 0 ? 'open' : ''}><summary>${escapeHtml(name)} <small>${children.length}</small></summary><div class="tree-children">${branch(item.children, depth + 1)}</div></details>`;
  }).join('');
  elements.fileTree.innerHTML = branch(root, 0);
}

function showFileProperties(index) {
  const file = currentFiles[index];
  if (!file) return;
  const projects = currentAnalysis && currentAnalysis.projects || [];
  const symbols = projects.reduce((items, project) => items.concat((project.symbols || []).map(symbol => ({ project: project.name, symbol }))), [])
    .filter(item => normalizePath(item.symbol.filePath) === normalizePath(file.fullPath));
  elements.fileProperties.className = 'file-properties';
  elements.fileProperties.innerHTML = `<h3>${escapeHtml(file.relativePath)}</h3>
    <dl class="properties"><dt>Catégorie</dt><dd>${escapeHtml(file.category)}</dd><dt>Extension</dt><dd>${escapeHtml(file.extension || '—')}</dd><dt>Taille</dt><dd>${formatBytes(file.size)}</dd><dt>Lignes</dt><dd>${Number(file.lineCount).toLocaleString('fr-FR')}</dd><dt>Modifié</dt><dd>${formatDate(file.lastWriteUtc)}</dd><dt>SHA-256</dt><dd><code>${escapeHtml(file.sha256)}</code></dd></dl>
    <h3>Symboles et propriétés (${symbols.length})</h3>${symbols.length ? symbols.map(item => `<div class="symbol"><strong>${escapeHtml(item.symbol.name)}</strong> · ${escapeHtml(item.symbol.kind)}${item.symbol.returnType ? ` : <code>${escapeHtml(item.symbol.returnType)}</code>` : ''}<br><small>${escapeHtml(item.project)} · ligne ${item.symbol.line} · ${item.symbol.lineCount} ligne(s)</small></div>`).join('') : '<p class="empty">Aucun symbole C# associé.</p>'}`;
}

async function loadCobol() {
  if (!currentId) return;
  try {
    const values = await Promise.all([api(`/api/analyses/${currentId}/cobol?q=`), api(`/api/analyses/${currentId}/cobol-relations`)]);
    const symbols = values[0], relations = values[1];
    elements.cobolResults.className = '';
    elements.cobolResults.innerHTML = symbols.length ? `<div class="cobol-summary">${symbols.length} symbole(s) · ${relations.length} relation(s)</div>` + symbols.map(symbol => `<article class="symbol"><strong>${escapeHtml(symbol.name)}</strong> · ${escapeHtml(symbol.kind)}<br><small>${escapeHtml(symbol.filePath)}:${symbol.line}</small></article>`).join('') : '<p class="empty">Aucun source COBOL détecté.</p>';
  } catch (error) { elements.cobolResults.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`; }
}

async function loadGraph() {
  if (!currentId) return;
  elements.graphStatus.className = 'empty';
  elements.graphStatus.textContent = 'Construction du graphe…';
  elements.dependencyGraph.hidden = true;
  try {
    graphData = await api(`/api/analyses/${currentId}/graph?kind=${encodeURIComponent(elements.graphKind.value)}&limit=180`);
    graphPositions = new Map();
    const count = Math.max(1, graphData.nodes.length);
    graphData.nodes.forEach((node, index) => {
      const angle = index / count * Math.PI * 2;
      const ring = 190 + (index % 4) * 32;
      graphPositions.set(node.id, { x: 500 + Math.cos(angle) * ring, y: 310 + Math.sin(angle) * ring });
    });
    const kinds = Array.from(new Set(graphData.edges.map(edge => edge.kind))).sort();
    elements.edgeFilter.innerHTML = '<option value="">Toutes relations</option>' + kinds.map(kind => `<option>${escapeHtml(kind)}</option>`).join('');
    resetGraphView();
  } catch (error) {
    elements.graphStatus.className = 'error';
    elements.graphStatus.textContent = error.message;
  }
}

function renderGraph() {
  if (!graphData) return;
  if (!graphData.nodes.length) {
    elements.dependencyGraph.hidden = true;
    elements.graphStatus.hidden = false;
    elements.graphStatus.className = 'empty';
    elements.graphStatus.textContent = 'Aucun nœud pour ce graphe.';
    return;
  }
  const filter = elements.edgeFilter.value;
  const edges = graphData.edges.filter(edge => !filter || edge.kind === filter);
  elements.graphStatus.hidden = true;
  elements.dependencyGraph.hidden = false;
  const edgeMarkup = edges.map(edge => {
    const source = graphPositions.get(edge.source), target = graphPositions.get(edge.target);
    if (!source || !target) return '';
    return `<line class="graph-edge confidence-${normalizeStatus(edge.confidence)}" x1="${source.x}" y1="${source.y}" x2="${target.x}" y2="${target.y}" data-source="${edge.source}" data-target="${edge.target}" data-kind="${escapeHtml(edge.kind)}"><title>${escapeHtml(edge.kind)} · ${escapeHtml(edge.confidence)}</title></line>`;
  }).join('');
  const nodeMarkup = graphData.nodes.map(node => {
    const position = graphPositions.get(node.id), radius = Math.min(24, 8 + Math.log2(Math.max(1, node.weight)) * 2);
    return `<g class="graph-node kind-${normalizeStatus(node.kind)}" data-node-id="${node.id}" transform="translate(${position.x} ${position.y})"><circle r="${radius}"></circle><text x="${radius + 5}" y="4">${escapeHtml(shortLabel(node.label))}</text><title>${escapeHtml(node.label)} · ${escapeHtml(node.kind)}${node.filePath ? ` · ${escapeHtml(node.filePath)}` : ''}</title></g>`;
  }).join('');
  elements.dependencyGraph.innerHTML = `<defs><style>.graph-background{fill:#f8fafc}.graph-edge{stroke:#8ba0b4;stroke-width:1.4;marker-end:url(#arrow)}.confidence-probable{stroke-dasharray:6 4}.confidence-textual{stroke:#d08335;stroke-dasharray:3 4}.graph-node circle{fill:#1e8585;stroke:white;stroke-width:2}.graph-node text{fill:#25364b;font:650 11px sans-serif;paint-order:stroke;stroke:#f8fafc;stroke-width:3px}.kind-interface circle,.kind-view circle{fill:#5f67b8}.kind-method circle,.kind-procedure circle{fill:#b76c2d}.kind-program circle{fill:#674c9d}</style><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z"></path></marker></defs><rect class="graph-background" width="1000" height="620"></rect><g id="graphViewport" transform="translate(${graphTransform.x} ${graphTransform.y}) scale(${graphTransform.scale})"><g class="edges">${edgeMarkup}</g><g class="nodes">${nodeMarkup}</g></g>`;
  bindGraphInteractions();
}

function bindGraphInteractions() {
  const svg = elements.dependencyGraph;
  const background = svg.querySelector('.graph-background');
  let pan = null;
  background.addEventListener('pointerdown', event => { pan = { x: event.clientX, y: event.clientY, originX: graphTransform.x, originY: graphTransform.y }; background.setPointerCapture(event.pointerId); });
  background.addEventListener('pointermove', event => {
    if (!pan) return;
    const rect = svg.getBoundingClientRect();
    graphTransform.x = pan.originX + (event.clientX - pan.x) * 1000 / rect.width;
    graphTransform.y = pan.originY + (event.clientY - pan.y) * 620 / rect.height;
    applyGraphTransform();
  });
  background.addEventListener('pointerup', () => { pan = null; });
  svg.querySelectorAll('.graph-node').forEach(nodeElement => {
    let drag = null;
    nodeElement.addEventListener('pointerdown', event => { event.stopPropagation(); drag = svgPoint(event); nodeElement.setPointerCapture(event.pointerId); });
    nodeElement.addEventListener('pointermove', event => {
      if (!drag) return;
      const point = svgPoint(event), position = graphPositions.get(nodeElement.dataset.nodeId);
      position.x += (point.x - drag.x) / graphTransform.scale;
      position.y += (point.y - drag.y) / graphTransform.scale;
      drag = point;
      nodeElement.setAttribute('transform', `translate(${position.x} ${position.y})`);
      svg.querySelectorAll(`.graph-edge[data-source="${nodeElement.dataset.nodeId}"]`).forEach(edge => { edge.setAttribute('x1', position.x); edge.setAttribute('y1', position.y); });
      svg.querySelectorAll(`.graph-edge[data-target="${nodeElement.dataset.nodeId}"]`).forEach(edge => { edge.setAttribute('x2', position.x); edge.setAttribute('y2', position.y); });
    });
    nodeElement.addEventListener('pointerup', () => { drag = null; });
  });
  svg.onwheel = event => { event.preventDefault(); zoomGraph(event.deltaY < 0 ? 1.12 : 1 / 1.12); };
}

function svgPoint(event) {
  const rect = elements.dependencyGraph.getBoundingClientRect();
  return { x: (event.clientX - rect.left) * 1000 / rect.width, y: (event.clientY - rect.top) * 620 / rect.height };
}
function applyGraphTransform() { const viewport = elements.dependencyGraph.querySelector('#graphViewport'); if (viewport) viewport.setAttribute('transform', `translate(${graphTransform.x} ${graphTransform.y}) scale(${graphTransform.scale})`); }
function zoomGraph(factor) { graphTransform.scale = Math.max(.3, Math.min(4, graphTransform.scale * factor)); applyGraphTransform(); }
function resetGraphView() { graphTransform = { x: 0, y: 0, scale: 1 }; renderGraph(); }
function shortLabel(value) { return value.length > 42 ? value.slice(0, 39) + '…' : value; }

function exportGraphPng() {
  if (!graphData || elements.dependencyGraph.hidden) return;
  const clone = elements.dependencyGraph.cloneNode(true);
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg'); clone.removeAttribute('hidden');
  const source = new XMLSerializer().serializeToString(clone);
  const image = new Image(), canvas = document.createElement('canvas'); canvas.width = 1600; canvas.height = 992;
  image.onload = () => { const context = canvas.getContext('2d'); context.fillStyle = '#f8fafc'; context.fillRect(0, 0, canvas.width, canvas.height); context.drawImage(image, 0, 0, canvas.width, canvas.height); canvas.toBlob(blob => { const link = document.createElement('a'); link.href = URL.createObjectURL(blob); link.download = `CodeScope-${graphData.kind}.png`; link.click(); URL.revokeObjectURL(link.href); }); };
  image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
}

function updateComparisonSelectors() {
  const options = completedAnalyses.map(analysis => `<option value="${analysis.id}">${escapeHtml((analysis.rootPath.split(/[\\/]/).filter(Boolean).pop() || analysis.rootPath) + ' · ' + formatDate(analysis.createdAt))}</option>`).join('');
  elements.compareFrom.innerHTML = options; elements.compareTo.innerHTML = options;
  if (completedAnalyses.length > 1) elements.compareFrom.selectedIndex = 1;
  [elements.compareFrom, elements.compareTo, elements.compare].forEach(element => { element.disabled = completedAnalyses.length < 2; });
}

async function compareAnalyses() {
  const from = elements.compareFrom.value, to = elements.compareTo.value;
  if (!from || !to || from === to) { elements.comparison.innerHTML = '<p class="error">Choisissez deux analyses différentes.</p>'; return; }
  elements.comparison.innerHTML = '<p class="empty">Comparaison…</p>';
  try {
    const result = await api(`/api/analyses/compare?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`);
    const commit = value => value ? escapeHtml(value.slice(0, 12)) : 'hors Git';
    const group = (title, values, css) => `<section><h3>${title} (${values.length})</h3>${values.length ? values.slice(0, 250).map(item => `<div class="change ${css}"><strong>${escapeHtml(item.kind)}</strong> ${escapeHtml(item.key)}${item.filePath ? `<small>${escapeHtml(item.filePath)}</small>` : ''}</div>`).join('') : '<p class="empty">Aucun.</p>'}</section>`;
    const renames = (result.renamed || []).length ? `<section class="renames"><h3>Renommages (${result.renamed.length})</h3>${result.renamed.map(item => `<div class="change renamed"><strong>${escapeHtml(item.fromPath)}</strong> → ${escapeHtml(item.toPath)}</div>`).join('')}</section>` : '';
    elements.comparison.innerHTML = `<div class="comparison-summary"><span>${commit(result.fromCommit)} → ${commit(result.toCommit)}</span><strong>${result.unchangedFiles} fichier(s) inchangé(s)</strong></div>${renames}<div class="change-grid">${group('Ajouts', result.added, 'added')}${group('Modifications', result.modified, 'modified')}${group('Suppressions', result.removed, 'removed')}</div>`;
  } catch (error) { elements.comparison.innerHTML = `<p class="error">${escapeHtml(error.message)}</p>`; }
}

function downloadCurrent(format) {
  if (!currentId) return;
  const link = document.createElement('a'); link.href = `/api/analyses/${currentId}/export/${format}`; link.click();
}

function normalizePath(value) { return String(value || '').replace(/\\/g, '/').toLowerCase(); }
function formatBytes(value) { const bytes = Number(value || 0); if (bytes < 1024) return `${bytes} o`; if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} Ko`; return `${(bytes / 1024 / 1024).toFixed(1)} Mo`; }

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
  elements.ormResults.className = 'empty';
  elements.ormResults.textContent = 'Sélectionnez une analyse terminée.';
  elements.ormProperties.hidden = true;
  elements.ormProperties.innerHTML = '';
  elements.fileTree.className = 'file-tree empty';
  elements.fileTree.textContent = 'Sélectionnez une analyse terminée.';
  elements.fileProperties.className = 'file-properties empty';
  elements.fileProperties.textContent = 'Cliquez sur un fichier pour afficher ses propriétés et symboles.';
  elements.cobolResults.className = 'empty';
  elements.cobolResults.textContent = 'Sélectionnez une analyse terminée.';
  elements.dependencyGraph.hidden = true;
  elements.graphStatus.hidden = false;
  elements.graphStatus.textContent = 'Sélectionnez une analyse terminée.';
  elements.diagnostics.className = 'empty';
  elements.diagnostics.textContent = 'Sélectionnez une analyse terminée.';
  currentImpact = null;
  elements.query.disabled = true;
  elements.search.disabled = true;
  elements.sqlQuery.disabled = true;
  elements.searchSql.disabled = true;
  elements.ormQuery.disabled = true;
  elements.searchOrm.disabled = true;
  elements.fileQuery.disabled = true;
  elements.fileCategory.disabled = true;
  elements.searchFiles.disabled = true;
  elements.diagnosticSeverity.disabled = true;
  [elements.graphKind, elements.edgeFilter, elements.zoomOut, elements.zoomIn, elements.resetGraph, elements.exportGraph]
    .forEach(element => { element.disabled = true; });
  elements.viewDocumentation.disabled = true;
  elements.downloadDocumentation.disabled = true;
  elements.downloadPdf.disabled = true;
  elements.downloadSarif.disabled = true;
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
