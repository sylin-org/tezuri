import { HttpTezuriApi, type TezuriApi } from './tezuri-api'
import { resolveLaunchNonce, type NonceStorage } from './launch-nonce'

/**
 * Binds the pure nonce resolution in `launch-nonce.ts` to this tab: read the launch URL, scrub it
 * from history before any request goes out, and keep the value for the lifetime of the tab.
 */
const launchNonce = bindLaunchNonce()
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

function bindLaunchNonce(): string | undefined {
  let storage: NonceStorage | undefined
  try {
    storage = window.sessionStorage
  } catch {
    storage = undefined
  }

  const resolved = resolveLaunchNonce(window.location.href, storage)
  if (resolved.scrubbedUrl !== undefined) {
    window.history.replaceState(window.history.state, '', resolved.scrubbedUrl)
  }

  return resolved.nonce
}
