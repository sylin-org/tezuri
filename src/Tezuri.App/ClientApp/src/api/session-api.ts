import { HttpTezuriApi, type TezuriApi } from './tezuri-api'

/**
 * The bootstrap nonce is accepted once from the launch URL and removed from browser history before
 * the application performs any request.
 *
 * It is then held for the lifetime of this browser tab in `sessionStorage`. Keeping it only in a
 * module variable meant an ordinary page refresh silently downgraded the whole application to
 * read-only, with no way back except finding the launch URL again — the most common way to lose
 * access to your own editor. Session storage is origin-scoped and dies with the tab, and any script
 * able to read it could already have issued requests with the in-memory value, so this restores the
 * refresh without widening the boundary. It is still never written to durable storage, a cookie, or
 * the repository.
 */
const SESSION_NONCE_KEY = 'tezuri.launch-nonce'

const launchNonce = resolveLaunchNonce()
export const hasLaunchNonce = launchNonce !== undefined
export const sessionApi: TezuriApi = new HttpTezuriApi(
  launchNonce === undefined ? {} : { nonce: launchNonce },
)

export class RequestError extends Error {
  readonly status: number
  readonly problem: unknown

  constructor(status: number, problem: unknown) {
    super(
      typeof problem === 'object' && problem !== null && typeof (problem as { title?: unknown }).title === 'string'
        ? ((problem as { title: string }).title)
        : `Request failed with status ${status}.`,
    )
    this.name = 'RequestError'
    this.status = status
    this.problem = problem
  }
}

/** One fetch helper: attaches the launch nonce, parses the body, throws a typed error. */
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body !== undefined && init.body !== null && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }
  if (launchNonce !== undefined) {
    headers.set('X-Tezuri-Nonce', launchNonce)
  }

  const response = await fetch(path, { ...init, credentials: 'same-origin', headers })
  if (response.status === 204) {
    return undefined as T
  }

  const body: unknown = (response.headers.get('content-type') ?? '').includes('json')
    ? await response.json()
    : await response.text()

  if (!response.ok) {
    throw new RequestError(response.status, body)
  }

  return body as T
}

function resolveLaunchNonce(): string | undefined {
  const fromUrl = consumeLaunchNonce()
  if (fromUrl !== undefined) {
    remember(fromUrl)
    return fromUrl
  }

  return recall()
}

function consumeLaunchNonce(): string | undefined {
  const launchUrl = new URL(window.location.href)
  if (!launchUrl.searchParams.has('nonce')) {
    return undefined
  }

  const nonce = launchUrl.searchParams.get('nonce') ?? undefined
  launchUrl.searchParams.delete('nonce')
  window.history.replaceState(
    window.history.state,
    '',
    `${launchUrl.pathname}${launchUrl.search}${launchUrl.hash}`,
  )

  return nonce === '' ? undefined : nonce
}

function remember(nonce: string): void {
  try {
    window.sessionStorage.setItem(SESSION_NONCE_KEY, nonce)
  } catch {
    // Private modes and storage-partitioned contexts can refuse. The tab still works; only a
    // refresh loses authority, which is the behaviour this replaced.
  }
}

function recall(): string | undefined {
  try {
    const stored = window.sessionStorage.getItem(SESSION_NONCE_KEY)
    return stored === null || stored === '' ? undefined : stored
  } catch {
    return undefined
  }
}
