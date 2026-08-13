import {
  ARTICLE_SOURCE_PROTOCOL,
  ARTICLE_SOURCE_PROTOCOL_VERSION,
  SOURCE_PATCH_PROTOCOL,
  type ArticleSourceEnvelopeV1,
  type ReplaceSourceRangeOperationV1,
  type SourceByteRangeV1,
  type SourcePatchSetV1,
} from './source-protocol.ts'

const UTF8_BOM = new Uint8Array([0xef, 0xbb, 0xbf])
const utf8Encoder = new TextEncoder()
const strictUtf8Decoder = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true })

export type BodyLineEndings = 'lf' | 'crlf' | 'mixed' | 'none'

export interface PreparedArticleBodyV1 {
  readonly canonicalBody: string
  readonly editorBody: string
  readonly bodyBytes: Uint8Array
  readonly bodyRange: SourceByteRangeV1
  readonly lineEndings: BodyLineEndings
  readonly protectedRanges: readonly SourceByteRangeV1[]
}

export type BodySourceEditPlan =
  | { readonly kind: 'unchanged' }
  | { readonly kind: 'unsupported'; readonly reason: string }
  | {
      readonly kind: 'ready'
      readonly operation: ReplaceSourceRangeOperationV1
      readonly patchSet: SourcePatchSetV1
    }

/**
 * Validates the source envelope's exact UTF-8/body relationship once when an article is opened.
 * The editor receives only the body projection; frontmatter and all other canonical bytes remain
 * outside the editable surface.
 */
export function prepareArticleBody(envelope: ArticleSourceEnvelopeV1): PreparedArticleBodyV1 {
  if (
    envelope.protocol !== ARTICLE_SOURCE_PROTOCOL ||
    envelope.version !== ARTICLE_SOURCE_PROTOCOL_VERSION ||
    envelope.base.encoding !== 'utf-8'
  ) {
    throw new Error('The article uses an unsupported source protocol or encoding.')
  }

  const canonicalBytes = decodeBase64(envelope.base.utf8Base64)
  if (canonicalBytes.byteLength !== envelope.base.byteLength) {
    throw new Error('The canonical source byte length does not match its envelope.')
  }

  assertBom(envelope.base.bom, canonicalBytes)
  decodeUtf8(canonicalBytes)

  const bodyRange = envelope.projection.body.range
  assertRange(bodyRange, canonicalBytes.byteLength, 'body')
  const contentStart = envelope.base.bom === 'utf-8' ? UTF8_BOM.byteLength : 0
  if (bodyRange.start < contentStart) {
    throw new Error('The editable body overlaps the canonical UTF-8 BOM.')
  }

  const frontmatter = envelope.projection.frontmatter
  if (frontmatter !== undefined) {
    assertProjectedSlice(frontmatter.range, frontmatter.utf8Base64, canonicalBytes, 'frontmatter')
    if (frontmatter.range.start < contentStart || frontmatter.range.endExclusive > bodyRange.start) {
      throw new Error('The editable body overlaps the frontmatter source range.')
    }
  }

  const bodyBytes = canonicalBytes.slice(bodyRange.start, bodyRange.endExclusive)
  const projectedBodyBytes = decodeBase64(envelope.projection.body.utf8Base64)
  if (!bytesEqual(bodyBytes, projectedBodyBytes)) {
    throw new Error('The body projection does not match the canonical source bytes.')
  }

  const canonicalBody = decodeUtf8(bodyBytes)
  const bodyLineEndings = detectLineEndings(canonicalBody)
  const lineEndings = bodyLineEndings === 'none'
    ? inheritDocumentLineEndings(envelope.base.lineEndings)
    : bodyLineEndings
  const protectedRanges = envelope.projection.segments
    .filter((segment) => segment.kind === 'protected-raw')
    .map((segment) => {
      assertRange(segment.range, canonicalBytes.byteLength, `protected segment ${segment.id}`)
      if (
        segment.range.start < bodyRange.start ||
        segment.range.endExclusive > bodyRange.endExclusive
      ) {
        throw new Error(`Protected segment ${segment.id} falls outside the editable body.`)
      }

      const segmentBytes = canonicalBytes.slice(segment.range.start, segment.range.endExclusive)
      const projectedSegmentBytes = decodeBase64(segment.source.utf8Base64)
      if (!rangesEqual(segment.range, segment.source.range) || !bytesEqual(segmentBytes, projectedSegmentBytes)) {
        throw new Error(`Protected segment ${segment.id} does not match the canonical source bytes.`)
      }

      return { ...segment.range }
    })

  if (protectedRanges.length !== envelope.capabilities.protectedSegmentCount) {
    throw new Error('The protected raw segment count does not match the source projection.')
  }

  return {
    canonicalBody,
    editorBody: normalizeEditorLineEndings(canonicalBody),
    bodyBytes,
    bodyRange: { ...bodyRange },
    lineEndings,
    protectedRanges,
  }
}

/**
 * Produces at most one localized replacement against the opened body bytes. Prefix/suffix
 * discovery is code-point-safe, while absolute offsets and expected bytes come from TextEncoder
 * and the canonical byte array rather than JavaScript string indices.
 */
export function planBodySourceEdit(
  envelope: ArticleSourceEnvelopeV1,
  prepared: PreparedArticleBodyV1,
  editorBody: string,
): BodySourceEditPlan {
  if (editorBody === prepared.editorBody) {
    return { kind: 'unchanged' }
  }

  if (prepared.lineEndings === 'mixed') {
    return {
      kind: 'unsupported',
      reason: 'The source has mixed or lone-carriage-return line endings, so safe source save is disabled.',
    }
  }

  if (hasUnpairedSurrogate(editorBody)) {
    return {
      kind: 'unsupported',
      reason: 'The source contains an unmatched Unicode surrogate and cannot be encoded exactly as UTF-8.',
    }
  }

  const canonicalEditedBody = restoreCanonicalLineEndings(editorBody, prepared.lineEndings)
  if (canonicalEditedBody === prepared.canonicalBody) {
    return { kind: 'unchanged' }
  }

  const originalCodePoints = Array.from(prepared.canonicalBody)
  const editedCodePoints = Array.from(canonicalEditedBody)
  const prefixLength = commonPrefixLength(originalCodePoints, editedCodePoints)
  const suffixLength = commonSuffixLength(originalCodePoints, editedCodePoints, prefixLength)

  const prefixByteLength = utf8Encoder.encode(originalCodePoints.slice(0, prefixLength).join('')).byteLength
  const suffixByteLength = utf8Encoder.encode(
    originalCodePoints.slice(originalCodePoints.length - suffixLength).join(''),
  ).byteLength
  const expectedEnd = prepared.bodyBytes.byteLength - suffixByteLength
  const expectedBytes = prepared.bodyBytes.slice(prefixByteLength, expectedEnd)
  const replacementText = editedCodePoints
    .slice(prefixLength, editedCodePoints.length - suffixLength)
    .join('')
  const replacementBytes = utf8Encoder.encode(replacementText)
  const range = {
    start: prepared.bodyRange.start + prefixByteLength,
    endExclusive: prepared.bodyRange.start + expectedEnd,
  }

  if (prepared.protectedRanges.some((protectedRange) => rangesIntersect(range, protectedRange))) {
    return {
      kind: 'unsupported',
      reason: 'The edit crosses a protected raw source segment. Its canonical bytes were left untouched.',
    }
  }

  const operation: ReplaceSourceRangeOperationV1 = {
    kind: 'replace',
    range,
    expectedUtf8Base64: encodeBase64(expectedBytes),
    replacementUtf8Base64: encodeBase64(replacementBytes),
    intent: 'source-edit',
  }

  return {
    kind: 'ready',
    operation,
    patchSet: {
      protocol: SOURCE_PATCH_PROTOCOL,
      version: ARTICLE_SOURCE_PROTOCOL_VERSION,
      articleId: envelope.article.id,
      relativePath: envelope.article.relativePath,
      baseSha256: envelope.base.sha256,
      operations: [operation],
    },
  }
}

export function decodeUtf8Base64(value: string): string {
  return decodeUtf8(decodeBase64(value))
}

function decodeBase64(value: string): Uint8Array {
  let binary: string
  try {
    binary = atob(value)
  } catch {
    throw new Error('The source envelope contains invalid base64 bytes.')
  }

  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}

function encodeBase64(bytes: Uint8Array): string {
  const chunkSize = 0x8000
  let binary = ''
  for (let offset = 0; offset < bytes.byteLength; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize))
  }
  return btoa(binary)
}

function decodeUtf8(bytes: Uint8Array): string {
  try {
    return strictUtf8Decoder.decode(bytes)
  } catch {
    throw new Error('The source envelope is not valid UTF-8 and cannot be edited safely.')
  }
}

function assertBom(bom: ArticleSourceEnvelopeV1['base']['bom'], bytes: Uint8Array): void {
  if (bom !== 'none' && bom !== 'utf-8') {
    throw new Error('The canonical source declares an unsupported BOM.')
  }
  const hasBom = bytes.byteLength >= UTF8_BOM.byteLength && UTF8_BOM.every((byte, index) => bytes[index] === byte)
  if ((bom === 'utf-8') !== hasBom) {
    throw new Error('The canonical source BOM does not match its envelope.')
  }
}

function assertProjectedSlice(
  range: SourceByteRangeV1,
  utf8Base64: string,
  canonicalBytes: Uint8Array,
  label: string,
): void {
  assertRange(range, canonicalBytes.byteLength, label)
  const canonicalSlice = canonicalBytes.slice(range.start, range.endExclusive)
  if (!bytesEqual(canonicalSlice, decodeBase64(utf8Base64))) {
    throw new Error(`The ${label} projection does not match the canonical source bytes.`)
  }
}

function assertRange(range: SourceByteRangeV1, byteLength: number, label: string): void {
  if (
    !Number.isSafeInteger(range.start) ||
    !Number.isSafeInteger(range.endExclusive) ||
    range.start < 0 ||
    range.endExclusive < range.start ||
    range.endExclusive > byteLength
  ) {
    throw new Error(`The ${label} source byte range is invalid.`)
  }
}

function rangesEqual(left: SourceByteRangeV1, right: SourceByteRangeV1): boolean {
  return left.start === right.start && left.endExclusive === right.endExclusive
}

function bytesEqual(left: Uint8Array, right: Uint8Array): boolean {
  return left.byteLength === right.byteLength && left.every((byte, index) => right[index] === byte)
}

function detectLineEndings(value: string): BodyLineEndings {
  let lf = 0
  let crlf = 0
  let loneCr = 0
  for (let index = 0; index < value.length; index += 1) {
    if (value[index] === '\r') {
      if (value[index + 1] === '\n') {
        crlf += 1
        index += 1
      } else {
        loneCr += 1
      }
    } else if (value[index] === '\n') {
      lf += 1
    }
  }

  if (loneCr > 0 || (lf > 0 && crlf > 0)) {
    return 'mixed'
  }
  if (crlf > 0) {
    return 'crlf'
  }
  if (lf > 0) {
    return 'lf'
  }
  return 'none'
}

function inheritDocumentLineEndings(
  lineEndings: ArticleSourceEnvelopeV1['base']['lineEndings'],
): BodyLineEndings {
  if (!['lf', 'crlf', 'mixed', 'none'].includes(lineEndings)) {
    throw new Error('The canonical source declares unsupported line endings.')
  }
  return lineEndings
}

function normalizeEditorLineEndings(value: string): string {
  return value.replace(/\r\n?/gu, '\n')
}

function restoreCanonicalLineEndings(value: string, lineEndings: BodyLineEndings): string {
  const normalized = normalizeEditorLineEndings(value)
  return lineEndings === 'crlf' ? normalized.replace(/\n/gu, '\r\n') : normalized
}

function commonPrefixLength(left: readonly string[], right: readonly string[]): number {
  const limit = Math.min(left.length, right.length)
  let length = 0
  while (length < limit && left[length] === right[length]) {
    length += 1
  }
  return length
}

function commonSuffixLength(
  left: readonly string[],
  right: readonly string[],
  prefixLength: number,
): number {
  const limit = Math.min(left.length, right.length) - prefixLength
  let length = 0
  while (
    length < limit &&
    left[left.length - length - 1] === right[right.length - length - 1]
  ) {
    length += 1
  }
  return length
}

function rangesIntersect(left: SourceByteRangeV1, right: SourceByteRangeV1): boolean {
  if (left.start === left.endExclusive) {
    return left.start > right.start && left.start < right.endExclusive
  }
  return left.start < right.endExclusive && left.endExclusive > right.start
}

function hasUnpairedSurrogate(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index)
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(index + 1)
      if (next < 0xdc00 || next > 0xdfff) {
        return true
      }
      index += 1
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      return true
    }
  }
  return false
}
