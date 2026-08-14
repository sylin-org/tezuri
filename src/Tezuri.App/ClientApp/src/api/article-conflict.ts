import type { Article } from './article-types'

/**
 * Raised when the server refuses a save because the article moved on in another Tezuri session.
 *
 * It carries the current article so the editor can offer it rather than just reporting a failure —
 * the text in this tab is never discarded.
 */
export class ArticleConflictError extends Error {
  readonly current: Article

  constructor(message: string, current: Article) {
    super(message)
    this.name = 'ArticleConflictError'
    this.current = current
  }
}

/**
 * Recognises the revision conflict inside an arbitrary thrown value. Anything that is not a 409
 * carrying a problem document with a detail and a current article is left alone, so a genuine
 * network or server fault is never reported to the writer as someone else's edit.
 */
export function asArticleConflict(
  error: unknown,
): { readonly detail: string; readonly current: Article } | undefined {
  if (typeof error !== 'object' || error === null) {
    return undefined
  }

  if ((error as { status?: unknown }).status !== 409) {
    return undefined
  }

  const problem = (error as { problem?: unknown }).problem
  if (typeof problem !== 'object' || problem === null) {
    return undefined
  }

  const candidate = problem as { detail?: unknown; current?: unknown }
  if (
    typeof candidate.detail !== 'string' ||
    typeof candidate.current !== 'object' ||
    candidate.current === null
  ) {
    return undefined
  }

  return { detail: candidate.detail, current: candidate.current as Article }
}
