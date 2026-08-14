import type { ArticleSummary } from '../api/articles'

export type PostFilter = 'all' | 'draft' | 'published'

export interface PostsRailCallbacks {
  readonly onOpen: (articleId: string) => void
  readonly onDelete: (article: ArticleSummary) => void
}

export interface PostsRailState {
  readonly articles: readonly ArticleSummary[]
  readonly activeArticleId: string | undefined
  readonly query: string
  readonly filter: PostFilter
}

const GROUP_ORDER: readonly { state: string; label: string }[] = [
  { state: 'draft', label: 'Drafts' },
  { state: 'published', label: 'Published' },
  ]

export function filterPosts(state: PostsRailState): readonly ArticleSummary[] {
  const query = state.query.trim().toLocaleLowerCase()
  return state.articles.filter((article) => {
    if (state.filter !== 'all' && (article.draft ? 'draft' : 'published') !== state.filter) {
      return false
    }
    if (query === '') {
      return true
    }
    return `${article.title} ${article.id}`.toLocaleLowerCase().includes(query)
  })
}

/**
 * Renders the posts list grouped by publication state, newest first inside each group. Grouping is
 * what turns a flat file listing into something a person can reason about: what is still mine, and
 * what is already public.
 */
export function renderPostsRail(
  container: HTMLElement,
  emptyNote: HTMLElement,
  state: PostsRailState,
  callbacks: PostsRailCallbacks,
): void {
  const visible = filterPosts(state)
  container.replaceChildren()

  if (visible.length === 0) {
    emptyNote.hidden = false
    emptyNote.textContent =
      state.articles.length === 0
        ? 'No posts in this repository yet.'
        : 'No posts match this search.'
    return
  }

  emptyNote.hidden = true

  for (const group of GROUP_ORDER) {
    const members = visible
      .filter((article) => (article.draft ? 'draft' : 'published') === group.state)
      .sort(byMostRecent)
    if (members.length === 0) {
      continue
    }

    const section = document.createElement('section')
    section.className = 'post-group'

    const heading = document.createElement('p')
    heading.className = 'post-group-heading'
    heading.textContent = `${group.label} · ${members.length}`
    section.append(heading)

    const list = document.createElement('ul')
    list.className = 'post-list'
    for (const article of members) {
      list.append(renderPostRow(article, state.activeArticleId, callbacks))
    }

    section.append(list)
    container.append(section)
  }
}

function renderPostRow(
  article: ArticleSummary,
  activeArticleId: string | undefined,
  callbacks: PostsRailCallbacks,
): HTMLLIElement {
  const item = document.createElement('li')
  item.className = 'post-row'

  const open = document.createElement('button')
  open.type = 'button'
  open.className = 'post-open'
  if (article.id === activeArticleId) {
    open.classList.add('is-active')
    open.setAttribute('aria-current', 'true')
  }
  open.addEventListener('click', () => callbacks.onOpen(article.id))

  const title = document.createElement('span')
  title.className = 'post-title'
  title.textContent = article.title

  const meta = document.createElement('span')
  meta.className = 'post-meta'
  meta.append(renderTime(article), document.createTextNode(' · '), renderSlug(article))

  open.append(title, meta)

  const actions = document.createElement('div')
  actions.className = 'post-actions'

  const remove = document.createElement('button')
  remove.type = 'button'
  remove.className = 'post-action post-action--danger'
  remove.textContent = '×'
  remove.title = `Delete ${article.title}`
  remove.setAttribute('aria-label', `Delete ${article.title}`)
  remove.addEventListener('click', () => callbacks.onDelete(article))

  actions.append(remove)
  item.append(open, actions)
  return item
}

function renderSlug(article: ArticleSummary): HTMLElement {
  const slug = document.createElement('span')
  slug.className = 'post-slug'
  slug.textContent = article.id
  return slug
}

function renderTime(article: ArticleSummary): HTMLElement {
  if (article.updatedAt === undefined) {
    const unknown = document.createElement('span')
    unknown.textContent = 'No date'
    return unknown
  }

  const date = new Date(article.updatedAt)
  if (Number.isNaN(date.valueOf())) {
    const unknown = document.createElement('span')
    unknown.textContent = 'No date'
    return unknown
  }

  const value = document.createElement('time')
  value.dateTime = article.updatedAt
  value.textContent = describeRelativeTime(date)
  value.title = date.toLocaleString()
  return value
}

/**
 * Recent work reads better as "3 hours ago" than as a date; older work reads better as a date.
 */
export function describeRelativeTime(date: Date, now: Date = new Date()): string {
  const seconds = Math.round((now.valueOf() - date.valueOf()) / 1000)
  if (seconds < 45) {
    return 'just now'
  }

  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) {
    return formatter.format(-minutes, 'minute')
  }

  const hours = Math.round(minutes / 60)
  if (hours < 24) {
    return formatter.format(-hours, 'hour')
  }

  const days = Math.round(hours / 24)
  if (days <= 6) {
    return formatter.format(-days, 'day')
  }

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  }).format(date)
}

function byMostRecent(left: ArticleSummary, right: ArticleSummary): number {
  const leftTime = Date.parse(left.updatedAt ?? '')
  const rightTime = Date.parse(right.updatedAt ?? '')
  if (Number.isNaN(leftTime) && Number.isNaN(rightTime)) {
    return left.title.localeCompare(right.title)
  }
  if (Number.isNaN(leftTime)) {
    return 1
  }
  if (Number.isNaN(rightTime)) {
    return -1
  }
  return rightTime - leftTime
}
