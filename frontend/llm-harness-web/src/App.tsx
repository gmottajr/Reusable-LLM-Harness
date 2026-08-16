import { useEffect, useMemo, useState, type FormEvent } from 'react'

type ProviderStatus = {
  provider: string
  available: boolean
  reason?: string | null
}

type ApiError = {
  type: string
  message: string
  retryable: boolean
  code?: string | null
}

type ApiMetadata = {
  provider?: string | null
  model?: string | null
  attempts?: number
  retryCount?: number
  durationMs?: number | null
  timeoutMs?: number | null
  fallbackUsed?: boolean
  correlationId?: string | null
}

type CompletionResponse = {
  success: boolean
  data?: unknown
  error?: ApiError | null
  metadata?: ApiMetadata
}

const DEFAULT_SCHEMA = `{
  "type": "object",
  "required": ["name", "role"],
  "properties": {
    "name": { "type": "string" },
    "role": { "type": "string" }
  }
}`

const PROVIDERS = [
  { value: 'OpenAI', label: 'OpenAI', note: 'Cloud' },
  { value: 'Ollama', label: 'Ollama', note: 'Local' },
  { value: 'LocalOpenAiCompatible', label: 'Local compatible', note: 'Local' },
  { value: 'Anthropic', label: 'Anthropic', note: 'Cloud' },
]

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

function apiUrl(path: string) {
  return `${apiBaseUrl}${path}`
}

function formatData(data: unknown) {
  if (typeof data === 'string') {
    return data
  }

  return JSON.stringify(data, null, 2) ?? 'No data returned.'
}

function App() {
  const [provider, setProvider] = useState('OpenAI')
  const [model, setModel] = useState('gpt-4.1-mini')
  const [systemPrompt, setSystemPrompt] = useState('Return JSON only.')
  const [prompt, setPrompt] = useState('Give me a name and role for a fictional engineer.')
  const [schemaEnabled, setSchemaEnabled] = useState(true)
  const [schema, setSchema] = useState(DEFAULT_SCHEMA)
  const [timeoutMs, setTimeoutMs] = useState('10000')
  const [statuses, setStatuses] = useState<ProviderStatus[]>([])
  const [statusLoading, setStatusLoading] = useState(true)
  const [statusError, setStatusError] = useState('')
  const [result, setResult] = useState<CompletionResponse | null>(null)
  const [requestError, setRequestError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const selectedStatus = useMemo(
    () => statuses.find((item) => item.provider === provider),
    [provider, statuses],
  )

  async function loadStatuses() {
    setStatusLoading(true)
    setStatusError('')

    try {
      const response = await fetch(apiUrl('/api/providers/status'))
      if (!response.ok) {
        throw new Error(`Provider status returned HTTP ${response.status}.`)
      }

      setStatuses((await response.json()) as ProviderStatus[])
    } catch (error) {
      setStatusError(error instanceof Error ? error.message : 'Could not reach the API.')
    } finally {
      setStatusLoading(false)
    }
  }

  useEffect(() => {
    void loadStatuses()
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setRequestError('')
    setResult(null)

    if (!prompt.trim()) {
      setRequestError('Add a user prompt before submitting.')
      return
    }

    let parsedSchema: unknown
    if (schemaEnabled) {
      try {
        parsedSchema = JSON.parse(schema)
        if (parsedSchema === null || typeof parsedSchema !== 'object' || Array.isArray(parsedSchema)) {
          throw new Error('The output schema must be a JSON object.')
        }
      } catch (error) {
        setRequestError(error instanceof Error ? error.message : 'The output schema is invalid JSON.')
        return
      }
    }

    const numericTimeout = Number(timeoutMs)
    if (!Number.isInteger(numericTimeout) || numericTimeout <= 0) {
      setRequestError('Timeout must be a positive whole number of milliseconds.')
      return
    }

    const messages = [
      ...(systemPrompt.trim() ? [{ role: 'system', content: systemPrompt.trim() }] : []),
      { role: 'user', content: prompt.trim() },
    ]

    const payload = {
      provider,
      model: model.trim() || undefined,
      timeoutMs: numericTimeout,
      messages,
      ...(schemaEnabled ? { outputSchema: parsedSchema } : {}),
    }

    setSubmitting(true)
    try {
      const response = await fetch(apiUrl('/api/llm/complete'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      const body = (await response.json()) as CompletionResponse
      setResult(body)
      if (!response.ok && body.error) {
        setRequestError(body.error.message)
      }
    } catch (error) {
      setRequestError(error instanceof Error ? error.message : 'Could not reach the API.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-lockup">
          <span className="eyebrow">LLM HARNESS / PLAYGROUND</span>
          <h1>Make the model call legible.</h1>
          <p>One small surface for testing provider choice, structured output, and runtime behavior.</p>
        </div>
        <div className="api-indicator">
          <span className="pulse-dot" />
          <span>{statusError ? 'API disconnected' : 'API connected'}</span>
        </div>
      </header>

      <main className="workspace">
        <section className="panel control-panel">
          <div className="panel-heading">
            <div>
              <span className="section-index">01</span>
              <h2>Compose request</h2>
            </div>
            <span className="heading-note">POST /api/llm/complete</span>
          </div>

          <form onSubmit={submit}>
            <div className="field-grid two-up">
              <label className="field">
                <span>Provider</span>
                <select value={provider} onChange={(event) => setProvider(event.target.value)}>
                  {PROVIDERS.map((item) => (
                    <option key={item.value} value={item.value}>
                      {item.label} · {item.note}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Model</span>
                <input value={model} onChange={(event) => setModel(event.target.value)} placeholder="gpt-4.1-mini" />
              </label>
            </div>

            <label className="field">
              <span>System instruction <small>optional</small></span>
              <input value={systemPrompt} onChange={(event) => setSystemPrompt(event.target.value)} />
            </label>

            <label className="field">
              <span>User prompt</span>
              <textarea
                value={prompt}
                onChange={(event) => setPrompt(event.target.value)}
                rows={5}
                placeholder="Ask the harness to do something useful..."
              />
            </label>

            <div className="field-grid two-up compact-fields">
              <label className="field">
                <span>Timeout <small>milliseconds</small></span>
                <input type="number" min="1" step="1" value={timeoutMs} onChange={(event) => setTimeoutMs(event.target.value)} />
              </label>
              <div className="field field-toggle">
                <span>Response mode</span>
                <button
                  type="button"
                  className={`toggle ${schemaEnabled ? 'is-on' : ''}`}
                  onClick={() => setSchemaEnabled((value) => !value)}
                  aria-pressed={schemaEnabled}
                >
                  <span className="toggle-track"><span /></span>
                  <span>{schemaEnabled ? 'Structured JSON' : 'Raw text'}</span>
                </button>
              </div>
            </div>

            {schemaEnabled && (
              <label className="field">
                <span>Output schema <small>JSON Schema subset</small></span>
                <textarea className="code-input" value={schema} onChange={(event) => setSchema(event.target.value)} rows={9} spellCheck={false} />
              </label>
            )}

            {requestError && <div className="inline-error" role="alert">{requestError}</div>}

            <button className="submit-button" type="submit" disabled={submitting}>
              <span>{submitting ? 'Running harness…' : 'Run completion'}</span>
              <span className="button-arrow">↗</span>
            </button>
          </form>
        </section>

        <section className="results-column">
          <div className="panel status-panel">
            <div className="panel-heading compact-heading">
              <div>
                <span className="section-index">02</span>
                <h2>Provider status</h2>
              </div>
              <button className="text-button" type="button" onClick={() => void loadStatuses()} disabled={statusLoading}>
                {statusLoading ? 'Checking…' : 'Refresh'}
              </button>
            </div>
            {statusError ? (
              <p className="muted status-message">{statusError}. Start the API on port 5000, then refresh.</p>
            ) : (
              <div className="status-list">
                {PROVIDERS.map((item) => {
                  const status = statuses.find((entry) => entry.provider === item.value)
                  return (
                    <div className={`provider-row ${provider === item.value ? 'selected' : ''}`} key={item.value}>
                      <span className={`status-dot ${status?.available ? 'available' : 'unavailable'}`} />
                      <span className="provider-name">{item.label}</span>
                      <span className="provider-state">
                        {statusLoading ? 'checking' : status?.available ? 'available' : status?.reason ?? 'not registered'}
                      </span>
                    </div>
                  )
                })}
              </div>
            )}
            {selectedStatus && !selectedStatus.available && !statusError && (
              <p className="status-footnote">Selected provider is unavailable; the API will return a structured result.</p>
            )}
          </div>

          <div className="panel result-panel">
            <div className="panel-heading">
              <div>
                <span className="section-index">03</span>
                <h2>Harness result</h2>
              </div>
              {result && <span className={`result-badge ${result.success ? 'success' : 'failure'}`}>{result.success ? 'success' : 'failure'}</span>}
            </div>

            {!result ? (
              <div className="empty-state">
                <div className="empty-mark">◎</div>
                <p>Run a completion to inspect typed output, validation, and metadata.</p>
              </div>
            ) : (
              <>
                <div className="metadata-grid">
                  <Metric label="Provider" value={result.metadata?.provider ?? '—'} />
                  <Metric label="Model" value={result.metadata?.model ?? '—'} />
                  <Metric label="Duration" value={result.metadata?.durationMs != null ? `${result.metadata.durationMs} ms` : '—'} />
                  <Metric label="Attempts" value={String(result.metadata?.attempts ?? 0)} />
                  <Metric label="Retries" value={String(result.metadata?.retryCount ?? 0)} />
                  <Metric label="Fallback" value={result.metadata?.fallbackUsed ? 'Used' : 'No'} />
                </div>
                {result.error && (
                  <div className="result-error">
                    <span>{result.error.type}</span>
                    <p>{result.error.message}</p>
                  </div>
                )}
                {result.success && (
                  <div className="output-block">
                    <div className="output-label"><span>Data</span><span>typed response</span></div>
                    <pre>{formatData(result.data)}</pre>
                  </div>
                )}
                {result.metadata?.correlationId && (
                  <p className="correlation">Correlation ID <code>{result.metadata.correlationId}</code></p>
                )}
              </>
            )}
          </div>
        </section>
      </main>

      <footer className="footer-note">
        <span>Core stays provider-agnostic.</span>
        <span>Keys stay on the backend.</span>
        <span>Results stay structured.</span>
      </footer>
    </div>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  )
}

export default App
