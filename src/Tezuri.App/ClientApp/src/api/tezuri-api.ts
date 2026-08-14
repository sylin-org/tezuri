import type { SiteProofRunReceiptV1 } from '../proof-protocol'

export interface GitChangedPathV1 {
  readonly path: string
  readonly indexStatus: string
  readonly workTreeStatus: string
  readonly allowed: boolean
}

export interface GitRemoteBranchV1 {
  readonly remote: string
  readonly branch: string
  readonly sha: string
}

export interface GitRepositorySnapshotV1 {
  readonly protocol: 'tezuri.git-repository-snapshot'
  readonly version: 1
  readonly headSha: string | null
  readonly isUnborn: boolean
  readonly isDetached: boolean
  readonly branch: string | null
  readonly upstream: string | null
  readonly remotes: readonly string[]
  readonly remoteBranches: readonly GitRemoteBranchV1[]
  readonly changes: readonly GitChangedPathV1[]
}

export interface GitCommitPlanRequestV1 {
  readonly selectedPaths: readonly string[]
}

export interface GitCommitPlanV1 {
  readonly protocol: 'tezuri.git-commit-plan'
  readonly version: 1
  readonly headSha: string
  readonly branch: string
  readonly planSha256: string
  readonly selectedPaths: readonly string[]
  readonly changes: readonly GitChangedPathV1[]
}

export interface PrepareGitCommitRequestV1 {
  readonly expectedHeadSha: string
  readonly expectedPlanSha256: string
  readonly message: string
  readonly selectedPaths: readonly string[]
}

export interface GitCommitReceiptV1 {
  readonly protocol: 'tezuri.git-commit-receipt'
  readonly version: 1
  readonly beforeSha: string
  readonly afterSha: string
  readonly branch: string
  readonly planSha256: string
  readonly selectedPaths: readonly string[]
  readonly created: boolean
}

export interface GitPushRequestV1 {
  readonly remote: string
  readonly branch: string
  readonly expectedHeadSha: string
  readonly expectedRemoteSha: string
}

export interface GitPushReceiptV1 {
  readonly protocol: 'tezuri.git-push-receipt'
  readonly version: 1
  readonly remote: string
  readonly branch: string
  readonly localSha: string
  readonly remoteBeforeSha: string
  readonly remoteAfterSha: string
  readonly pushed: boolean
}

export interface TezuriApi {
  runSiteProof(signal?: AbortSignal): Promise<SiteProofRunReceiptV1>
  inspectGit(signal?: AbortSignal): Promise<GitRepositorySnapshotV1>
  planGitCommit(
    request: GitCommitPlanRequestV1,
    signal?: AbortSignal,
  ): Promise<GitCommitPlanV1>
  prepareGitCommit(
    request: PrepareGitCommitRequestV1,
    signal?: AbortSignal,
  ): Promise<GitCommitReceiptV1>
  pushGit(request: GitPushRequestV1, signal?: AbortSignal): Promise<GitPushReceiptV1>
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


  runSiteProof(signal?: AbortSignal): Promise<SiteProofRunReceiptV1> {
    return this.#request<SiteProofRunReceiptV1>('/api/v1/proof/runs', {
      method: 'POST',
      signal: signal ?? null,
    })
  }

  inspectGit(signal?: AbortSignal): Promise<GitRepositorySnapshotV1> {
    return this.#request<GitRepositorySnapshotV1>('/api/v1/git/status', {
      method: 'GET',
      signal: signal ?? null,
    })
  }

  planGitCommit(
    request: GitCommitPlanRequestV1,
    signal?: AbortSignal,
  ): Promise<GitCommitPlanV1> {
    return this.#request<GitCommitPlanV1>('/api/v1/git/commit-plans', {
      method: 'POST',
      body: JSON.stringify(request),
      signal: signal ?? null,
    })
  }

  prepareGitCommit(
    request: PrepareGitCommitRequestV1,
    signal?: AbortSignal,
  ): Promise<GitCommitReceiptV1> {
    return this.#request<GitCommitReceiptV1>('/api/v1/git/commits', {
      method: 'POST',
      body: JSON.stringify(request),
      signal: signal ?? null,
    })
  }

  pushGit(request: GitPushRequestV1, signal?: AbortSignal): Promise<GitPushReceiptV1> {
    return this.#request<GitPushReceiptV1>('/api/v1/git/pushes', {
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

