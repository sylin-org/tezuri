import type { ProofRun } from './proof-types'

export interface GitChangedPath {
  readonly path: string
  readonly indexStatus: string
  readonly workTreeStatus: string
  readonly allowed: boolean
}

export interface GitRemoteBranch {
  readonly remote: string
  readonly branch: string
  readonly sha: string
}

export interface GitRepositorySnapshot {
  readonly headSha: string | null
  readonly isUnborn: boolean
  readonly isDetached: boolean
  readonly branch: string | null
  readonly upstream: string | null
  readonly remotes: readonly string[]
  readonly remoteBranches: readonly GitRemoteBranch[]
  readonly changes: readonly GitChangedPath[]
}

export interface GitCommitPlanRequest {
  readonly selectedPaths: readonly string[]
}

export interface GitCommitPlan {
  readonly headSha: string
  readonly branch: string
  readonly planSha256: string
  readonly selectedPaths: readonly string[]
  readonly changes: readonly GitChangedPath[]
}

export interface PrepareGitCommitRequest {
  readonly expectedHeadSha: string
  readonly expectedPlanSha256: string
  readonly message: string
  readonly selectedPaths: readonly string[]
}

export interface GitCommitReceipt {
  readonly beforeSha: string
  readonly afterSha: string
  readonly branch: string
  readonly planSha256: string
  readonly selectedPaths: readonly string[]
  readonly created: boolean
}

export interface GitPushRequest {
  readonly remote: string
  readonly branch: string
  readonly expectedHeadSha: string
  readonly expectedRemoteSha: string
}

export interface GitPushReceipt {
  readonly remote: string
  readonly branch: string
  readonly localSha: string
  readonly remoteBeforeSha: string
  readonly remoteAfterSha: string
  readonly pushed: boolean
}

export interface TezuriApi {
  runSiteProof(signal?: AbortSignal): Promise<ProofRun>
  inspectGit(signal?: AbortSignal): Promise<GitRepositorySnapshot>
  planGitCommit(
    request: GitCommitPlanRequest,
    signal?: AbortSignal,
  ): Promise<GitCommitPlan>
  prepareGitCommit(
    request: PrepareGitCommitRequest,
    signal?: AbortSignal,
  ): Promise<GitCommitReceipt>
  pushGit(request: GitPushRequest, signal?: AbortSignal): Promise<GitPushReceipt>
}

export interface TezuriApiClientOptions {
  readonly baseUrl?: string
  /** Kept in memory and delegated by the local bootstrap flow; never persisted here. */
  readonly nonce?: string
}

export class TezuriApiError extends Error {
  readonly status: number
  readonly problem: unknown

  constructor(message: string, status: number, problem: unknown) {
    super(message)
    this.name = 'TezuriApiError'
    this.status = status
    this.problem = problem
  }
}

export class HttpTezuriApi implements TezuriApi {
  readonly #baseUrl: string
  readonly #nonce: string | undefined

  constructor(options: TezuriApiClientOptions = {}) {
    this.#baseUrl = options.baseUrl?.replace(/\/$/, '') ?? ''
    this.#nonce = options.nonce
  }


  runSiteProof(signal?: AbortSignal): Promise<ProofRun> {
    return this.#request<ProofRun>('/api/v1/proof/runs', {
      method: 'POST',
      signal: signal ?? null,
    })
  }

  inspectGit(signal?: AbortSignal): Promise<GitRepositorySnapshot> {
    return this.#request<GitRepositorySnapshot>('/api/v1/git/status', {
      method: 'GET',
      signal: signal ?? null,
    })
  }

  planGitCommit(
    request: GitCommitPlanRequest,
    signal?: AbortSignal,
  ): Promise<GitCommitPlan> {
    return this.#request<GitCommitPlan>('/api/v1/git/commit-plans', {
      method: 'POST',
      body: JSON.stringify(request),
      signal: signal ?? null,
    })
  }

  prepareGitCommit(
    request: PrepareGitCommitRequest,
    signal?: AbortSignal,
  ): Promise<GitCommitReceipt> {
    return this.#request<GitCommitReceipt>('/api/v1/git/commits', {
      method: 'POST',
      body: JSON.stringify(request),
      signal: signal ?? null,
    })
  }

  pushGit(request: GitPushRequest, signal?: AbortSignal): Promise<GitPushReceipt> {
    return this.#request<GitPushReceipt>('/api/v1/git/pushes', {
      method: 'POST',
      body: JSON.stringify(request),
      signal: signal ?? null,
    })
  }

  async #request<T>(path: string, init: RequestInit): Promise<T> {
    const headers = new Headers(init.headers)
    headers.set('Accept', 'application/json')

    if (init.body !== undefined && init.body !== null && !(init.body instanceof FormData)) {
      headers.set('Content-Type', 'application/json')
    }

    if (this.#nonce !== undefined) {
      headers.set('X-Tezuri-Nonce', this.#nonce)
    }

    const response = await fetch(`${this.#baseUrl}${path}`, {
      ...init,
      credentials: 'same-origin',
      headers,
    })

    const responseType = response.headers.get('content-type') ?? ''
    const body: unknown = responseType.includes('json')
      ? await response.json()
      : await response.text()

    if (!response.ok) {
      throw new TezuriApiError(`Tezuri request failed with status ${response.status}.`, response.status, body)
    }

    return body as T
  }
}

