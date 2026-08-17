import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { BrowserLlmClient, BrowserLlmTimeoutError, type BrowserProgress } from './browser-llm-client'
import { validateAdvisorySchema, type AdvisoryValidationIssue } from './advisory-schema'

type SourceId = 'cloud-api' | 'managed-local' | 'installed-local'
type CloudProvider = 'OpenAI' | 'GoogleGemini' | 'Mistral' | 'Grok'
type LogLevel = 'INFO' | 'WARN' | 'ERROR'
type BrowserModelTier = 'lightweight' | 'standard' | 'heavy' | 'experimental'

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
  browserModelId?: string | null
  browserOnly?: boolean
  browserVramRequiredMb?: number | null
  browserTier?: BrowserModelTier | null
  browserRecommended?: boolean
  browserWarning?: string | null
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
    fallbackUsed?: boolean
    promptChars?: number
    outputChars?: number
    maxTokens?: number
    temperature?: number
    schemaEnabled?: boolean
    coldStart?: boolean
    tokensPerSecond?: number | null
    modelTier?: BrowserModelTier | null
    wasCached?: boolean
    correlationId?: string | null
  }
}
type FlowLogEntry = { timestamp: string; level: LogLevel; message: string }
type DownloadToastState = {
  modelName: string
  state: string
  percentage: number
  bytesDownloaded: number
  totalBytes?: number | null
  message: string
  error?: string | null
}
type AdvisoryValidationState = { status: 'idle' | 'valid' | 'invalid'; message: string; issues?: AdvisoryValidationIssue[] }

const DEFAULT_SCHEMA = `{}`
const BROWSER_MAX_TOKENS = 512
const BROWSER_TEMPERATURE = 0.2

const SOURCE_OPTIONS: Array<{ id: SourceId; number: string; title: string; description: string }> = [
  { id: 'cloud-api', number: '01', title: 'Cloud API', description: 'Call a hosted provider such as OpenAI.' },
  { id: 'managed-local', number: '02', title: 'Browser WebLLM showcase', description: 'Demonstrate the harness with an LLM running entirely in the browser through WebGPU.' },
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

function formatBytes(value?: number | null) {
  if (!value) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let amount = value
  let unit = 0
  while (amount >= 1024 && unit < units.length - 1) {
    amount /= 1024
    unit += 1
  }
  return `${amount.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

const BROWSER_MODEL_FALLBACKS: Record<string, string> = {
  'smollm2-135m-instruct-q4km': 'SmolLM2-135M-Instruct-q0f32-MLC',
  'qwen2.5-0.5b-instruct-q4f16-browser': 'Qwen2.5-0.5B-Instruct-q4f16_1-MLC',
  'deepseek-r1-distill-qwen-7b-q4f16-browser': 'DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC',
  'gemma-3-1b-it-q4km': 'gemma3-1b-it-q4f16_1-MLC',
}

const BROWSER_MODEL_NAMES: Record<string, string> = {
  'smollm2-135m-instruct-q4km': 'SmolLM2 135M Instruct · WebLLM',
  'qwen3-0.6b-q4f16-browser': 'Qwen Small · Qwen3 0.6B · WebLLM',
  'qwen2.5-0.5b-instruct-q4f16-browser': 'Qwen Tiny · Qwen2.5 0.5B · WebLLM',
  'deepseek-r1-distill-qwen-7b-q4f16-browser': 'DeepSeek-R1 Distill Qwen 7B · WebLLM',
  'gemma-3-1b-it-q4km': 'Gemma 3 1B Instruct · WebLLM',
}

const BROWSER_MODEL_TIER_FALLBACKS: Record<string, BrowserModelTier> = {
  'smollm2-135m-instruct-q4km': 'lightweight',
  'qwen3-0.6b-q4f16-browser': 'lightweight',
  'qwen2.5-0.5b-instruct-q4f16-browser': 'lightweight',
  'deepseek-r1-distill-qwen-7b-q4f16-browser': 'heavy',
  'gemma-3-1b-it-q4km': 'standard',
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
  const [inputSchemaEnabled, setInputSchemaEnabled] = useState(false)
  const [inputSchema, setInputSchema] = useState(DEFAULT_SCHEMA)
  const [inputSchemaOpen, setInputSchemaOpen] = useState(false)
  const [inputValidation, setInputValidation] = useState<AdvisoryValidationState>({ status: 'idle', message: '' })
  const [timeoutMs, setTimeoutMs] = useState('10000')
  const [loading, setLoading] = useState(true)
  const [setupBusy, setSetupBusy] = useState(false)
  const [setupError, setSetupError] = useState('')
  const [result, setResult] = useState<CompletionResponse | null>(null)
  const [requestError, setRequestError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [flowLog, setFlowLog] = useState<FlowLogEntry[]>([])
  const [downloadToast, setDownloadToast] = useState<DownloadToastState | null>(null)
  const browserClientRef = useRef<BrowserLlmClient | null>(null)
  const setupLoadedRef = useRef(false)

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

  function validateInputAdvisory(request: unknown) {
    if (!inputSchemaEnabled) {
      setInputValidation({ status: 'idle', message: '' })
      return
    }

    let parsedSchema: unknown
    try {
      parsedSchema = JSON.parse(inputSchema)
      if (!parsedSchema || typeof parsedSchema !== 'object' || Array.isArray(parsedSchema)) throw new Error('The input schema root must be a JSON object.')
    } catch (error) {
      const message = error instanceof Error ? error.message : 'The input schema is invalid JSON.'
      setInputValidation({ status: 'invalid', message })
      log('WARN', `Advisory input validation skipped reason=invalid-schema message=${message}`)
      return
    }

    const issues = validateAdvisorySchema(request, parsedSchema)
    if (issues.length > 0) {
      const message = `Advisory input validation found ${issues.length} issue${issues.length === 1 ? '' : 's'}. The request will still be sent.`
      setInputValidation({ status: 'invalid', message, issues })
      log('WARN', `Advisory input validation failed issueCount=${issues.length} firstPath=${issues[0].path}`)
      return
    }

    setInputValidation({ status: 'valid', message: 'Advisory input validation passed. The request will be sent.' })
    log('INFO', 'Advisory input validation passed requestWillBeSent=true')
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
      const browserModels = nextModels.filter((item) => item.browserModelId ?? BROWSER_MODEL_FALLBACKS[item.id]).map((item) => {
        const browserModelId = item.browserModelId ?? BROWSER_MODEL_FALLBACKS[item.id]
        const browserLoaded = browserModelId ? browserClientRef.current?.isLoaded(browserModelId) : false
        return {
          ...item,
          name: BROWSER_MODEL_NAMES[item.id] ?? item.name,
          browserModelId,
          browserTier: item.browserTier ?? BROWSER_MODEL_TIER_FALLBACKS[item.id] ?? null,
          state: browserLoaded ? 'Downloaded' : 'NotDownloaded',
          bytesDownloaded: browserLoaded ? item.bytesDownloaded : 0,
          totalBytes: browserLoaded ? item.totalBytes : null,
          percentage: browserLoaded ? 100 : 0,
          runtimeRunning: Boolean(browserLoaded),
        }
      })
      setModels(browserModels)
      setInstalledSetup(nextInstalled)
      if (!managedModel && browserModels[0]) setManagedModel(browserModels[0].id)
      setInstalledEndpoint(nextInstalled.endpoint)
      setInstalledModel(nextInstalled.model)
      if (nextSources.some((item) => item.id === source)) log('INFO', `Setup loaded sources=${nextSources.length} browserModels=${browserModels.length}`)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Could not load setup.'
      setSetupError(message)
      log('ERROR', `Setup refresh failed message=${message}`)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (setupLoadedRef.current) return
    setupLoadedRef.current = true
    void loadSetup()
  }, [])
  useEffect(() => () => { browserClientRef.current?.dispose() }, [])

  const selectedSource = useMemo(() => sources.find((item) => item.id === source), [sources, source])
  const selectedManagedModel = useMemo(() => models.find((item) => item.id === managedModel), [models, managedModel])
  const apiReady = sources.length > 0 && !setupError

  function selectSource(next: SourceId) {
    setSource(next)
    setResult(null)
    setRequestError('')
    log('INFO', `Source selected id=${next}`)
  }

  function getBrowserClient() {
    if (!browserClientRef.current) browserClientRef.current = new BrowserLlmClient()
    return browserClientRef.current
  }

  function getBrowserModelId(model: ManagedModel) {
    return model.browserModelId ?? BROWSER_MODEL_FALLBACKS[model.id]
  }

  function updateBrowserModel(modelId: string, patch: Partial<ManagedModel>) {
    setModels((current) => current.map((item) => item.id === modelId ? { ...item, ...patch } : item))
  }

  function showDownloadToast(next: ManagedModel, message?: string) {
    setDownloadToast({
      modelName: next.name,
      state: next.state,
      percentage: next.percentage,
      bytesDownloaded: next.bytesDownloaded,
      totalBytes: next.totalBytes,
      message: message ?? (next.state === 'Downloaded' ? 'Browser model ready and cached.' : 'Downloading model into browser storage…'),
      error: next.error,
    })
  }

  async function loadBrowserModel(model: ManagedModel, action: 'download' | 'start' = 'download'): Promise<boolean> {
    const browserModelId = getBrowserModelId(model)
    if (!browserModelId) throw new Error('This managed model has no browser WebLLM mapping.')
    if (model.browserTier === 'heavy' && typeof window !== 'undefined') {
      const warning = model.browserWarning ?? 'This is a heavy browser model and may require several gigabytes of GPU memory.'
      if (!window.confirm(`${warning}\n\nContinue loading this model?`)) {
        log('INFO', `Browser WebLLM heavy model load canceled model=${model.id}`)
        return false
      }
    }
    if (typeof navigator === 'undefined' || !('gpu' in navigator)) {
      throw new Error('WebGPU is not available in this browser. Enable hardware acceleration or use a WebGPU-capable browser.')
    }
    const gpu = navigator.gpu as { requestAdapter: () => Promise<unknown> }
    const adapter = await gpu.requestAdapter()
    if (!adapter) {
      throw new Error('WebGPU is present but no compatible GPU adapter was found. Enable hardware acceleration or close other GPU-heavy tabs.')
    }

    const initial = { ...model, state: 'Downloading', percentage: 0, bytesDownloaded: 0, totalBytes: null, runtimeRunning: false }
    updateBrowserModel(model.id, initial)
    showDownloadToast(initial, action === 'start' ? 'Starting the browser runtime…' : 'Downloading the model into browser storage…')
    log('INFO', `Browser WebLLM load started model=${model.id} browserModel=${browserModelId}`)

    const onProgress = (progress: BrowserProgress) => {
      const next = {
        ...model,
        state: 'Downloading',
        percentage: progress.progress,
        bytesDownloaded: progress.downloadedMB ? progress.downloadedMB * 1024 * 1024 : 0,
        totalBytes: progress.totalMB ? progress.totalMB * 1024 * 1024 : null,
        runtimeRunning: false,
      }
      updateBrowserModel(model.id, next)
      showDownloadToast(next, progress.message)
    }

    await getBrowserClient().loadModel(browserModelId, onProgress)
    const ready = {
      ...model,
      state: 'Downloaded',
      percentage: 100,
      bytesDownloaded: model.bytesDownloaded,
      totalBytes: model.totalBytes,
      runtimeRunning: true,
    }
    updateBrowserModel(model.id, ready)
    showDownloadToast(ready, 'Browser model ready. Inference will run locally with WebGPU.')
    log('INFO', `Browser WebLLM load completed model=${model.id} browserModel=${browserModelId}`)
    window.setTimeout(() => setDownloadToast(null), 6000)
    return true
  }

  async function managedAction(action: 'download' | 'start' | 'stop') {
    if (action !== 'stop' && !managedModel) return
    setSetupBusy(true)
    setSetupError('')
    const selected = models.find((item) => item.id === managedModel)
    log('INFO', `Browser managed model action started action=${action.toUpperCase()} model=${managedModel}`)
    try {
      if (!selected) throw new Error('Select a managed browser model first.')
      if (action === 'stop') {
        await getBrowserClient().stop()
        const stopped = { ...selected, state: 'Downloaded', percentage: 100, runtimeRunning: false }
        updateBrowserModel(selected.id, stopped)
        showDownloadToast(stopped, 'Browser runtime stopped. Cached model files remain available.')
        window.setTimeout(() => setDownloadToast(null), 4000)
      } else {
        await loadBrowserModel(selected, action)
      }
      log('INFO', `Browser managed model action completed action=${action} model=${managedModel}`)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Managed model action failed.'
      setSetupError(message)
      setDownloadToast((current) => current ? { ...current, state: 'Failed', message: 'The browser runtime could not load this model.', error: message } : current)
      log('ERROR', `Browser managed model action failed action=${action} message=${message}`)
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
    const provider = source === 'cloud-api' ? cloudProvider : source === 'managed-local' ? 'BrowserWebLLM' : 'Ollama'
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
    const messages: Array<{ role: 'system' | 'user'; content: string }> = [
      ...(systemPrompt.trim() ? [{ role: 'system' as const, content: systemPrompt.trim() }] : []),
      { role: 'user', content: prompt.trim() },
    ]
    const payload = {
      provider, model: model.trim() || undefined, timeoutMs: numericTimeout,
      messages,
      ...(schemaEnabled ? { outputSchema: parsedSchema } : {}),
    }
    validateInputAdvisory({
      provider,
      model: model.trim() || null,
      timeoutMs: numericTimeout,
      messages,
    })
    setSubmitting(true)
    log('INFO', source === 'managed-local'
      ? `Completion request validated Browser WebLLM structuredOutput=${schemaEnabled}`
      : `Completion request validated POST /api/llm/complete structuredOutput=${schemaEnabled}`)
    let browserModelIdForLogging = managedModel
    let browserColdStart = false
    let browserWasCached = false
    let browserModelTier: BrowserModelTier = 'standard'
    try {
      if (source === 'managed-local') {
        const selected = models.find((item) => item.id === managedModel)
        if (!selected) throw new Error('Select a managed browser model first.')
        const browserModelId = getBrowserModelId(selected)
        if (!browserModelId) throw new Error('This managed model has no browser WebLLM mapping.')
        const coldStart = !getBrowserClient().isLoaded(browserModelId)
        browserModelIdForLogging = browserModelId
        browserColdStart = coldStart
        browserModelTier = selected.browserTier ?? 'standard'
        if (coldStart) {
          const loaded = await loadBrowserModel(selected, 'start')
          if (!loaded) {
            setRequestError('Browser model load canceled.')
            return
          }
        }
        const wasCached = getBrowserClient().wasCached(browserModelId)
        browserWasCached = wasCached
        const started = performance.now()
        const modelTier = browserModelTier
        const browserResponse = await getBrowserClient().complete(payload.messages, {
          responseSchema: schemaEnabled ? parsedSchema as Record<string, unknown> : undefined,
          timeoutMs: numericTimeout,
          maxTokens: BROWSER_MAX_TOKENS,
          temperature: BROWSER_TEMPERATURE,
          schemaEnabled,
          coldStart,
          modelTier,
        })
        const rawText = browserResponse.text?.trim() ?? ''
        if (browserResponse.message) log('WARN', `Browser WebLLM completion fallback model=${browserModelId} message=${browserResponse.message}`)
        let data: unknown = rawText
        try { data = JSON.parse(rawText.replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '').trim()) } catch { /* Preserve non-JSON model output as text. */ }
        const next: CompletionResponse = {
          success: true,
          data,
          metadata: {
            provider: 'BrowserWebLLM',
            model: browserModelId,
            attempts: 1,
            retryCount: 0,
            durationMs: browserResponse.durationMs ?? Math.round(performance.now() - started),
            timeoutMs: browserResponse.timeoutMs ?? numericTimeout,
            fallbackUsed: Boolean(browserResponse.message),
            promptChars: browserResponse.promptChars ?? payload.messages.reduce((total, item) => total + item.content.length, 0),
            outputChars: browserResponse.outputChars ?? rawText.length,
            maxTokens: browserResponse.maxTokens ?? BROWSER_MAX_TOKENS,
            temperature: browserResponse.temperature ?? BROWSER_TEMPERATURE,
            schemaEnabled: browserResponse.schemaEnabled ?? schemaEnabled,
            coldStart: browserResponse.coldStart ?? coldStart,
            tokensPerSecond: browserResponse.tokensPerSecond,
            modelTier: (browserResponse.modelTier as BrowserModelTier | undefined) ?? modelTier,
            wasCached: browserResponse.wasCached ?? wasCached,
            correlationId: crypto.randomUUID(),
          },
        }
        setResult(next)
        const tokensPerSecond = next.metadata?.tokensPerSecond != null ? next.metadata.tokensPerSecond.toFixed(2) : 'n/a'
        log('INFO', `Browser completion response received model=${browserModelId} durationMs=${next.metadata?.durationMs} promptChars=${next.metadata?.promptChars} outputChars=${next.metadata?.outputChars} maxTokens=${next.metadata?.maxTokens} temperature=${next.metadata?.temperature} schemaEnabled=${next.metadata?.schemaEnabled} coldStart=${next.metadata?.coldStart} tokensPerSecond=${tokensPerSecond} modelTier=${next.metadata?.modelTier} wasCached=${next.metadata?.wasCached} timeoutMs=${next.metadata?.timeoutMs}`)
        return
      }
      const { response, body } = await readJson('/api/llm/complete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      const next = (body ?? { success: false, error: { type: 'EmptyApiResponse', message: `The API returned HTTP ${response.status} without a response body.`, retryable: false } }) as CompletionResponse
      setResult(next)
      log(response.ok ? 'INFO' : 'WARN', `Completion response received HTTP ${response.status} success=${next.success} correlationId=${next.metadata?.correlationId ?? 'none'}`)
      if (!response.ok && next.error) setRequestError(next.error.message)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Could not reach the API.'
      if (source === 'managed-local' && error instanceof BrowserLlmTimeoutError) {
        const selected = models.find((item) => item.id === managedModel)
        const browserModelId = selected ? getBrowserModelId(selected) : managedModel
        const timeoutResponse: CompletionResponse = {
          success: false,
          error: { type: 'Timeout', message, retryable: false, code: error.code },
          metadata: {
            provider: 'BrowserWebLLM',
            model: browserModelId,
            attempts: 1,
            retryCount: 0,
            durationMs: numericTimeout,
            timeoutMs: numericTimeout,
            fallbackUsed: false,
            promptChars: payload.messages.reduce((total, item) => total + item.content.length, 0),
            outputChars: 0,
            maxTokens: BROWSER_MAX_TOKENS,
            temperature: BROWSER_TEMPERATURE,
            schemaEnabled,
            coldStart: browserColdStart,
            tokensPerSecond: null,
            modelTier: browserModelTier,
            wasCached: browserWasCached,
            correlationId: crypto.randomUUID(),
          },
        }
        setResult(timeoutResponse)
        setRequestError(message)
        log('WARN', `Browser WebLLM completion timed out model=${browserModelId} promptChars=${timeoutResponse.metadata?.promptChars} outputChars=0 maxTokens=${timeoutResponse.metadata?.maxTokens} temperature=${timeoutResponse.metadata?.temperature} schemaEnabled=${timeoutResponse.metadata?.schemaEnabled} coldStart=${timeoutResponse.metadata?.coldStart} tokensPerSecond=n/a modelTier=${timeoutResponse.metadata?.modelTier} wasCached=${timeoutResponse.metadata?.wasCached} timeoutMs=${timeoutResponse.metadata?.timeoutMs}`)
        return
      }
      setRequestError(message)
      if (source === 'managed-local') setDownloadToast((current) => current ? { ...current, state: 'Failed', message: 'The browser runtime could not complete the request.', error: message } : current)
      log('ERROR', `Completion request failed message=${message}`)
    } finally { setSubmitting(false) }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-lockup">
          <span className="eyebrow">LLM HARNESS / SETUP FIRST</span>
          <h1>Choose where your model lives.</h1>
          <p>Select one of the three supported paths, configure it, then use the same chat surface to test the call.</p>
          <div className="showcase-callout" role="note">
            <span className="showcase-label">OPTIONAL SHOWCASE PROVIDER</span>
            <p><strong>What if the harness ran entirely in the browser?</strong> WebLLM + WebGPU demonstrates that the same harness abstraction is not limited to cloud APIs. The reusable reliability layer remains backend-first.</p>
          </div>
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
              const browserReady = option.id === 'managed-local' && typeof navigator !== 'undefined' && 'gpu' in navigator
              const ready = option.id === 'managed-local' ? browserReady : status?.available
              return <button className={`source-card ${active ? 'selected' : ''}`} key={option.id} type="button" onClick={() => selectSource(option.id)}>
                <span className="source-number">{option.number}</span><span className="source-title">{option.title}</span><span className="source-description">{option.description}</span>
                <span className={`source-status ${ready ? 'ready' : status?.configured ? 'configured' : 'not-ready'}`}><span className="status-dot" />{option.id === 'managed-local' ? (browserReady ? 'WebGPU ready' : 'WebGPU unavailable') : status?.available ? 'Ready' : status?.configured ? 'Configured, not reachable' : 'Needs setup'}</span>
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
            <div className="active-source-line"><span className="pulse-dot" />{selectedSource?.name ?? SOURCE_OPTIONS.find((item) => item.id === source)?.title}<span className="provider-pill">{source === 'cloud-api' ? cloudProvider : source === 'managed-local' ? 'BrowserWebLLM' : 'Ollama'}</span></div>
            <div className="field-grid two-up">
              <label className="field"><span>Model</span>{source === 'managed-local' ? <select value={managedModel} onChange={(event) => setManagedModel(event.target.value)}>{models.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select> : <input value={source === 'cloud-api' ? cloudModel : installedModel} onChange={(event) => source === 'cloud-api' ? setCloudModel(event.target.value) : setInstalledModel(event.target.value)} />}</label>
              <label className="field"><span>Timeout <small>MILLISECONDS</small></span><input value={timeoutMs} onChange={(event) => setTimeoutMs(event.target.value)} inputMode="numeric" /></label>
            </div>
            <label className="field"><span>System instruction <small>OPTIONAL</small></span><input value={systemPrompt} onChange={(event) => setSystemPrompt(event.target.value)} /></label>
            <label className="field"><span>User prompt</span><textarea rows={4} value={prompt} onChange={(event) => setPrompt(event.target.value)} /></label>
            <div className="input-schema-bar"><div><span>Input validation <small>ADVISORY ONLY</small></span><p>Checks the request envelope without blocking the LLM call.</p></div><button className="secondary-button" type="button" onClick={() => setInputSchemaOpen(true)}>Configure</button><span className={`advisory-state advisory-state-${inputSchemaEnabled ? inputValidation.status : 'off'}`}>{inputSchemaEnabled ? inputValidation.status : 'off'}</span></div>
            {inputValidation.status !== 'idle' && <div className={`advisory-result advisory-result-${inputValidation.status}`}><strong>{inputValidation.message}</strong>{inputValidation.issues?.slice(0, 3).map((issue) => <span key={`${issue.path}-${issue.message}`}>{issue.path}: {issue.message}</span>)}</div>}
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
      {downloadToast && <DownloadToast toast={downloadToast} onDismiss={() => setDownloadToast(null)} />}
      {inputSchemaOpen && <InputSchemaDialog enabled={inputSchemaEnabled} schema={inputSchema} validation={inputValidation} onToggle={() => setInputSchemaEnabled((value) => !value)} onChange={setInputSchema} onValidate={() => validateInputAdvisory({ provider: source === 'cloud-api' ? cloudProvider : source === 'managed-local' ? 'BrowserWebLLM' : 'Ollama', model: source === 'cloud-api' ? cloudModel : source === 'managed-local' ? managedModel : installedModel, timeoutMs: Number(timeoutMs), messages: [{ role: 'system', content: systemPrompt }, { role: 'user', content: prompt }] })} onClose={() => setInputSchemaOpen(false)} />}
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
  const memory = model?.browserVramRequiredMb ? `~${Math.round(model.browserVramRequiredMb)} MB estimated GPU memory` : 'GPU memory depends on the browser and device'
  const tier = model?.browserTier ?? 'standard'
  return <div className="setup-card"><div className="setup-copy"><span className="section-index">BROWSER LOCAL MODEL</span><h2>Download and run in this browser</h2><p>The model is downloaded into browser storage and executed locally with WebGPU. The API is not used for model inference, and no prompt leaves this browser.</p></div><div className="setup-facts"><label className="field"><span>Browser model</span><select value={selectedId} onChange={(event) => setSelectedId(event.target.value)}>{models.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.creator} · {(item.browserTier ?? 'standard').toUpperCase()}</option>)}</select></label>{model && <><div className="model-state"><strong>{model.state}</strong><span>{model.percentage.toFixed(0)}% · {model.runtimeRunning ? 'browser runtime running' : 'browser runtime stopped'}</span></div><div className={`model-tier model-tier-${tier}`}><strong>{tier}</strong>{model.browserRecommended && <span>Recommended</span>}</div><p className="setup-reason">{model.error ?? model.description}</p><p className="setup-reason">{memory}</p>{model.browserWarning && <div className="model-warning">{model.browserWarning}</div>}</>}<div className="button-row"><button type="button" onClick={() => onAction('download')} disabled={busy || !selectedId}>{model?.state === 'Downloading' ? 'Downloading…' : 'Download to browser'}</button><button type="button" onClick={() => onAction('start')} disabled={busy || !selectedId}>Start browser runtime</button><button className="secondary-button" type="button" onClick={() => onAction('stop')} disabled={busy}>Stop</button></div></div></div>
}

function DownloadToast({ toast, onDismiss }: { toast: DownloadToastState; onDismiss: () => void }) {
  const failed = toast.state === 'Failed'
  const completed = toast.state === 'Downloaded'
  return <aside className={`download-toast ${failed ? 'is-failed' : completed ? 'is-complete' : ''}`} role="status" aria-live="polite">
    <div className="download-toast-heading"><div><span className="toast-kicker">MANAGED MODEL DOWNLOAD</span><strong>{toast.modelName}</strong></div><button type="button" onClick={onDismiss} aria-label="Close download status">×</button></div>
    <p>{toast.error ?? toast.message}</p>
    <div className="download-progress-meta"><span>{completed ? 'Verified' : failed ? 'Failed' : `${toast.percentage.toFixed(1)}%`}</span><span>{toast.totalBytes ? `${formatBytes(toast.bytesDownloaded)} / ${formatBytes(toast.totalBytes)}` : 'Browser cache'}</span></div>
    <div className="download-progress-track"><span style={{ width: `${Math.min(100, Math.max(0, toast.percentage))}%` }} /></div>
  </aside>
}

function InputSchemaDialog({ enabled, schema, validation, onToggle, onChange, onValidate, onClose }: { enabled: boolean; schema: string; validation: AdvisoryValidationState; onToggle: () => void; onChange: (value: string) => void; onValidate: () => void; onClose: () => void }) {
  return <div className="dialog-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}><section className="schema-dialog" role="dialog" aria-modal="true" aria-labelledby="input-schema-title"><div className="dialog-heading"><div><span className="toast-kicker">OPTIONAL REQUEST CHECK</span><h2 id="input-schema-title">Input schema validation</h2></div><button className="dialog-close" type="button" onClick={onClose} aria-label="Close input schema dialog">×</button></div><p className="dialog-copy">Define a flexible JSON Schema for the request envelope. This is advisory only: a mismatch is shown in the page and log, but the LLM request is still sent.</p><div className="dialog-toggle"><span>Enable advisory validation</span><button className={`toggle ${enabled ? 'is-on' : ''}`} type="button" onClick={onToggle}><span className="toggle-track"><span /></span>{enabled ? 'On' : 'Off'}</button></div><textarea className="code-input" rows={12} value={schema} onChange={(event) => onChange(event.target.value)} aria-label="Input JSON schema" /><div className="dialog-actions"><button className="secondary-button" type="button" onClick={onValidate}>Validate current request</button><button type="button" onClick={onClose}>Done</button></div>{validation.status !== 'idle' && <div className={`advisory-result advisory-result-${validation.status}`}><strong>{validation.message}</strong></div>}</section></div>
}

function InstalledSetup({ setup, endpoint, model, setEndpoint, setModel, busy, onSave }: { setup: InstalledSetup | null; endpoint: string; model: string; setEndpoint: (value: string) => void; setModel: (value: string) => void; busy: boolean; onSave: (testOnly?: boolean) => void }) {
  return <div className="setup-card"><div className="setup-copy"><span className="section-index">INSTALLED LOCAL LLM</span><h2>Connect to your local server</h2><p>Point the harness at an OpenAI-compatible endpoint. Ollama normally uses <code>http://127.0.0.1:11434/v1</code>; LM Studio can use its local server URL.</p></div><div className="setup-facts"><label className="field"><span>Base endpoint</span><input value={endpoint} onChange={(event) => setEndpoint(event.target.value)} /></label><label className="field"><span>Model name</span><input value={model} onChange={(event) => setModel(event.target.value)} /></label><div className="button-row"><button type="button" onClick={() => onSave(false)} disabled={busy}>Save & test</button><button className="secondary-button" type="button" onClick={() => onSave(true)} disabled={busy}>Test current settings</button></div>{setup && <p className={`setup-result ${setup.available ? 'ready-text' : ''}`}>{setup.available ? 'Server reachable and model endpoint accepted.' : setup.reason ?? 'Server is not reachable yet.'}</p>}</div></div>
}

function Fact({ label, value }: { label: string; value: string }) { return <div className="fact"><span>{label}</span><strong title={value}>{value}</strong></div> }

function ResultView({ result }: { result: CompletionResponse }) {
  const metadata = result.metadata
  return <>{result.error && <div className="result-error"><span>{result.error.type}</span><p>{result.error.message}</p></div>}{result.success && <div className="output-block"><div className="output-label"><span>DATA</span><span>STRUCTURED RESULT</span></div><pre>{formatData(result.data)}</pre></div>}<div className="metadata-grid"><Metric label="Provider" value={metadata?.provider ?? '—'} /><Metric label="Model" value={metadata?.model ?? '—'} /><Metric label="Duration" value={metadata?.durationMs != null ? `${metadata.durationMs} ms` : '—'} /><Metric label="Prompt" value={metadata?.promptChars != null ? `${metadata.promptChars} chars` : '—'} /><Metric label="Output" value={metadata?.outputChars != null ? `${metadata.outputChars} chars` : '—'} /><Metric label="Tokens/s" value={metadata?.tokensPerSecond != null ? metadata.tokensPerSecond.toFixed(2) : '—'} /><Metric label="Max tokens" value={String(metadata?.maxTokens ?? '—')} /><Metric label="Temperature" value={String(metadata?.temperature ?? '—')} /><Metric label="Schema" value={metadata?.schemaEnabled == null ? '—' : metadata.schemaEnabled ? 'enabled' : 'off'} /><Metric label="Cold start" value={metadata?.coldStart == null ? '—' : metadata.coldStart ? 'yes' : 'no'} /><Metric label="Cache" value={metadata?.wasCached == null ? '—' : metadata.wasCached ? 'hit' : 'miss'} /><Metric label="Tier" value={metadata?.modelTier ?? '—'} /><Metric label="Attempts" value={String(metadata?.attempts ?? '—')} /><Metric label="Timeout" value={metadata?.timeoutMs != null ? `${metadata.timeoutMs} ms` : '—'} /><Metric label="Correlation" value={metadata?.correlationId ?? '—'} /></div></>
}

function Metric({ label, value }: { label: string; value: string }) { return <div className="metric"><span>{label}</span><strong title={value}>{value}</strong></div> }

export default App
