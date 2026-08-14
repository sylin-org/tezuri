import { hasLaunchNonce, request } from './session-api'

export interface ArticleSummary {
  readonly id: string
  readonly title: string
  readonly subtitle: string | null
  readonly draft: boolean
  readonly tags: readonly string[]
  readonly updatedAt: string
  readonly revision: string
}

export interface Article {
  readonly id: string
  readonly title: string
  readonly subtitle: string | null
  readonly body: string
  readonly draft: boolean
  readonly date: string | null
  readonly tags: readonly string[]
  readonly revision: string
  readonly updatedAt: string
}

export interface SaveArticleRequest {
  readonly title: string
  readonly subtitle: string | null
  readonly body: string
  readonly draft: boolean
  readonly date: string | null
  readonly tags: readonly string[]
  /** The revision this tab read. The server refuses the write if it has moved on. */
  readonly revision: string
}

export class ArticleConflictError extends Error {
  readonly current: Article

  constructor(message: string, current: Article) {
    super(message)
    this.name = 'ArticleConflictError'
    this.current = current
  }
}

export { hasLaunchNonce }

export const articles = {
  list: () => request<ArticleSummary[]>('/api/v1/articles'),

  get: (id: string, signal?: AbortSignal) =>
    request<Article>(
      `/api/v1/articles/${encodeURIComponent(id)}`,
      signal === undefined ? {} : { signal },
    ),

  create: (title: string) =>
    request<Article>('/api/v1/articles', {
      method: 'POST',
      body: JSON.stringify({ title }),
    }),

  save: async (id: string, changes: SaveArticleRequest): Promise<Article> => {
    try {
      return await request<Article>(`/api/v1/articles/${encodeURIComponent(id)}`, {
        method: 'PUT',
        body: JSON.stringify(changes),
      })
    } catch (error) {
      const conflict = asConflict(error)
      if (conflict !== undefined) {
        throw new ArticleConflictError(conflict.detail, conflict.current)
      }
      throw error
    }
  },

  remove: (id: string) =>
    request<void>(`/api/v1/articles/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  uploadMedia: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file, file.name)
    return request<{ relativePath: string; fileName: string; deduplicated: boolean }>(
      `/api/v1/articles/${encodeURIComponent(id)}/media`,
      { method: 'POST', body: form },
    )
  },
}

function asConflict(error: unknown): { detail: string; current: Article } | undefined {
  if (
    typeof error !== 'object' ||
    error === null ||
    (error as { status?: number }).status !== 409
  ) {
    return undefined
  }

  const problem = (error as { problem?: unknown }).problem
  if (typeof problem !== 'object' || problem === null) {
    return undefined
  }

  const candidate = problem as { detail?: unknown; current?: unknown }
  if (typeof candidate.detail !== 'string' || typeof candidate.current !== 'object') {
    return undefined
  }

  return { detail: candidate.detail, current: candidate.current as Article }
}
