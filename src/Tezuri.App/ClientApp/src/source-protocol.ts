/**
 * The permanent browser/server boundary for canonical article source.
 *
 * Editor-native ProseMirror, Milkdown, Lexical, or Tiptap state must never be added here. The
 * canonical document is transferred as exact UTF-8 bytes and changed through source-range patches.
 */

export const ARTICLE_SOURCE_PROTOCOL = 'tezuri.article-source' as const
export const SOURCE_PATCH_PROTOCOL = 'tezuri.source-patch-set' as const
export const ARTICLE_SOURCE_PROTOCOL_VERSION = 1 as const

export type Sha256Hex = string
export type Utf8Base64 = string
export type Iso8601Utc = string

export interface SourceByteRangeV1 {
  readonly start: number
  readonly endExclusive: number
}

export interface CanonicalSourceBytesV1 {
  readonly encoding: 'utf-8'
  readonly bom: 'none' | 'utf-8'
  readonly lineEndings: 'lf' | 'crlf' | 'mixed' | 'none'
  readonly byteLength: number
  readonly sha256: Sha256Hex
  readonly utf8Base64: Utf8Base64
}

export interface SourceSliceV1 {
  readonly range: SourceByteRangeV1
  readonly sha256: Sha256Hex
  readonly utf8Base64: Utf8Base64
}

export interface RichSourceSegmentV1 {
  readonly kind: 'rich'
  readonly id: string
  readonly range: SourceByteRangeV1
  readonly source: SourceSliceV1
  readonly syntax: 'commonmark' | 'gfm'
}

export interface ProtectedRawSourceSegmentV1 {
  readonly kind: 'protected-raw'
  readonly id: string
  readonly range: SourceByteRangeV1
  readonly source: SourceSliceV1
  readonly syntaxHint: 'html' | 'directive' | 'embed' | 'unknown'
  readonly notice: string
}

export type ArticleSourceSegmentV1 = RichSourceSegmentV1 | ProtectedRawSourceSegmentV1

export interface SourceDiagnosticV1 {
  readonly code: string
  readonly severity: 'information' | 'warning' | 'error'
  readonly message: string
  readonly range?: SourceByteRangeV1
}

export interface ArticleSourceEnvelopeV1 {
  readonly protocol: typeof ARTICLE_SOURCE_PROTOCOL
  readonly version: typeof ARTICLE_SOURCE_PROTOCOL_VERSION
  readonly article: {
    readonly id: string
    readonly slug: string
    readonly displayTitle: string
    readonly relativePath: string
  }
  readonly base: CanonicalSourceBytesV1
  readonly projection: {
    readonly frontmatter?: SourceSliceV1
    readonly body: SourceSliceV1
    readonly segments: readonly ArticleSourceSegmentV1[]
  }
  readonly capabilities: {
    readonly richEditing: 'available' | 'source-only'
    readonly protectedSegmentCount: number
  }
  readonly diagnostics: readonly SourceDiagnosticV1[]
}

export interface ReplaceSourceRangeOperationV1 {
  readonly kind: 'replace'
  readonly range: SourceByteRangeV1
  /** Original bytes make each operation independently conflict-detectable. */
  readonly expectedUtf8Base64: Utf8Base64
  readonly replacementUtf8Base64: Utf8Base64
  readonly intent: 'rich-edit' | 'source-edit' | 'metadata-edit'
  readonly segmentId?: string
}

export interface SourcePatchSetV1 {
  readonly protocol: typeof SOURCE_PATCH_PROTOCOL
  readonly version: typeof ARTICLE_SOURCE_PROTOCOL_VERSION
  readonly articleId: string
  readonly relativePath: string
  readonly baseSha256: Sha256Hex
  readonly operations: readonly ReplaceSourceRangeOperationV1[]
}

export interface AppliedSourcePatchV1 {
  readonly protocol: 'tezuri.applied-source-patch'
  readonly version: typeof ARTICLE_SOURCE_PROTOCOL_VERSION
  readonly savedAt: Iso8601Utc
  readonly previousSha256: Sha256Hex
  readonly current: ArticleSourceEnvelopeV1
}

export interface SourcePatchConflictV1 {
  readonly protocol: 'tezuri.source-patch-conflict'
  readonly version: typeof ARTICLE_SOURCE_PROTOCOL_VERSION
  readonly articleId: string
  readonly expectedBaseSha256: Sha256Hex
  readonly current: ArticleSourceEnvelopeV1
  readonly message: string
}

export interface ArticleSummaryV1 {
  readonly id: string
  readonly slug: string
  readonly displayTitle: string
  readonly relativePath: string
  readonly publicationState: 'draft' | 'published' | 'unknown'
  readonly sourceSha256: Sha256Hex
  readonly updatedAt?: Iso8601Utc
}

export interface ArticleListEnvelopeV1 {
  readonly protocol: 'tezuri.article-list'
  readonly version: typeof ARTICLE_SOURCE_PROTOCOL_VERSION
  readonly articles: readonly ArticleSummaryV1[]
}
