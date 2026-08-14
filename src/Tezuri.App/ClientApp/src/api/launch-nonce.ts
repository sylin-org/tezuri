/**
 * Resolving the launch nonce, with no browser globals in sight.
 *
 * The nonce arrives once in the launch URL, is removed from browser history before the application
 * issues any request, and is then held for the lifetime of the tab. Keeping it only in a module
 * variable meant an ordinary refresh silently downgraded the editor to read-only with no way back
 * except finding the launch URL again — the most common way to lose access to your own work.
 * Session storage is origin-scoped and dies with the tab, and any script able to read it could
 * already have issued requests with the in-memory value, so this restores the refresh without
 * widening the boundary. It is never written to durable storage, a cookie, or the repository.
 */

export const SESSION_NONCE_KEY = 'tezuri.launch-nonce'

/** The subset of `Storage` this needs — enough to substitute one that refuses, or none at all. */
export interface NonceStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

export interface LaunchNonceResolution {
  readonly nonce: string | undefined
  /**
   * Present only when the nonce came from the query string: the same-origin path the caller should
   * replace the current history entry with, so the nonce leaves the address bar and the back stack.
   */
  readonly scrubbedUrl: string | undefined
}

export function resolveLaunchNonce(
  href: string,
  storage: NonceStorage | undefined,
): LaunchNonceResolution {
  const fromUrl = readFromUrl(href)
  if (fromUrl.nonce !== undefined) {
    remember(storage, fromUrl.nonce)
    return fromUrl
  }

  // A URL that carried an empty nonce is still scrubbed; it just leaves nothing behind.
  return { nonce: recall(storage), scrubbedUrl: fromUrl.scrubbedUrl }
}

function readFromUrl(href: string): LaunchNonceResolution {
  let launchUrl: URL
  try {
    launchUrl = new URL(href)
  } catch {
    return { nonce: undefined, scrubbedUrl: undefined }
  }

  if (!launchUrl.searchParams.has('nonce')) {
    return { nonce: undefined, scrubbedUrl: undefined }
  }

  const nonce = launchUrl.searchParams.get('nonce') ?? ''
  launchUrl.searchParams.delete('nonce')
  return {
    nonce: nonce === '' ? undefined : nonce,
    scrubbedUrl: `${launchUrl.pathname}${launchUrl.search}${launchUrl.hash}`,
  }
}

function remember(storage: NonceStorage | undefined, nonce: string): void {
  try {
    storage?.setItem(SESSION_NONCE_KEY, nonce)
  } catch {
    // Private modes and storage-partitioned contexts refuse. The tab still works; only a refresh
    // loses authority, which is exactly the behaviour this replaced.
  }
}

function recall(storage: NonceStorage | undefined): string | undefined {
  try {
    const stored = storage?.getItem(SESSION_NONCE_KEY)
    return stored === null || stored === undefined || stored === '' ? undefined : stored
  } catch {
    return undefined
  }
}
