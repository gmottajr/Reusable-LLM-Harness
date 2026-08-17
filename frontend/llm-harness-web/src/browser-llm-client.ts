export type BrowserProgress = {
  progress: number
  message: string
  downloadedMB?: number
  totalMB?: number
}

type BrowserWorkerResponse = {
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

type BrowserRequest = {
  resolve: (value: BrowserWorkerResponse) => void
  reject: (reason?: unknown) => void
  onProgress?: (progress: BrowserProgress) => void
  timeoutId?: ReturnType<typeof setTimeout>
}

export class BrowserLlmTimeoutError extends Error {
  readonly code = 'Timeout'

  constructor(readonly timeoutMs: number) {
    super(`Browser WebLLM completion exceeded the configured timeout of ${timeoutMs} ms.`)
    this.name = 'BrowserLlmTimeoutError'
  }
}

export class BrowserLlmClient {
  private worker: Worker | null = null
  private readonly requests = new Map<string, BrowserRequest>()
  private currentModelId: string | null = null
  private currentModelWasCached = false

  private getWorker() {
    if (!this.worker) {
      this.worker = new Worker(new URL('./browser-llm.worker.ts', import.meta.url), { type: 'module' })
      this.worker.onmessage = (event: MessageEvent<BrowserWorkerResponse>) => this.handleMessage(event.data)
      this.worker.onerror = (event) => {
        for (const request of this.requests.values()) {
          if (request.timeoutId) clearTimeout(request.timeoutId)
          request.reject(new Error(event.message || 'Browser WebLLM worker failed.'))
        }
        this.requests.clear()
      }
    }
    return this.worker
  }

  private handleMessage(message: BrowserWorkerResponse) {
    const request = this.requests.get(message.id)
    if (!request) return
    if (message.status === 'progress') {
      request.onProgress?.({
        progress: message.progress ?? 0,
        message: message.message ?? 'Loading browser model…',
        downloadedMB: message.downloadedMB,
        totalMB: message.totalMB,
      })
      return
    }
    this.requests.delete(message.id)
    if (request.timeoutId) clearTimeout(request.timeoutId)
    if (message.status === 'error') request.reject(new Error(message.error ?? 'Browser WebLLM failed.'))
    else request.resolve(message)
  }

  private timeoutRequest(id: string, timeoutMs: number) {
    const request = this.requests.get(id)
    if (!request) return
    this.requests.delete(id)
    if (request.timeoutId) clearTimeout(request.timeoutId)
    this.worker?.postMessage({ id: crypto.randomUUID(), type: 'interrupt' })
    request.reject(new BrowserLlmTimeoutError(timeoutMs))
  }

  private send(type: 'load' | 'complete' | 'stop', payload: Omit<WorkerRequestMessage, 'id' | 'type'>, onProgress?: (progress: BrowserProgress) => void, timeoutMs?: number) {
    const id = crypto.randomUUID()
    return new Promise<BrowserWorkerResponse>((resolve, reject) => {
      const timeoutId = timeoutMs && timeoutMs > 0
        ? setTimeout(() => this.timeoutRequest(id, timeoutMs), timeoutMs)
        : undefined
      this.requests.set(id, { resolve, reject, onProgress, timeoutId })
      this.getWorker().postMessage({ id, type, ...payload })
    })
  }

  async loadModel(modelId: string, onProgress?: (progress: BrowserProgress) => void) {
    const response = await this.send('load', { modelId }, onProgress)
    this.currentModelId = modelId
    this.currentModelWasCached = response.wasCached ?? false
    return response
  }

  isLoaded(modelId: string) {
    return this.currentModelId === modelId
  }

  wasCached(modelId: string) {
    return this.currentModelId === modelId && this.currentModelWasCached
  }

  async complete(
    messages: Array<{ role: 'system' | 'user' | 'assistant'; content: string }>,
    options: {
      responseSchema?: Record<string, unknown>
      timeoutMs: number
      maxTokens: number
      temperature: number
      schemaEnabled: boolean
      coldStart: boolean
      modelTier: string
    },
  ) {
    return this.send('complete', { messages, ...options }, undefined, options.timeoutMs)
  }

  async stop() {
    if (!this.worker) return
    await this.send('stop', {})
    this.currentModelId = null
    this.currentModelWasCached = false
  }

  dispose() {
    this.worker?.terminate()
    this.worker = null
    this.currentModelId = null
    this.currentModelWasCached = false
    for (const request of this.requests.values()) {
      if (request.timeoutId) clearTimeout(request.timeoutId)
      request.reject(new Error('Browser WebLLM worker disposed.'))
    }
    this.requests.clear()
  }
}

type WorkerRequestMessage = {
  id: string
  type: 'load' | 'complete' | 'stop' | 'interrupt'
  modelId?: string
  messages?: Array<{ role: 'system' | 'user' | 'assistant'; content: string }>
  responseSchema?: Record<string, unknown>
  temperature?: number
  maxTokens?: number
  schemaEnabled?: boolean
  coldStart?: boolean
  modelTier?: string
  timeoutMs?: number
}
