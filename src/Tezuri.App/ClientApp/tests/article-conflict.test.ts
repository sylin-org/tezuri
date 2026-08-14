import assert from 'node:assert/strict'
import { test } from 'node:test'

import { ArticleConflictError, asArticleConflict } from '../src/api/article-conflict.ts'
import type { Article } from '../src/api/article-types.ts'

const current: Article = {
  id: 'patina',
  title: 'Patina',
  subtitle: null,
  body: 'The version another session saved.',
  draft: true,
  date: null,
  tags: [],
  revision: 'newer',
  updatedAt: '2026-08-14T09:00:00Z',
}

function conflict(problem: unknown): unknown {
  return { status: 409, problem }
}

test('recognises the revision conflict and carries the current article', () => {
  const recognised = asArticleConflict(
    conflict({ title: 'This article changed in another Tezuri session.', detail: 'Reopen it.', current }),
  )

  assert.notEqual(recognised, undefined)
  assert.equal(recognised?.detail, 'Reopen it.')
  assert.equal(recognised?.current.revision, 'newer')
})

test('leaves other failures alone so a server fault is never blamed on another session', () => {
  const problem = { detail: 'Nope.', current }

  assert.equal(asArticleConflict({ status: 500, problem }), undefined)
  assert.equal(asArticleConflict({ status: 403, problem }), undefined)
  assert.equal(asArticleConflict(new Error('the network went away')), undefined)
  assert.equal(asArticleConflict(undefined), undefined)
  assert.equal(asArticleConflict(null), undefined)
  assert.equal(asArticleConflict('409'), undefined)
})

test('a 409 without a usable body is not treated as a conflict', () => {
  assert.equal(asArticleConflict(conflict(undefined)), undefined)
  assert.equal(asArticleConflict(conflict(null)), undefined)
  assert.equal(asArticleConflict(conflict('conflict')), undefined)
  assert.equal(asArticleConflict(conflict({ detail: 'Reopen it.' })), undefined)
  assert.equal(asArticleConflict(conflict({ current })), undefined)
  assert.equal(asArticleConflict(conflict({ detail: 7, current })), undefined)
  assert.equal(asArticleConflict(conflict({ detail: 'Reopen it.', current: null })), undefined)
})

test('the error names itself and keeps the article the writer can recover from', () => {
  const error = new ArticleConflictError('Reopen it.', current)

  assert.ok(error instanceof Error)
  assert.equal(error.name, 'ArticleConflictError')
  assert.equal(error.message, 'Reopen it.')
  assert.equal(error.current.body, 'The version another session saved.')
})
