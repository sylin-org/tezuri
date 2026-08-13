import { HttpTezuriApi, type TezuriApi } from './tezuri-api'

/**
 * The bootstrap nonce is accepted once from the launch URL, retained only by this module's API
 * instance, and removed from browser history before the application performs any request.
 */
const launchNonce = consumeLaunchNonce()
export const hasLaunchNonce = launchNonce !== undefined
export const sessionApi: TezuriApi = new HttpTezuriApi(
  launchNonce === undefined ? {} : { nonce: launchNonce },
)

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
