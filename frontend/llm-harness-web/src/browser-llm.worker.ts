import { CreateMLCEngine, hasModelInCache, prebuiltAppConfig } from '@mlc-ai/web-llm'

const GEMMA_MODEL_ID = 'gemma3-1b-it-q4f16_1-MLC'
const browserAppConfig = {
  ...prebuiltAppConfig,
  model_list: prebuiltAppConfig.model_list.map((record) => record.model_id === GEMMA_MODEL_ID
    ? {
        ...record,
        overrides: {
          ...record.overrides,
          context_window_size: 4096,
          sliding_window_size: -1,
        },
      }
    : record),
}

type WorkerMessage = {
  id: string
  type: 'load' | 'complete' | 'stop' | 'interrupt'
  modelId?: string
  messages?: Array<{ role: 'system' | 'user' | 'assistant'; content: string }>
  temperature?: number
  maxTokens?: number
  responseSchema?: Record<string, unknown>
  schemaEnabled?: boolean
  coldStart?: boolean
  modelTier?: string
  timeoutMs?: number
}

type WorkerResponse = {
  id: string
  status: 'progress' | 'ready' | 'result' | 'error' | 'stopped'
  progress?: number
  message?: string
  downloadedMB?: number
  totalMB?: number
  text?: string
  durationMs?: number
  promptChars?: number
  outputChars?: number
  maxTokens?: number
  temperature?: number
  schemaEnabled?: boolean
  coldStart?: boolean
  tokensPerSecond?: number
  modelTier?: string
  wasCached?: boolean
  timeoutMs?: number
  error?: string
}

let engine: Awaited<ReturnType<typeof CreateMLCEngine>> | null = null
let currentModelId: string | null = null
let currentModelWasCached = false

function post(response: WorkerResponse) {
  self.postMessage(response)
}

async function loadModel(message: WorkerMessage) {
  if (!message.modelId) throw new Error('A WebLLM model ID is required.')
  if (engine && currentModelId === message.modelId) {
    currentModelWasCached = true
    post({ id: message.id, status: 'ready', progress: 100, message: 'Browser model already loaded.', wasCached: true })
    return
  }

  const wasCached = await hasModelInCache(message.modelId, browserAppConfig)

  if (engine) {
    await engine.unload()
    engine = null
    currentModelId = null
  }

  engine = await CreateMLCEngine(message.modelId, {
    appConfig: browserAppConfig,
    initProgressCallback: (info) => {
      post({
        id: message.id,
        status: 'progress',
        progress: Math.max(0, Math.min(100, Math.round((info.progress ?? 0) * 100))),
        message: info.text ?? 'Downloading and loading browser model…',
      })
    },
  })
  currentModelId = message.modelId
  currentModelWasCached = wasCached
  post({ id: message.id, status: 'ready', progress: 100, message: 'Browser model ready.', wasCached })
}

async function complete(message: WorkerMessage) {
  if (!engine || !currentModelId) throw new Error('Load a browser model before running a completion.')
  const started = performance.now()
  const temperature = message.temperature ?? 0.2
  const maxTokens = message.maxTokens ?? 512
  const schemaEnabled = message.schemaEnabled ?? Boolean(message.responseSchema && Object.keys(message.responseSchema).length > 0)
  const promptChars = (message.messages ?? []).reduce((total, item) => total + item.content.length, 0)
  const request = {
    messages: message.messages ?? [],
    temperature,
    max_tokens: maxTokens,
    ...(schemaEnabled && message.responseSchema ? {
      response_format: { type: 'json_object' as const, schema: JSON.stringify(message.responseSchema) },
    } : {}),
  }
  let warning: string | undefined
  let response
  try {
    response = await engine.chat.completions.create(request)
  } catch (error) {
    if (!message.responseSchema) throw error
    warning = 'The browser model did not support the requested structured-output constraint; returned its raw response instead.'
    try {
      response = await engine.chat.completions.create({
        messages: message.messages ?? [],
        temperature,
        max_tokens: maxTokens,
      })
    } catch (fallbackError) {
      const firstMessage = error instanceof Error ? error.message : 'structured-output request failed'
      const secondMessage = fallbackError instanceof Error ? fallbackError.message : 'raw-response request failed'
      throw new Error(`WebLLM completion failed. Structured output: ${firstMessage}. Raw fallback: ${secondMessage}`)
    }
  }
  const text = response.choices[0]?.message?.content ?? ''
  const durationMs = Math.round(performance.now() - started)
  const completionTokens = response.usage?.completion_tokens
  const nativeTokensPerSecond = response.usage?.extra?.decode_tokens_per_s
  const tokensPerSecond = typeof nativeTokensPerSecond === 'number' && Number.isFinite(nativeTokensPerSecond)
    ? nativeTokensPerSecond
    : typeof completionTokens === 'number' && durationMs > 0
      ? completionTokens / (durationMs / 1000)
      : undefined
  post({
    id: message.id,
    status: 'result',
    text,
    message: warning,
    durationMs,
    promptChars,
    outputChars: text.length,
    maxTokens,
    temperature,
    schemaEnabled,
    coldStart: message.coldStart ?? false,
    tokensPerSecond,
    modelTier: message.modelTier,
    wasCached: currentModelWasCached,
    timeoutMs: message.timeoutMs,
  })
}

self.onmessage = async (event: MessageEvent<WorkerMessage>) => {
  const message = event.data
  try {
    if (message.type === 'load') await loadModel(message)
    else if (message.type === 'complete') await complete(message)
    else if (message.type === 'interrupt') await engine?.interruptGenerate()
    else if (message.type === 'stop') {
      if (engine) await engine.unload()
      engine = null
      currentModelId = null
      currentModelWasCached = false
      post({ id: message.id, status: 'stopped', message: 'Browser model unloaded. Cached files remain available.' })
    }
  } catch (error) {
    post({ id: message.id, status: 'error', error: error instanceof Error ? error.message : 'Browser WebLLM failed.' })
  }
}
