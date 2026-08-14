import assert from 'node:assert/strict'
import { test } from 'node:test'

import {
  SESSION_NONCE_KEY,
  resolveLaunchNonce,
  type NonceStorage,
} from '../src/api/launch-nonce.ts'

function storage(initial?: string): NonceStorage & { readonly items: Map<string, string> } {
  const items = new Map<string, string>()
  if (initial !== undefined) {
    items.set(SESSION_NONCE_KEY, initial)
  }

  return {
    items,
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
  }
}

test('takes the nonce from the launch URL and reports the scrubbed address', () => {
  const store = storage()

  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/?nonce=abc123#top', store)

  assert.equal(resolved.nonce, 'abc123')
  assert.equal(resolved.scrubbedUrl, '/#top')
  assert.equal(store.items.get(SESSION_NONCE_KEY), 'abc123')
})

test('keeps every other query parameter when it removes the nonce', () => {
  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/edit?nonce=abc&open=patina', storage())

  assert.equal(resolved.scrubbedUrl, '/edit?open=patina')
})

test('a refresh recovers the nonce from tab storage', () => {
  const store = storage('remembered')

  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/', store)

  assert.equal(resolved.nonce, 'remembered')
  assert.equal(resolved.scrubbedUrl, undefined)
})

test('an empty nonce grants nothing but is still removed from the address', () => {
  const store = storage()

  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/?nonce=', store)

  assert.equal(resolved.nonce, undefined)
  assert.equal(resolved.scrubbedUrl, '/')
  assert.equal(store.items.size, 0)
})

test('a URL nonce wins over one left in storage', () => {
  const store = storage('stale')

  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/?nonce=fresh', store)

  assert.equal(resolved.nonce, 'fresh')
  assert.equal(store.items.get(SESSION_NONCE_KEY), 'fresh')
})

test('no nonce anywhere leaves the application read-only', () => {
  const resolved = resolveLaunchNonce('http://127.0.0.1:8080/', storage())

  assert.equal(resolved.nonce, undefined)
  assert.equal(resolved.scrubbedUrl, undefined)
})

test('storage that refuses to read or write still yields a usable tab', () => {
  const refusing: NonceStorage = {
    getItem: () => {
      throw new Error('storage is partitioned')
    },
    setItem: () => {
      throw new Error('storage is partitioned')
    },
  }

  assert.equal(resolveLaunchNonce('http://127.0.0.1:8080/?nonce=abc', refusing).nonce, 'abc')
  assert.equal(resolveLaunchNonce('http://127.0.0.1:8080/', refusing).nonce, undefined)
})

test('absent storage is not an error', () => {
  assert.equal(resolveLaunchNonce('http://127.0.0.1:8080/?nonce=abc', undefined).nonce, 'abc')
})

test('an unparseable address does not throw, and does not revoke a remembered nonce', () => {
  const resolved = resolveLaunchNonce('not a url', storage('remembered'))

  assert.equal(resolved.nonce, 'remembered')
  assert.equal(resolved.scrubbedUrl, undefined)
})
