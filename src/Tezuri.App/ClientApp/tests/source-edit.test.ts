import {
  decodeUtf8Base64,
  planBodySourceEdit,
  prepareArticleBody,
  type BodySourceEditPlan,
} from '../src/source-edit.ts'
import type {
  ArticleSourceEnvelopeV1,
  ArticleSourceSegmentV1,
  SourceByteRangeV1,
} from '../src/source-protocol.ts'
import { HttpTezuriApi, SourcePatchConflictError } from '../src/api/tezuri-api.ts'

const encoder = new TextEncoder()
const decoder = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true })

const tests: { readonly name: string; readonly run: () => void | Promise<void> }[] = []

test('plans one Unicode-safe byte replacement after frontmatter and BOM', () => {
  const frontmatter = '---\ntitle: Patina\n---\n'
  const fixture = envelope(frontmatter, 'A 🪴 stays.\n', { bom: true })
  const prepared = prepareArticleBody(fixture)

  const plan = expectReady(planBodySourceEdit(fixture, prepared, 'A 🪴 grows.\n'))
  const expectedStart = 3 + byteLength(frontmatter) + byteLength('A 🪴 ')

  equal(plan.operation.range.start, expectedStart)
  equal(plan.operation.range.endExclusive, expectedStart + byteLength('stay'))
  equal(decodeUtf8Base64(plan.operation.expectedUtf8Base64), 'stay')
  equal(decodeUtf8Base64(plan.operation.replacementUtf8Base64), 'grow')
  equal(plan.patchSet.operations.length, 1)
  equal(decodeApplied(fixture, plan), `${frontmatter}A 🪴 grows.\n`)
})

test('emits no patch for an unchanged source body', () => {
  const fixture = envelope('---\ntitle: Still\n---\n', 'Unchanged.\n')
  const prepared = prepareArticleBody(fixture)

  equal(planBodySourceEdit(fixture, prepared, prepared.editorBody).kind, 'unchanged')
})

test('restores CRLF before calculating a localized body edit', () => {
  const frontmatter = '---\r\ntitle: Windows\r\n---\r\n'
  const fixture = envelope(frontmatter, 'One.\r\nTwo.\r\n')
  const prepared = prepareArticleBody(fixture)

  equal(prepared.editorBody, 'One.\nTwo.\n')
  const plan = expectReady(planBodySourceEdit(fixture, prepared, 'One.\nDeux.\n'))
  equal(decodeUtf8Base64(plan.operation.expectedUtf8Base64), 'Two')
  equal(decodeUtf8Base64(plan.operation.replacementUtf8Base64), 'Deux')
  equal(decodeApplied(fixture, plan), `${frontmatter}One.\r\nDeux.\r\n`)
})

test('inherits document CRLF when a single-line body has no newline yet', () => {
  const frontmatter = '---\r\ntitle: Windows\r\n---\r\n'
  const fixture = envelope(frontmatter, 'One.')
  const prepared = prepareArticleBody(fixture)

  equal(prepared.lineEndings, 'crlf')
  const plan = expectReady(planBodySourceEdit(fixture, prepared, 'One.\nTwo.'))
  equal(decodeUtf8Base64(plan.operation.replacementUtf8Base64), '\r\nTwo.')
  equal(decodeApplied(fixture, plan), `${frontmatter}One.\r\nTwo.`)
})

test('keeps frontmatter bytes outside the planned replacement', () => {
  const frontmatter = '---\nunknown: keep exactly\n---\n'
  const fixture = envelope(frontmatter, 'Before.\n')
  const prepared = prepareArticleBody(fixture)
  const plan = expectReady(planBodySourceEdit(fixture, prepared, 'After.\n'))

  assert(plan.operation.range.start >= byteLength(frontmatter), 'operation must start after frontmatter')
  equal(decodeApplied(fixture, plan).slice(0, frontmatter.length), frontmatter)
})

test('blocks a replacement whose localized range crosses protected raw bytes', () => {
  const body = 'Before.\n<aside>keep raw</aside>\nAfter.\n'
  const raw = '<aside>keep raw</aside>'
  const bodyStart = byteLength('---\n---\n')
  const rawStart = bodyStart + byteLength(body.slice(0, body.indexOf(raw)))
  const protectedRange = { start: rawStart, endExclusive: rawStart + byteLength(raw) }
  const fixture = envelope('---\n---\n', body, { protectedRange })
  const prepared = prepareArticleBody(fixture)

  const plan = planBodySourceEdit(fixture, prepared, body.replace('keep raw', 'changed'))
  equal(plan.kind, 'unsupported')
})

test('allows a localized edit wholly outside protected raw bytes', () => {
  const frontmatter = '---\n---\n'
  const body = 'Before.\n<aside>keep raw</aside>\nAfter.\n'
  const raw = '<aside>keep raw</aside>'
  const bodyStart = byteLength(frontmatter)
  const rawStart = bodyStart + byteLength(body.slice(0, body.indexOf(raw)))
  const fixture = envelope(frontmatter, body, {
    protectedRange: { start: rawStart, endExclusive: rawStart + byteLength(raw) },
  })
  const prepared = prepareArticleBody(fixture)

  const plan = expectReady(planBodySourceEdit(fixture, prepared, body.replace('Before', 'Earlier')))
  equal(decodeApplied(fixture, plan), `${frontmatter}${body.replace('Before', 'Earlier')}`)
})

test('blocks separated edits whose single localized replacement would span protected raw bytes', () => {
  const frontmatter = '---\n---\n'
  const body = 'Before.\n<aside>keep raw</aside>\nAfter.\n'
  const raw = '<aside>keep raw</aside>'
  const rawStart = byteLength(frontmatter) + byteLength(body.slice(0, body.indexOf(raw)))
  const fixture = envelope(frontmatter, body, {
    protectedRange: { start: rawStart, endExclusive: rawStart + byteLength(raw) },
  })
  const prepared = prepareArticleBody(fixture)
  const edited = body.replace('Before', 'Earlier').replace('After', 'Later')

  equal(planBodySourceEdit(fixture, prepared, edited).kind, 'unsupported')
})

test('blocks edits to a body with mixed line endings', () => {
  const fixture = envelope('', 'One.\r\nTwo.\n')
  const prepared = prepareArticleBody(fixture)

  equal(planBodySourceEdit(fixture, prepared, 'Changed.\nTwo.\n').kind, 'unsupported')
})

test('rejects invalid UTF-8 instead of decoding replacement characters', () => {
  const valid = envelope('', 'safe')
  const invalidBytes = bytesToBase64(new Uint8Array([0xc3, 0x28]))
  const invalidRange = { start: 0, endExclusive: 2 }
  const fixture: ArticleSourceEnvelopeV1 = {
    ...valid,
    base: {
      ...valid.base,
      byteLength: 2,
      utf8Base64: invalidBytes,
    },
    projection: {
      ...valid.projection,
      body: {
        ...valid.projection.body,
        range: invalidRange,
        utf8Base64: invalidBytes,
      },
    },
  }

  throws(() => prepareArticleBody(fixture), 'valid UTF-8')
})

test('nonce client sends the launch nonce and surfaces a +json 409 conflict', async () => {
  const current = envelope('', 'Repository body.\n')
  let receivedNonce: string | null = null
  const originalFetch = globalThis.fetch
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    receivedNonce = new Headers(init?.headers).get('X-Tezuri-Nonce')
    return new Response(
      JSON.stringify({
        protocol: 'tezuri.source-patch-conflict',
        version: 1,
        articleId: current.article.id,
        expectedBaseSha256: 'opened-sha',
        current,
        message: 'The repository source changed.',
      }),
      {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      },
    )
  }) as typeof fetch

  try {
    const api = new HttpTezuriApi({ nonce: 'launch-secret' })
    let conflict: SourcePatchConflictError | undefined
    try {
      await api.applySourcePatches(current.article.id, {
        protocol: 'tezuri.source-patch-set',
        version: 1,
        articleId: current.article.id,
        relativePath: current.article.relativePath,
        baseSha256: 'opened-sha',
        operations: [],
      })
    } catch (error) {
      if (error instanceof SourcePatchConflictError) {
        conflict = error
      } else {
        throw error
      }
    }

    assert(conflict !== undefined, 'expected a typed source conflict')
    equal(conflict.conflict.current.projection.body.utf8Base64, current.projection.body.utf8Base64)
    equal(receivedNonce, 'launch-secret')
  } finally {
    globalThis.fetch = originalFetch
  }
})

test('runs the configured site proof without a browser-supplied command payload', async () => {
  let requestUrl: string | undefined
  let requestInit: RequestInit | undefined
  const originalFetch = globalThis.fetch
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    requestUrl = input.toString()
    requestInit = init
    return new Response(
      JSON.stringify({
        protocol: 'tezuri.site-proof-run',
        version: 1,
        runId: 'proof:test',
        status: 'passed',
        startedAt: '2026-08-13T12:00:00Z',
        completedAt: '2026-08-13T12:00:01Z',
        progress: {
          state: 'passed',
          completedCommands: 1,
          totalCommands: 1,
          currentCommandId: null,
        },
        result: {
          succeeded: true,
          commands: [
            {
              id: 'site-test',
              executable: 'npm',
              arguments: ['test'],
              status: 'passed',
              exitCode: 0,
              timedOut: false,
              durationMilliseconds: 1000,
              standardOutput: 'passed',
              standardError: '',
              standardOutputTruncated: false,
              standardErrorTruncated: false,
              outputDirectory: 'dist',
              outputDirectoryExists: true,
            },
          ],
        },
      }),
      { headers: { 'Content-Type': 'application/json' } },
    )
  }) as typeof fetch

  try {
    const api = new HttpTezuriApi({ nonce: 'launch-secret' })
    const receipt = await api.runSiteProof()

    assert(requestInit !== undefined, 'expected a proof request')
    equal(requestUrl, '/api/v1/proof/runs')
    equal(requestInit.method, 'POST')
    equal(requestInit.body, undefined)
    equal(new Headers(requestInit.headers).get('Content-Type'), null)
    equal(new Headers(requestInit.headers).get('X-Tezuri-Nonce'), 'launch-secret')
    equal(receipt.result.commands[0]?.outputDirectoryExists, true)
  } finally {
    globalThis.fetch = originalFetch
  }
})

test('uploads article media as nonce-protected multipart data with a browser boundary', async () => {
  let requestUrl: string | undefined
  let requestInit: RequestInit | undefined
  const originalFetch = globalThis.fetch
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    requestUrl = input.toString()
    requestInit = init
    return new Response(
      JSON.stringify({
        protocol: 'tezuri.media-asset-receipt',
        version: 1,
        articleId: 'patina:feature',
        originalFileName: 'proof image.png',
        fileName: `${'a'.repeat(64)}.png`,
        relativePath: `src/writing/patina/media/${'a'.repeat(64)}.png`,
        mediaType: 'image/png',
        sha256: 'a'.repeat(64),
        byteLength: 11,
        deduplicated: false,
      }),
      {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      },
    )
  }) as typeof fetch

  try {
    const api = new HttpTezuriApi({ nonce: 'launch-secret' })
    const file = new File(['png-fixture'], 'proof image.png', { type: 'image/png' })
    const receipt = await api.uploadArticleMedia('patina:feature', file)

    assert(requestInit !== undefined, 'expected a media upload request')
    assert(requestInit.body instanceof FormData, 'expected a multipart form body')
    const uploaded = requestInit.body.get('file')
    assert(uploaded instanceof File, 'expected one file form field')

    equal(requestUrl, '/api/v1/articles/patina%3Afeature/media')
    equal(requestInit.method, 'POST')
    equal(requestInit.credentials, 'same-origin')
    equal(new Headers(requestInit.headers).get('Accept'), 'application/json')
    equal(new Headers(requestInit.headers).get('Content-Type'), null)
    equal(new Headers(requestInit.headers).get('X-Tezuri-Nonce'), 'launch-secret')
    equal(uploaded.name, 'proof image.png')
    equal(uploaded.type, 'image/png')
    equal(await uploaded.text(), 'png-fixture')
    equal(receipt.relativePath, `src/writing/patina/media/${'a'.repeat(64)}.png`)
  } finally {
    globalThis.fetch = originalFetch
  }
})

test('sends exact nonce-protected Git status, plan, commit, and push requests', async () => {
  const headSha = '1'.repeat(40)
  const remoteSha = '2'.repeat(40)
  const planSha256 = '3'.repeat(64)
  const commitSha = '4'.repeat(40)
  const selectedPaths = ['src/writing/patina/index.md']
  const requests: { readonly url: string; readonly init: RequestInit | undefined }[] = []
  const originalFetch = globalThis.fetch
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = input.toString()
    requests.push({ url, init })

    let response: unknown
    switch (url) {
      case '/api/v1/git/status':
        response = {
          protocol: 'tezuri.git-repository-snapshot',
          version: 1,
          headSha,
          isUnborn: false,
          isDetached: false,
          branch: 'main',
          upstream: 'origin/main',
          remotes: ['origin'],
          remoteBranches: [{ remote: 'origin', branch: 'main', sha: remoteSha }],
          changes: [
            {
              path: selectedPaths[0],
              indexStatus: 'none',
              workTreeStatus: 'modified',
              allowed: true,
            },
          ],
        }
        break
      case '/api/v1/git/commit-plans':
        response = {
          protocol: 'tezuri.git-commit-plan',
          version: 1,
          headSha,
          branch: 'main',
          planSha256,
          selectedPaths,
          changes: [],
        }
        break
      case '/api/v1/git/commits':
        response = {
          protocol: 'tezuri.git-commit-receipt',
          version: 1,
          beforeSha: headSha,
          afterSha: commitSha,
          branch: 'main',
          planSha256,
          selectedPaths,
          created: true,
        }
        break
      case '/api/v1/git/pushes':
        response = {
          protocol: 'tezuri.git-push-receipt',
          version: 1,
          remote: 'origin',
          branch: 'main',
          localSha: commitSha,
          remoteBeforeSha: remoteSha,
          remoteAfterSha: commitSha,
          pushed: true,
        }
        break
      default:
        throw new Error(`Unexpected Git request: ${url}`)
    }

    return new Response(JSON.stringify(response), {
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  try {
    const api = new HttpTezuriApi({ nonce: 'launch-secret' })
    const planRequest = { selectedPaths }
    const commitRequest = {
      expectedHeadSha: headSha,
      expectedPlanSha256: planSha256,
      message: 'feat: publish patina',
      selectedPaths,
    }
    const pushRequest = {
      remote: 'origin',
      branch: 'main',
      expectedHeadSha: commitSha,
      expectedRemoteSha: remoteSha,
    }

    const snapshot = await api.inspectGit()
    const plan = await api.planGitCommit(planRequest)
    const commit = await api.prepareGitCommit(commitRequest)
    const push = await api.pushGit(pushRequest)

    equal(requests.length, 4)
    const inspectRequest = requests[0]
    const planRequestShape = requests[1]
    const commitRequestShape = requests[2]
    const pushRequestShape = requests[3]
    assert(inspectRequest !== undefined, 'expected a Git status request')
    assert(planRequestShape !== undefined, 'expected a Git plan request')
    assert(commitRequestShape !== undefined, 'expected a Git commit request')
    assert(pushRequestShape !== undefined, 'expected a Git push request')

    equal(inspectRequest.url, '/api/v1/git/status')
    equal(inspectRequest.init?.method, 'GET')
    equal(inspectRequest.init?.body, undefined)
    equal(new Headers(inspectRequest.init?.headers).get('Content-Type'), null)

    equal(planRequestShape.url, '/api/v1/git/commit-plans')
    equal(planRequestShape.init?.method, 'POST')
    equal(planRequestShape.init?.body, JSON.stringify(planRequest))

    equal(commitRequestShape.url, '/api/v1/git/commits')
    equal(commitRequestShape.init?.method, 'POST')
    equal(commitRequestShape.init?.body, JSON.stringify(commitRequest))

    equal(pushRequestShape.url, '/api/v1/git/pushes')
    equal(pushRequestShape.init?.method, 'POST')
    equal(pushRequestShape.init?.body, JSON.stringify(pushRequest))

    for (const request of requests) {
      equal(request.init?.credentials, 'same-origin')
      equal(new Headers(request.init?.headers).get('Accept'), 'application/json')
      equal(new Headers(request.init?.headers).get('X-Tezuri-Nonce'), 'launch-secret')
    }
    for (const request of requests.slice(1)) {
      equal(new Headers(request.init?.headers).get('Content-Type'), 'application/json')
    }

    equal(snapshot.remoteBranches[0]?.sha, remoteSha)
    equal(plan.planSha256, planSha256)
    equal(commit.afterSha, commitSha)
    equal(push.remoteAfterSha, commitSha)
  } finally {
    globalThis.fetch = originalFetch
  }
})

for (const { name, run } of tests) {
  try {
    await run()
    console.log(`ok - ${name}`)
  } catch (error) {
    console.error(`not ok - ${name}`)
    throw error
  }
}

console.log(`1..${tests.length}`)

function test(name: string, run: () => void | Promise<void>): void {
  tests.push({ name, run })
}

interface EnvelopeOptions {
  readonly bom?: boolean
  readonly protectedRange?: SourceByteRangeV1
}

function envelope(
  frontmatter: string,
  body: string,
  options: EnvelopeOptions = {},
): ArticleSourceEnvelopeV1 {
  const prefixBytes = options.bom === true ? new Uint8Array([0xef, 0xbb, 0xbf]) : new Uint8Array()
  const frontmatterBytes = encoder.encode(frontmatter)
  const bodyBytes = encoder.encode(body)
  const canonical = concat(prefixBytes, frontmatterBytes, bodyBytes)
  const bodyStart = prefixBytes.byteLength + frontmatterBytes.byteLength
  const bodyRange = { start: bodyStart, endExclusive: canonical.byteLength }
  const segments: ArticleSourceSegmentV1[] = [
    {
      kind: 'rich',
      id: 'body',
      range: bodyRange,
      source: { range: bodyRange, sha256: 'body', utf8Base64: bytesToBase64(bodyBytes) },
      syntax: 'gfm',
    },
  ]

  if (options.protectedRange !== undefined) {
    const protectedBytes = canonical.slice(
      options.protectedRange.start,
      options.protectedRange.endExclusive,
    )
    segments.push({
      kind: 'protected-raw',
      id: 'raw',
      range: options.protectedRange,
      source: {
        range: options.protectedRange,
        sha256: 'raw',
        utf8Base64: bytesToBase64(protectedBytes),
      },
      syntaxHint: 'html',
      notice: 'keep',
    })
  }

  return {
    protocol: 'tezuri.article-source',
    version: 1,
    article: {
      id: 'patina',
      slug: 'patina',
      displayTitle: 'Patina',
      relativePath: 'src/writing/patina/index.md',
    },
    base: {
      encoding: 'utf-8',
      bom: options.bom === true ? 'utf-8' : 'none',
      lineEndings: canonical.includes(13) ? 'crlf' : canonical.includes(10) ? 'lf' : 'none',
      byteLength: canonical.byteLength,
      sha256: 'base-sha',
      utf8Base64: bytesToBase64(canonical),
    },
    projection: {
      frontmatter: {
        range: {
          start: prefixBytes.byteLength,
          endExclusive: bodyStart,
        },
        sha256: 'frontmatter',
        utf8Base64: bytesToBase64(frontmatterBytes),
      },
      body: {
        range: bodyRange,
        sha256: 'body',
        utf8Base64: bytesToBase64(bodyBytes),
      },
      segments,
    },
    capabilities: {
      richEditing: 'available',
      protectedSegmentCount: options.protectedRange === undefined ? 0 : 1,
    },
    diagnostics: [],
  }
}

function expectReady(plan: BodySourceEditPlan): Extract<BodySourceEditPlan, { kind: 'ready' }> {
  if (plan.kind !== 'ready') {
    throw new Error(`Expected a ready plan, received ${plan.kind}.`)
  }
  return plan
}

function decodeApplied(
  fixture: ArticleSourceEnvelopeV1,
  plan: Extract<BodySourceEditPlan, { kind: 'ready' }>,
): string {
  const canonical = base64ToBytes(fixture.base.utf8Base64)
  const replacement = base64ToBytes(plan.operation.replacementUtf8Base64)
  const output = concat(
    canonical.slice(0, plan.operation.range.start),
    replacement,
    canonical.slice(plan.operation.range.endExclusive),
  )
  const withoutBom = fixture.base.bom === 'utf-8' ? output.slice(3) : output
  return decoder.decode(withoutBom)
}

function byteLength(value: string): number {
  return encoder.encode(value).byteLength
}

function concat(...parts: readonly Uint8Array[]): Uint8Array {
  const output = new Uint8Array(parts.reduce((length, part) => length + part.byteLength, 0))
  let offset = 0
  for (const part of parts) {
    output.set(part, offset)
    offset += part.byteLength
  }
  return output
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary)
}

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value)
  return Uint8Array.from(binary, (character) => character.charCodeAt(0))
}

function equal<T>(actual: T, expected: T): void {
  if (!Object.is(actual, expected)) {
    throw new Error(`Expected ${String(expected)}, received ${String(actual)}.`)
  }
}

function assert(condition: boolean, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function throws(run: () => void, messageFragment: string): void {
  try {
    run()
  } catch (error) {
    if (error instanceof Error && error.message.includes(messageFragment)) {
      return
    }
    throw error
  }
  throw new Error(`Expected an error containing ${messageFragment}.`)
}
