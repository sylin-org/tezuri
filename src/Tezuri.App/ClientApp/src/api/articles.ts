import { asArticleConflict, ArticleConflictError } from './article-conflict'
import { hasLaunchNonce, request } from './session-api'
import type {
  Article,
  ArticleSummary,
  MediaUploadReceipt,
  SaveArticleRequest,
} from './article-types'

export { hasLaunchNonce, ArticleConflictError }
export type { Article, ArticleSummary, MediaUploadReceipt, SaveArticleRequest }

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
      const conflict = asArticleConflict(error)
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
    return request<MediaUploadReceipt>(
      `/api/v1/articles/${encodeURIComponent(id)}/media`,
      { method: 'POST', body: form },
    )
  },
}
