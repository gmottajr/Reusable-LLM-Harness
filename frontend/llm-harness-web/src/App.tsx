import { useEffect, useMemo, useState, type FormEvent } from 'react'

type SourceId = 'cloud-api' | 'managed-local' | 'installed-local'
type CloudProvider = 'OpenAI' | 'GoogleGemini' | 'Mistral' | 'Grok'
type LogLevel = 'INFO' | 'WARN' | 'ERROR'

type SourceStatus = {
  id: SourceId
  name: string
  description: string
  provider: string
  configured: boolean
  available: boolean
  reason?: string | null
  endpoint?: string | null
  model?: string | null
}

type ManagedModel = {
  id: string
  name: string
  creator: string
  description: string
  sizeBytes?: number | null
  license: string
  state: string
  bytesDownloaded: number
  totalBytes?: number | null
  percentage: number
  error?: string | null
  runtimeRunning: boolean
}

type InstalledSetup = {
  endpoint: string
  model: string
  configured: boolean
  available: boolean
  reason?: string | null
}

type ApiError = { type: string; message: string; retryable: boolean; code?: string | null }
type CompletionResponse = {
  success: boolean
  data?: unknown
  error?: ApiError | null
  metadata?: {
    provider?: string | null
    model?: string | null
    attempts?: number
    retryCount?: number
    durationMs?: number | null
    timeoutMs?: number | null
    correlationId?: string | null
  }
}
type FlowLogEntry = { timestamp: string; level: LogLevel; message: string }

const DEFAULT_SCHEMA = `{}`

const SOURCE_OPTIONS: Array<{ id: SourceId; number: string; title: string; description: string }> = [
  { id: 'cloud-api', number: '01', title: 'Cloud API', description: 'Call a hosted provider such as OpenAI.' },
  { id: 'managed-local', number: '02', title: 'Download and manage', description: 'Download, verify, and run a curated local model.' },
  { id: 'installed-local', number: '03', title: 'Installed local LLM', description: 'Connect to Ollama, LM Studio, or another compatible server.' },
]

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const apiUrl = (path: string) => `${apiBaseUrl}${path}`

function formatData(data: unknown) {
  return typeof data === 'string' ? data : JSON.stringify(data, null, 2) ?? 'No data returned.'
}

function formatLog(entries: FlowLogEntry[]) {
  return entries.map((entry) => `${entry.timestamp} [${entry.level}] ${entry.message}`).join('\n')
}

async function readJson(path: string, init?: RequestInit) {
  const response = await fetch(apiUrl(path), init)
  const text = await response.text()
  let body: any = null
  if (text) {
    try { body = JSON.parse(text) } catch { body = null }
  }
  return { response, body }
}

function App() {
  const [source, setSource] = useState<SourceId>('cloud-api')
  const [sources, setSources] = useState<SourceStatus[]>([])
  const [models, setModels] = useState<ManagedModel[]>([])
  const [managedModel, setManagedModel] = useState('')
  const [installedEndpoint, setInstalledEndpoint] = useState('http://127.0.0.1:11434/v1')
  const [installedModel, setInstalledModel] = useState('llama3.2')
  const [installedSetup, setInstalledSetup] = useState<InstalledSetup | null>(null)
  const [cloudProvider, setCloudProvider] = useState<CloudProvider>('OpenAI')
  const [cloudModel, setCloudModel] = useState('gpt-4o-mini')
  const [systemPrompt, setSystemPrompt] = useState('Return JSON only.')
  const [prompt, setPrompt] = useState('Give me a name and role for a fictional engineer.')
  const [schemaEnabled, setSchemaEnabled] = useState(true)
  const [schema, setSchema] = useState(DEFAULT_SCHEMA)
  const [timeoutMs, setTimeoutMs] = useState('10000')
  const [loading, setLoading] = useState(true)
  const [setupBusy, setSetupBusy] = useState(false)
  const [setupError, setSetupError] = useState('')
  const [result, setResult] = useState<CompletionResponse | null>(null)
  const [requestError, setRequestError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [flowLog, setFlowLog] = useState<FlowLogEntry[]>([])

  function log(level: LogLevel, message: string) {
    setFlowLog((current) => [...current.slice(-249), { timestamp: new Date().toISOString(), level, message }])
  }

  function downloadLog() {
    const snapshot = [...flowLog, { timestamp: new Date().toISOString(), level: 'INFO' as const, message: 'Frontend log download requested.' }]
    const blob = new Blob([formatLog(snapshot)], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `llm-harness-frontend-${new Date().toISOString().replaceAll(':', '-')}.log`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function loadSetup() {
    setLoading(true)
    setSetupError('')
    log('INFO', 'Setup refresh started: GET /api/setup/sources, /api/models, /api/setup/installed-local')
    try {
      const [sourceResult, modelResult, installedResult] = await Promise.all([
        readJson('/api/setup/sources'),
        readJson('/api/models'),
        readJson('/api/setup/installed-local'),
      ])
      if (!sourceResult.response.ok) throw new Error(`Source setup returned HTTP ${sourceResult.response.status}.`)
      if (!modelResult.response.ok) throw new Error(`Managed model setup returned HTTP ${modelResult.response.status}.`)
      if (!installedResult.response.ok) throw new Error(`Installed LLM setup returned HTTP ${installedResult.response.status}.`)
      const nextSources = sourceResult.body as SourceStatus[]
      const nextModels = modelResult.body as ManagedModel[]
      const nextInstalled = installedResult.body as InstalledSetup
      setSources(nextSources)
      setModels(nextModels)
      setInstalledSetup(nextInstalled)
      if (!managedModel && nextModels[0]) setManagedModel(nextModels[0].id)
      setInstalledEndpoint(nextInstalled.endpoint)
      setInstalledModel(nextInstalled.model)
      if (nextSources.some((item) => item.id === source)) log('INFO', `Setup loaded sources=${nextSources.length} managedModels=${nextModels.length}`)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Could not load setup.'
      setSetupError(message)
      log('ERROR', `Setup refresh failed message=${message}`)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void loadSetup() }, [])

  const selectedSource = useMemo(() => sources.find((item) => item.id === source), [sources, source])
  const selectedManagedModel = useMemo(() => models.find((item) => item.id === managedModel), [models, managedModel])
  const apiReady = sources.length > 0 && !setupError

  function selectSource(next: SourceId) {
    setSource(next)
    setResult(null)
    setRequestError('')
    log('INFO', `Source selected id=${next}`)
  }

  async function managedAction(action: 'download' | 'start' | 'stop') {
    if (action !== 'stop' && !managedModel) return
    setSetupBusy(true)
    setSetupError('')
    const path = action === 'stop' ? '/api/models/stop' : `/api/models/${encodeURIComponent(managedModel)}/${action}`
    log('INFO', `Managed model action started ${action.toUpperCase()} ${path}`)
    try {
      const { response, body } = await readJson(path, { method: 'POST' })
      if (!response.ok) throw new Error(body?.error ?? `Managed model action returned HTTP ${response.status}.`)
      log('INFO', `Managed model action completed action=${action} state=${body?.state ?? 'unknown'}`)
      await loadSetup()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Managed model action failed.'
      setSetupError(message)
      log('ERROR', `Managed model action failed action=${action} message=${message}`)
    } finally { setSetupBusy(false) }
  }

  async function saveInstalled(testOnly = false) {
    setSetupBusy(true)
    setSetupError('')
    const path = testOnly ? '/api/setup/installed-local/test' : '/api/setup/installed-local'
    log('INFO', `${testOnly ? 'Installed LLM test' : 'Installed LLM configuration'} started ${testOnly ? 'POST' : 'PUT'} ${path}`)
    try {
      const init: RequestInit = testOnly ? { method: 'POST' } : {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ endpoint: installedEndpoint.trim(), model: installedModel.trim() }),
      }
      const { response, body } = await readJson(path, init)
      if (!response.ok) throw new Error(body?.error ?? `Installed LLM setup returned HTTP ${response.status}.`)
      setInstalledSetup(body as InstalledSetup)
      setInstalledEndpoint(body.endpoint)
      setInstalledModel(body.model)
      log('INFO', `Installed LLM setup completed available=${body.available} endpoint=${body.endpoint} model=${body.model}`)
      await loadSetup()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Installed LLM setup failed.'
      setSetupError(message)
      log('ERROR', `Installed LLM setup failed message=${message}`)
    } finally { setSetupBusy(false) }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setRequestError(''); setResult(null)
    const provider = source === 'cloud-api' ? cloudProvider : source === 'managed-local' ? 'LocalOpenAiCompatible' : 'Ollama'
    const model = source === 'cloud-api' ? cloudModel : source === 'managed-local' ? managedModel : installedModel
    log('INFO', `Run completion button pressed source=${source} provider=${provider} model=${model || '(default)'}`)
    if (!prompt.trim()) { setRequestError('Add a user prompt before submitting.'); log('WARN', 'Completion stopped validation=missing-prompt'); return }
    const numericTimeout = Number(timeoutMs)
    if (!Number.isInteger(numericTimeout) || numericTimeout <= 0) { setRequestError('Timeout must be a positive whole number of milliseconds.'); log('WARN', 'Completion stopped validation=invalid-timeout'); return }
    let parsedSchema: unknown
    if (schemaEnabled) {
      try {
        parsedSchema = JSON.parse(schema)
        if (!parsedSchema || typeof parsedSchema !== 'object' || Array.isArray(parsedSchema)) throw new Error('The output schema must be a JSON object.')
      } catch (error) {
        const message = error instanceof Error ? error.message : 'The output schema is invalid JSON.'
        setRequestError(message); log('WARN', `Completion stopped validation=invalid-schema message=${message}`); return
      }
    }
    const payload = {
      provider, model: model.trim() || undefined, timeoutMs: numericTimeout,
      messages: [...(systemPrompt.trim() ? [{ role: 'system', content: systemPrompt.trim() }] : []), { role: 'user', content: prompt.trim() }],
      ...(schemaEnabled ? { outputSchema: parsedSchema } : {}),
    }
    setSubmitting(true)
    log('INFO', `Completion request validated POST /api/llm/complete structuredOutput=${schemaEnabled}`)
    try {
      const { response, body } = await readJson('/api/llm/complete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      const next = (body ?? { success: false, error: { type: 'EmptyApiResponse', message: `The API returned HTTP ${response.status} without a response body.`, retryable: false } }) as CompletionResponse
      setResult(next)
      log(response.ok ? 'INFO' : 'WARN', `Completion response received HTTP ${response.status} success=${next.success} correlationId=${next.metadata?.correlationId ?? 'none'}`)
      if (!response.ok && next.error) setRequestError(next.error.message)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Could not reach the API.'
      setRequestError(message); log('ERROR', `Completion request failed message=${message}`)
    } finally { setSubmitting(false) }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-lockup">
          <span className="eyebrow">LLM HARNESS / SETUP FIRST</span>
          <h1>Choose where your model lives.</h1>
          <p>Select one of the three supported paths, configure it, then use the same chat surface to test the call.</p>
        </div>
        <div className="top-actions">
          <div className={`api-indicator ${apiReady ? '' : 'is-warning'}`}><span className="pulse-dot" /><span>{loading ? 'Checking API' : apiReady ? 'API connected' : 'API setup error'}</span></div>
          <button className="text-button" type="button" onClick={() => void loadSetup()} disabled={loading}>Refresh setup</button>
          <a className="api-docs-link" href={apiUrl('/')} target="_blank" rel="noreferrer">Swagger</a>
        </div>
      </header>

      <main>
        <section className="panel source-panel">
          <div className="panel-heading"><div><span className="section-index">01</span><h2>Choose an LLM source</h2></div><span className="heading-note">ONE ACTIVE PATH</span></div>
          <div className="source-grid">
            {SOURCE_OPTIONS.map((option) => {
              const status = sources.find((item) => item.id === option.id)
              const active = source === option.id
              return <button className={`source-card ${active ? 'selected' : ''}`} key={option.id} type="button" onClick={() => selectSource(option.id)}>
                <span className="source-number">{option.number}</span><span className="source-title">{option.title}</span><span className="source-description">{option.description}</span>
                <span className={`source-status ${status?.available ? 'ready' : status?.configured ? 'configured' : 'not-ready'}`}><span className="status-dot" />{status?.available ? 'Ready' : status?.configured ? 'Configured, not reachable' : 'Needs setup'}</span>
              </button>
            })}
          </div>
        </section>

        {setupError && <div className="inline-error">{setupError}</div>}
        <section className="setup-panel">
          {source === 'cloud-api' && <CloudSetup status={selectedSource} provider={cloudProvider} model={cloudModel} setModel={setCloudModel} onProviderChange={(next) => { setCloudProvider(next); setCloudModel(defaultCloudModel(next)) }} />}
          {source === 'managed-local' && <ManagedSetup model={selectedManagedModel} models={models} selectedId={managedModel} setSelectedId={setManagedModel} busy={setupBusy} onAction={managedAction} />}
          {source === 'installed-local' && <InstalledSetup setup={installedSetup} endpoint={installedEndpoint} model={installedModel} setEndpoint={setInstalledEndpoint} setModel={setInstalledModel} busy={setupBusy} onSave={saveInstalled} />}
        </section>

        <section className="workspace">
          <form className="panel control-panel" onSubmit={submit}>
            <div className="panel-heading"><div><span className="section-index">02</span><h2>Test the active source</h2></div><span className="heading-note">POST /API/LLM/COMPLETE</span></div>
            <div className="active-source-line"><span className="pulse-dot" />{selectedSource?.name ?? SOURCE_OPTIONS.find((item) => item.id === source)?.title}<span className="provider-pill">{source === 'cloud-api' ? cloudProvider : source === 'managed-local' ? 'LocalOpenAiCompatible' : 'Ollama'}</span></div>
            <div className="field-grid two-up">
              <label className="field"><span>Model</span>{source === 'managed-local' ? <select value={managedModel} onChange={(event) => setManagedModel(event.target.value)}>{models.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select> : <input value={source === 'cloud-api' ? cloudModel : installedModel} onChange={(event) => source === 'cloud-api' ? setCloudModel(event.target.value) : setInstalledModel(event.target.value)} />}</label>
              <label className="field"><span>Timeout <small>MILLISECONDS</small></span><input value={timeoutMs} onChange={(event) => setTimeoutMs(event.target.value)} inputMode="numeric" /></label>
            </div>
            <label className="field"><span>System instruction <small>OPTIONAL</small></span><input value={systemPrompt} onChange={(event) => setSystemPrompt(event.target.value)} /></label>
            <label className="field"><span>User prompt</span><textarea rows={4} value={prompt} onChange={(event) => setPrompt(event.target.value)} /></label>
            <div className="schema-heading"><span>Output schema <small>JSON SCHEMA SUBSET · {} ACCEPTS ANY JSON</small></span><button className={`toggle ${schemaEnabled ? 'is-on' : ''}`} type="button" onClick={() => setSchemaEnabled((value) => !value)}><span className="toggle-track"><span /></span>{schemaEnabled ? 'Structured JSON on' : 'Structured JSON off'}</button></div>
            {schemaEnabled && <textarea className="code-input schema-input" rows={8} value={schema} onChange={(event) => setSchema(event.target.value)} />}
            {requestError && <div className="inline-error">{requestError}</div>}
            <button className="submit-button" type="submit" disabled={submitting || loading}>{submitting ? 'Running completion…' : 'Run completion'}<span className="button-arrow">↗</span></button>
          </form>

          <div className="results-column">
            <section className="panel result-panel"><div className="panel-heading compact-heading"><div><span className="section-index">03</span><h2>Harness result</h2></div>{result && <span className={`result-badge ${result.success ? 'success' : 'failure'}`}>{result.success ? 'Success' : 'Failure'}</span>}</div>{!result ? <div className="empty-state"><div className="empty-mark">◎</div><p>Run a completion to inspect typed output, validation, and metadata.</p></div> : <ResultView result={result} />}</section>
            <section className="panel log-panel"><div className="panel-heading compact-heading"><div><span className="section-index">04</span><h2>Frontend flow log</h2></div><button className="text-button" type="button" onClick={downloadLog}>Download .log</button></div><pre className="flow-log">{flowLog.length ? formatLog(flowLog) : 'Press a button to see the frontend flow here.'}</pre><p className="log-note">The backend writes its matching request flow to <code>src/LlmHarness.Api/logs/llm-harness.log</code>.</p></section>
          </div>
        </section>
      </main>
      <footer className="footer-note"><span>ONE SOURCE SELECTED AT A TIME.</span><span>KEYS STAY ON THE BACKEND.</span><span>EVERY ACTION IS LOGGED.</span></footer>
    </div>
  )
}

function defaultCloudModel(provider: CloudProvider) {
  return provider === 'GoogleGemini' ? 'gemini-flash-latest' : provider === 'Mistral' ? 'mistral-small-latest' : provider === 'Grok' ? 'grok-4.5' : 'gpt-4o-mini'
}

function CloudSetup({ status, provider, model, setModel, onProviderChange }: { status?: SourceStatus; provider: CloudProvider; model: string; setModel: (value: string) => void; onProviderChange: (value: CloudProvider) => void }) {
  const endpoint = provider === 'GoogleGemini' ? 'https://generativelanguage.googleapis.com/v1beta' : provider === 'Mistral' ? 'https://api.mistral.ai/v1/chat/completions' : provider === 'Grok' ? 'https://api.x.ai/v1/chat/completions' : status?.endpoint ?? 'https://api.openai.com/v1/chat/completions'
  return <div className="setup-card"><div className="setup-copy"><span className="section-index">CLOUD API</span><h2>Use a hosted provider</h2><p>The API reads the provider key from backend User Secrets or environment variables. It is never sent from this browser.</p></div><div className="setup-facts"><label className="field"><span>Cloud provider</span><select value={provider} onChange={(event) => onProviderChange(event.target.value as CloudProvider)}><option value="OpenAI">OpenAI</option><option value="GoogleGemini">Google Gemini</option><option value="Mistral">Mistral</option><option value="Grok">Grok</option></select></label><Fact label="Status" value={status?.available ? 'At least one cloud provider is ready' : status?.reason ?? 'Configure a cloud provider key'} /><Fact label="Endpoint" value={endpoint} /><label className="field"><span>Model</span><input value={model} onChange={(event) => setModel(event.target.value)} /></label></div></div>
}

function ManagedSetup({ model, models, selectedId, setSelectedId, busy, onAction }: { model?: ManagedModel; models: ManagedModel[]; selectedId: string; setSelectedId: (value: string) => void; busy: boolean; onAction: (action: 'download' | 'start' | 'stop') => void }) {
  return <div className="setup-card"><div className="setup-copy"><span className="section-index">MANAGED LOCAL MODEL</span><h2>Download and run from here</h2><p>The backend owns the curated model files, verification, and local runtime. Nothing is downloaded by the browser.</p></div><div className="setup-facts"><label className="field"><span>Curated model</span><select value={selectedId} onChange={(event) => setSelectedId(event.target.value)}>{models.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.creator}</option>)}</select></label>{model && <><div className="model-state"><strong>{model.state}</strong><span>{model.percentage.toFixed(0)}% · {model.runtimeRunning ? 'runtime running' : 'runtime stopped'}</span></div><p className="setup-reason">{model.error ?? model.description}</p></>}<div className="button-row"><button type="button" onClick={() => onAction('download')} disabled={busy || !selectedId}>Download & verify</button><button type="button" onClick={() => onAction('start')} disabled={busy || !selectedId}>Start runtime</button><button className="secondary-button" type="button" onClick={() => onAction('stop')} disabled={busy}>Stop</button></div></div></div>
}

function InstalledSetup({ setup, endpoint, model, setEndpoint, setModel, busy, onSave }: { setup: InstalledSetup | null; endpoint: string; model: string; setEndpoint: (value: string) => void; setModel: (value: string) => void; busy: boolean; onSave: (testOnly?: boolean) => void }) {
  return <div className="setup-card"><div className="setup-copy"><span className="section-index">INSTALLED LOCAL LLM</span><h2>Connect to your local server</h2><p>Point the harness at an OpenAI-compatible endpoint. Ollama normally uses <code>http://127.0.0.1:11434/v1</code>; LM Studio can use its local server URL.</p></div><div className="setup-facts"><label className="field"><span>Base endpoint</span><input value={endpoint} onChange={(event) => setEndpoint(event.target.value)} /></label><label className="field"><span>Model name</span><input value={model} onChange={(event) => setModel(event.target.value)} /></label><div className="button-row"><button type="button" onClick={() => onSave(false)} disabled={busy}>Save & test</button><button className="secondary-button" type="button" onClick={() => onSave(true)} disabled={busy}>Test current settings</button></div>{setup && <p className={`setup-result ${setup.available ? 'ready-text' : ''}`}>{setup.available ? 'Server reachable and model endpoint accepted.' : setup.reason ?? 'Server is not reachable yet.'}</p>}</div></div>
}

function Fact({ label, value }: { label: string; value: string }) { return <div className="fact"><span>{label}</span><strong title={value}>{value}</strong></div> }

function ResultView({ result }: { result: CompletionResponse }) {
  return <>{result.error && <div className="result-error"><span>{result.error.type}</span><p>{result.error.message}</p></div>}{result.success && <div className="output-block"><div className="output-label"><span>DATA</span><span>STRUCTURED RESULT</span></div><pre>{formatData(result.data)}</pre></div>}<div className="metadata-grid"><Metric label="Provider" value={result.metadata?.provider ?? '—'} /><Metric label="Model" value={result.metadata?.model ?? '—'} /><Metric label="Duration" value={result.metadata?.durationMs != null ? `${result.metadata.durationMs} ms` : '—'} /><Metric label="Attempts" value={String(result.metadata?.attempts ?? '—')} /><Metric label="Timeout" value={result.metadata?.timeoutMs != null ? `${result.metadata.timeoutMs} ms` : '—'} /><Metric label="Correlation" value={result.metadata?.correlationId ?? '—'} /></div></>
}

function Metric({ label, value }: { label: string; value: string }) { return <div className="metric"><span>{label}</span><strong title={value}>{value}</strong></div> }

export default App
