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

export interface MediaUploadReceipt {
  readonly relativePath: string
  readonly fileName: string
  readonly deduplicated: boolean
}
