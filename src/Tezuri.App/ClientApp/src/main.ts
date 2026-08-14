import './styles.css'

import { hasLaunchNonce, sessionApi, RequestError } from './api/session-api'
import { articles as api, ArticleConflictError, type Article, type ArticleSummary } from './api/articles'
import { createMediaUploader } from './editor/media-uploader'
import { MarkdownEditor } from './editor/markdown-editor'
import {
  type ProofCommandResult,
  type ProofRun,
  type ProofStatus,
} from './api/proof-types'
import { renderPostsRail, type PostFilter } from './views/posts-rail'
import { PublishPanel, type StatusTone } from './views/publish-panel'
import { openPrompt } from './views/prompt'

type Mode = 'write' | 'proof' | 'publish'

const el = {
  app: must<HTMLElement>('#app'),
  announcer: must<HTMLElement>('#announcer'),
  toast: must<HTMLElement>('#toast'),
  workspacePathButton: must<HTMLButtonElement>('#workspace-path-button'),
  workspacePath: must<HTMLElement>('#workspace-path'),
  modeWrite: must<HTMLButtonElement>('#mode-write'),
  modeProof: must<HTMLButtonElement>('#mode-proof'),
  modePublish: must<HTMLButtonElement>('#mode-publish'),
  viewWrite: must<HTMLElement>('#view-write'),
  viewProof: must<HTMLElement>('#view-proof'),
  viewPublish: must<HTMLElement>('#view-publish'),
  newPostButton: must<HTMLButtonElement>('#new-post-button'),
  emptyNewButton: must<HTMLButtonElement>('#empty-new-button'),
  postSearch: must<HTMLInputElement>('#post-search'),
  postGroups: must<HTMLElement>('#post-groups'),
  railEmpty: must<HTMLElement>('#rail-empty'),
  railStatus: must<HTMLElement>('#rail-status'),
  documentSurface: must<HTMLElement>('#document-surface'),
  emptyStage: must<HTMLElement>('#empty-stage'),
  articlePath: must<HTMLElement>('#article-path'),
  saveState: must<HTMLElement>('#save-state'),
  sourceToggle: must<HTMLButtonElement>('#source-toggle'),

  documentSource: must<HTMLElement>('#document-source'),
  titleInput: must<HTMLTextAreaElement>('#article-title-input'),
  subtitleInput: must<HTMLTextAreaElement>('#article-subtitle-input'),
  milkdownHost: must<HTMLElement>('#milkdown-editor'),
  sourceInput: must<HTMLTextAreaElement>('#markdown-source'),
  documentCount: must<HTMLElement>('#document-count'),
  documentAuthority: must<HTMLElement>('#document-authority'),
  conflictPanel: must<HTMLElement>('#conflict-panel'),
  conflictMessage: must<HTMLElement>('#conflict-message'),
  conflictCurrent: must<HTMLElement>('#conflict-current-source'),
  proofStatus: must<HTMLElement>('#proof-status'),
  proofNote: must<HTMLElement>('#proof-note'),
  proofEvidence: must<HTMLDListElement>('#proof-evidence'),
  proofRunButton: must<HTMLButtonElement>('#proof-run-button'),
}

let editor: MarkdownEditor | undefined
let editorFailed = false
let articles: readonly ArticleSummary[] = []
let activeArticleId: string | undefined
let opened: Article | undefined
let editorMarkdown = ''
let sourceVisible = false
let mode: Mode = 'write'
void mode
let filter: PostFilter = 'all'
let openController: AbortController | undefined
let openGeneration = 0
let busyCount = 0
let saving = false
let saveTimer: number | undefined
let proofRunning = false

const publish = new PublishPanel(
  {
    status: must<HTMLElement>('#git-status'),
    note: must<HTMLElement>('#git-note'),
    summary: must<HTMLElement>('#git-summary'),
    railStatus: el.railStatus,
    countBadge: must<HTMLElement>('#publish-count'),
    pathFieldset: must<HTMLFieldSetElement>('#git-path-fieldset'),
    pathLegend: must<HTMLElement>('#git-path-legend'),
    changes: must<HTMLElement>('#git-changes'),
    commitMessage: must<HTMLTextAreaElement>('#git-commit-message'),
    refreshButton: must<HTMLButtonElement>('#git-refresh-button'),
    reviewButton: must<HTMLButtonElement>('#git-review-button'),
    planReview: must<HTMLElement>('#git-plan-review'),
    planSummary: must<HTMLElement>('#git-plan-summary'),
    planPaths: must<HTMLUListElement>('#git-plan-paths'),
    commitButton: must<HTMLButtonElement>('#git-commit-button'),
    pushPanel: must<HTMLElement>('#git-push-panel'),
    pushSummary: must<HTMLElement>('#git-push-summary'),
    pushButton: must<HTMLButtonElement>('#git-push-button'),
    alert: must<HTMLElement>('#git-alert'),
  },
  {
    announce,
    setBusy,
    hasUnsavedWork: () => hasUnsavedChanges(),
    formatError,
    setStatusPill,
  },
)

wireEvents()
void start()

async function start(): Promise<void> {
  if (!hasLaunchNonce) {
    toast(
      'This tab has no launch key, so it can read but not save. Reopen the link Tezuri printed when it started.',
      'warning',
    )
  }

  await loadArticles()
  void publish.refresh(false)
}

function wireEvents(): void {
  el.modeWrite.addEventListener('click', () => setMode('write'))
  el.modeProof.addEventListener('click', () => setMode('proof'))
  el.modePublish.addEventListener('click', () => setMode('publish'))

  el.newPostButton.addEventListener('click', () => void createPost())
  el.emptyNewButton.addEventListener('click', () => void createPost())
  el.postSearch.addEventListener('input', () => paintRail())

  for (const chip of document.querySelectorAll<HTMLButtonElement>('[data-filter]')) {
    chip.addEventListener('click', () => {
      filter = (chip.dataset.filter ?? 'all') as PostFilter
      for (const other of document.querySelectorAll('[data-filter]')) {
        other.classList.toggle('is-selected', other === chip)
      }
      paintRail()
    })
  }

  el.workspacePathButton.addEventListener('click', () => void copyWorkspacePath())
  el.sourceToggle.addEventListener('click', () => toggleSource())

  el.titleInput.addEventListener('input', () => {
    autoGrow(el.titleInput)
    scheduleSave()
  })
  el.subtitleInput.addEventListener('input', () => {
    autoGrow(el.subtitleInput)
    scheduleSave()
  })
  // Enter in the title moves into the writing, the way a document behaves.
  for (const field of [el.titleInput, el.subtitleInput]) {
    field.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        if (field === el.titleInput) {
          el.subtitleInput.focus()
        } else {
          editor?.focus()
        }
      }
    })
  }

  el.sourceInput.addEventListener('input', () => {
    editorMarkdown = el.sourceInput.value
    updateCount()
    scheduleSave()
  })

  el.proofRunButton.addEventListener('click', () => void runProof())

  document.addEventListener('keydown', (event) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault()
      void saveNow()
    }
  })

  window.addEventListener('beforeunload', (event) => {
    if (hasUnsavedChanges()) {
      event.preventDefault()
    }
  })

  window.addEventListener('pagehide', () => {
    openController?.abort()
    void editor?.destroy()
  })
}

/* ─────────────────────────  posts  ───────────────────────── */

async function loadArticles(selectId?: string): Promise<void> {
  setBusy(true)
  try {
    articles = await api.list()
    paintRail()

    const target = selectId ?? activeArticleId ?? articles[0]?.id
    if (target === undefined) {
      showEmptyWorkspace()
    } else {
      await openArticle(target)
    }
  } catch (error) {
    el.railEmpty.hidden = false
    el.railEmpty.textContent = formatError(error)
    showEmptyWorkspace()
  } finally {
    setBusy(false)
  }
}

function paintRail(): void {
  renderPostsRail(
    el.postGroups,
    el.railEmpty,
    {
      articles,
      activeArticleId,
      query: el.postSearch.value,
      filter,
    },
    {
      onOpen: (articleId) => void openArticle(articleId),
      onDelete: (article) => void deletePost(article),
    },
  )
}

async function createPost(): Promise<void> {
  if (!hasLaunchNonce) {
    toast('Reopen the launch link Tezuri printed to create posts.', 'warning')
    return
  }

  const title = await openPrompt({
    title: 'New post',
    copy: 'The title names the folder and the file. You can rename it later.',
    label: 'Title',
    confirmLabel: 'Create',
  })
  if (title === undefined) {
    return
  }

  setBusy(true)
  try {
    const created = await api.create(title)
    await loadArticles(created.id)
    setMode('write')
    el.titleInput.focus()
    toast(`Created ${`src/writing/${created.id}/index.md`}`, 'success')
    void publish.refresh(false)
  } catch (error) {
    toast(formatError(error), 'danger')
  } finally {
    setBusy(false)
  }
}

async function deletePost(article: ArticleSummary): Promise<void> {
  if (!hasLaunchNonce) {
    toast('Reopen the launch link Tezuri printed to delete posts.', 'warning')
    return
  }

  const confirmation = await openPrompt({
    title: `Delete ${article.title}?`,
    copy: `This removes ${article.id}/ and the media it owns. Git still has the history, so a committed post can be recovered. Type the folder name to confirm.`,
    label: 'Folder name',
    confirmLabel: 'Delete',
    destructive: true,
    requireExactly: article.id,
  })
  if (confirmation === undefined) {
    return
  }

  setBusy(true)
  try {
    await api.remove(article.id)
    if (activeArticleId === article.id) {
      activeArticleId = undefined
      opened = undefined
    }
    await loadArticles()
    toast(`Deleted ${article.title}`, 'default')
    void publish.refresh(false)
  } catch (error) {
    toast(formatError(error), 'danger')
  } finally {
    setBusy(false)
  }
}

/* ─────────────────────────  document  ───────────────────────── */

async function openArticle(id: string): Promise<void> {
  if (saving) {
    return
  }

  openController?.abort()
  const controller = new AbortController()
  openController = controller
  const generation = ++openGeneration
  setBusy(true)

  try {
    const article = await api.get(id, controller.signal)
    if (generation !== openGeneration) {
      return
    }

    await adopt(article)
    announce(`${article.title} opened.`)
  } catch (error) {
    if (!isAbortError(error) && generation === openGeneration) {
      toast(formatError(error), 'danger')
    }
  } finally {
    if (generation === openGeneration) {
      setBusy(false)
    }
  }
}

async function adopt(article: Article): Promise<void> {
  opened = article
  activeArticleId = article.id
  editorMarkdown = article.body

  el.emptyStage.hidden = true
  el.documentSurface.hidden = false
  el.conflictPanel.hidden = true
  el.app.dataset.state = 'ready'

  el.articlePath.textContent = `src/writing/${article.id}/index.md`
  el.documentAuthority.textContent = article.draft ? 'Draft' : 'Published'

  el.titleInput.value = article.title
  el.subtitleInput.value = article.subtitle ?? ''
  el.titleInput.readOnly = !hasLaunchNonce
  el.subtitleInput.readOnly = !hasLaunchNonce
  autoGrow(el.titleInput)
  autoGrow(el.subtitleInput)

  el.sourceInput.value = editorMarkdown
  el.sourceInput.readOnly = !hasLaunchNonce

  await syncEditor(article.body)
  updateCount()
  setSaveState('saved')
  paintRail()
}

async function syncEditor(markdown: string): Promise<void> {
  const preview = resolveOwnedMediaForPreview(markdown)
  if (editor !== undefined) {
    editor.replaceMarkdown(preview)
    return
  }
  if (editorFailed) {
    return
  }

  try {
    editor = await MarkdownEditor.create({
      host: el.milkdownHost,
      markdown: preview,
      accessibleName: 'Article body',
      readonly: !hasLaunchNonce,
      uploader: createMediaUploader({
        articleId: () => activeArticleId,
        articleRelativePath: () => (activeArticleId === undefined ? undefined : `src/writing/${activeArticleId}/index.md`),
        upload: (articleId, file) => api.uploadMedia(articleId, file),
        onProblem: (message) => toast(message, 'danger'),
        onStored: (receipt) =>
          toast(
            receipt.deduplicated
              ? `${receipt.fileName} was already in this post.`
              : `Stored ${receipt.fileName} in this post.`,
            'success',
          ),
      }),
      onChange: (markdown) => {
        editorMarkdown = restoreOwnedMediaPaths(markdown)
        el.sourceInput.value = editorMarkdown
        updateCount()
        scheduleSave()
      },
    })
  } catch (error) {
    console.error('The rich editor could not start.', error)
    editorFailed = true
    toast('The rich editor could not start. Markdown is still available.', 'warning')
    toggleSource(true)
  }
}

function showEmptyWorkspace(): void {
  activeArticleId = undefined
  opened = undefined
  el.documentSurface.hidden = true
  el.emptyStage.hidden = false
  el.app.dataset.state = 'empty'
  paintRail()
}

/* ─────────────────────────  saving  ───────────────────────── */

function hasUnsavedChanges(): boolean {
  if (opened === undefined) {
    return false
  }
  return (
    editorMarkdown !== opened.body ||
    el.titleInput.value.trim() !== opened.title ||
    el.subtitleInput.value.trim() !== (opened.subtitle ?? '')
  )
}

function scheduleSave(): void {
  if (!hasLaunchNonce || opened === undefined) {
    return
  }

  setSaveState(hasUnsavedChanges() ? 'unsaved' : 'saved')
  if (saveTimer !== undefined) {
    window.clearTimeout(saveTimer)
  }
  saveTimer = window.setTimeout(() => void saveNow(), 1200)
}

/** Autosave writes the article. It never commits, pushes, or publishes. */
async function saveNow(): Promise<void> {
  if (saveTimer !== undefined) {
    window.clearTimeout(saveTimer)
    saveTimer = undefined
  }

  const article = opened
  if (!hasLaunchNonce || saving || article === undefined) {
    return
  }
  if (!hasUnsavedChanges()) {
    setSaveState('saved')
    return
  }

  saving = true
  setSaveState('saving')

  try {
    const saved = await api.save(article.id, {
      title: el.titleInput.value.trim(),
      subtitle: el.subtitleInput.value.trim() || null,
      body: editorMarkdown,
      draft: article.draft,
      date: article.date,
      tags: article.tags,
      revision: article.revision,
    })

    opened = saved
    editorMarkdown = saved.body
    articles = articles.map((entry) =>
      entry.id === saved.id
        ? { ...entry, title: saved.title, subtitle: saved.subtitle, updatedAt: saved.updatedAt, revision: saved.revision }
        : entry,
    )
    setSaveState('saved')
    paintRail()
    void publish.refresh(false)
  } catch (error) {
    if (error instanceof ArticleConflictError) {
      showConflict(error)
    } else {
      setSaveState('failed', formatError(error))
    }
  } finally {
    saving = false
    publish.updateControls()
  }
}

function setSaveState(state: 'saved' | 'saving' | 'unsaved' | 'failed' | 'blocked', detail?: string): void {
  el.saveState.classList.remove('is-unsaved', 'is-failed')
  switch (state) {
    case 'saved':
      el.saveState.textContent = opened === undefined ? 'Ready' : 'Saved'
      break
    case 'saving':
      el.saveState.textContent = 'Saving…'
      break
    case 'unsaved':
      el.saveState.textContent = 'Unsaved'
      el.saveState.classList.add('is-unsaved')
      break
    case 'failed':
      el.saveState.textContent = 'Not saved'
      el.saveState.classList.add('is-failed')
      toast(detail ?? 'The file could not be saved.', 'danger')
      break
    case 'blocked':
      el.saveState.textContent = 'Not saved'
      el.saveState.classList.add('is-failed')
      toast(detail ?? 'This edit cannot be written safely.', 'warning')
      break
  }
}

function showConflict(error: ArticleConflictError): void {
  el.conflictPanel.hidden = false
  el.conflictMessage.textContent = error.message
  el.conflictCurrent.textContent = error.current.body
  setSaveState('failed', 'Another Tezuri session saved this article.')
  announce('Save paused. Your draft and the newer version are both on screen.')
}

/* ─────────────────────────  proof  ───────────────────────── */

async function runProof(): Promise<void> {
  if (!hasLaunchNonce || proofRunning) {
    return
  }

  proofRunning = true
  el.proofRunButton.disabled = true
  el.proofRunButton.textContent = 'Running…'
  setStatusPill(el.proofStatus, 'Running', 'quiet')
  el.proofNote.textContent = 'Running your repository’s declared build in an isolated copy.'
  el.proofEvidence.replaceChildren()
  setBusy(true)

  try {
    const receipt = await sessionApi.runSiteProof()
    if (!Array.isArray(receipt.result.commands)) {
      throw new Error('The workspace returned an unsupported proof response.')
    }
    renderProof(receipt)
  } catch (error) {
    const message = formatError(error)
    setStatusPill(el.proofStatus, 'Failed', 'danger')
    el.proofNote.textContent = `Proof could not run. ${message}`
    announce(`Proof failed. ${message}`)
  } finally {
    proofRunning = false
    el.proofRunButton.disabled = false
    el.proofRunButton.textContent = 'Run proof'
    setBusy(false)
  }
}

function renderProof(receipt: ProofRun): void {
  const passed = receipt.status === 'passed' && receipt.result.succeeded
  setStatusPill(el.proofStatus, passed ? 'Passed' : 'Failed', passed ? 'success' : 'danger')
  el.proofNote.textContent = passed
    ? `Your site built cleanly. ${receipt.progress.completedCommands} of ${receipt.progress.totalCommands} commands passed.`
    : `The build did not pass. ${receipt.progress.completedCommands} of ${receipt.progress.totalCommands} commands completed.`

  el.proofEvidence.replaceChildren()
  for (const command of receipt.result.commands) {
    appendEvidence(
      command.id,
      describeCommand(command),
      command.status === 'passed' ? 'passed' : 'failed',
      commandOutput(command),
    )
  }
  announce(passed ? 'Proof passed.' : 'Proof failed. The build output is on screen.')
}

function describeCommand(command: ProofCommandResult): string {
  const line = [command.executable, ...command.arguments].join(' ')
  const exit = command.exitCode === null ? '' : ` · exit ${command.exitCode}`
  const output =
    command.outputDirectory === null
      ? 'no output directory declared'
      : `${command.outputDirectory} ${command.outputDirectoryExists ? 'found' : 'missing'}`
  return `${line} · ${statusLabel(command.status)}${exit} · ${command.durationMilliseconds} ms · ${output}`
}

function commandOutput(command: ProofCommandResult): string {
  const out = command.standardOutput.trim()
  const err = command.standardError.trim()
  if (out === '') {
    return err
  }
  if (err === '') {
    return out
  }
  return `${out}\n\n--- standard error ---\n${err}`
}

function statusLabel(status: ProofStatus): string {
  switch (status) {
    case 'passed':
      return 'passed'
    case 'timed-out':
      return 'timed out'
    case 'start-failed':
      return 'could not start'
    case 'failed':
      return 'failed'
  }
}

function appendEvidence(
  title: string,
  detail: string,
  tone: 'pending' | 'passed' | 'failed',
  output?: string,
): void {
  const row = document.createElement('div')
  const term = document.createElement('dt')
  const description = document.createElement('dd')
  const dot = document.createElement('span')
  term.textContent = title
  dot.className = `evidence-dot evidence-dot--${tone}`
  dot.setAttribute('aria-hidden', 'true')
  description.append(dot, detail)

  const captured = output?.trim()
  if (captured !== undefined && captured !== '') {
    const details = document.createElement('details')
    details.className = 'evidence-details'
    details.open = tone === 'failed'
    const summary = document.createElement('summary')
    summary.textContent = 'Build output'
    const pre = document.createElement('pre')
    pre.className = 'evidence-output'
    pre.tabIndex = 0
    pre.textContent = captured
    details.append(summary, pre)
    description.append(details)
  }

  row.append(term, description)
  el.proofEvidence.append(row)
}

/* ─────────────────────────  shell  ───────────────────────── */

function setMode(next: Mode): void {
  mode = next
  el.app.dataset.mode = next
  for (const [tab, view, name] of [
    [el.modeWrite, el.viewWrite, 'write'],
    [el.modeProof, el.viewProof, 'proof'],
    [el.modePublish, el.viewPublish, 'publish'],
  ] as const) {
    const selected = name === next
    tab.classList.toggle('is-selected', selected)
    if (selected) {
      tab.setAttribute('aria-current', 'page')
    } else {
      tab.removeAttribute('aria-current')
    }
    view.hidden = !selected
  }

  if (next === 'publish') {
    void publish.refresh(false)
  }
}

function toggleSource(force?: boolean): void {
  sourceVisible = force ?? !sourceVisible
  el.documentSource.hidden = !sourceVisible
  // Title and subtitle are frontmatter, not body. They stay put while the body view swaps.
  el.milkdownHost.hidden = sourceVisible
  el.sourceToggle.textContent = sourceVisible ? 'Rich text' : 'Markdown'
  el.sourceToggle.classList.toggle('is-selected', sourceVisible)
  if (sourceVisible) {
    el.sourceInput.value = editorMarkdown
    el.sourceInput.focus()
  } else if (editor !== undefined) {
    editor.replaceMarkdown(resolveOwnedMediaForPreview(editorMarkdown))
    editor.focus()
  }
}

async function copyWorkspacePath(): Promise<void> {
  const path = el.workspacePath.textContent ?? ''
  try {
    await navigator.clipboard.writeText(path)
    toast(`Copied ${path}`, 'success')
  } catch {
    toast(`The mounted repository is at ${path}`, 'default')
  }
}

function updateCount(): void {
  const words = editorMarkdown
    .replace(/[`#>*_|[\]()-]/gu, ' ')
    .trim()
    .split(/\s+/u)
    .filter(Boolean).length
  el.documentCount.textContent = `${words} word${words === 1 ? '' : 's'}`
}

function autoGrow(field: HTMLTextAreaElement): void {
  field.style.height = 'auto'
  field.style.height = `${field.scrollHeight}px`
}

/* ─────────────────────────  media paths  ───────────────────────── */

const OWNED_MEDIA = /(!\[(?:\\.|[^\]\r\n])*\]\()media\/([a-f0-9]{64}\.(?:avif|gif|jpe?g|png|webp))(\))/giu

function resolveOwnedMediaForPreview(markdown: string): string {
  if (activeArticleId === undefined) {
    return markdown
  }
  const endpoint = `/api/v1/articles/${encodeURIComponent(activeArticleId)}/media/`
  return markdown.replace(
    OWNED_MEDIA,
    (_match, opening: string, fileName: string, closing: string) =>
      `${opening}${endpoint}${encodeURIComponent(fileName)}${closing}`,
  )
}

/** The document stores the article-relative path; the endpoint URL is a rendering detail only. */
function restoreOwnedMediaPaths(markdown: string): string {
  if (activeArticleId === undefined) {
    return markdown
  }
  const endpoint = `/api/v1/articles/${encodeURIComponent(activeArticleId)}/media/`
  return markdown.replaceAll(endpoint, 'media/')
}

/* ─────────────────────────  feedback  ───────────────────────── */

function setStatusPill(element: HTMLElement, label: string, tone: StatusTone): void {
  element.textContent = label
  element.classList.remove(
    'status-pill--quiet',
    'status-pill--warning',
    'status-pill--success',
    'status-pill--danger',
  )
  if (tone !== 'default') {
    element.classList.add(`status-pill--${tone}`)
  }
}

let toastTimer: number | undefined
function toast(message: string, tone: 'default' | 'success' | 'warning' | 'danger'): void {
  el.toast.textContent = message
  el.toast.className = `toast toast--${tone}`
  el.toast.hidden = false
  announce(message)
  if (toastTimer !== undefined) {
    window.clearTimeout(toastTimer)
  }
  // Failures stay until the next message; they are not something to miss by looking away.
  if (tone !== 'danger') {
    toastTimer = window.setTimeout(() => {
      el.toast.hidden = true
    }, 4000)
  }
}

let announceTimer: number | undefined
function announce(message: string): void {
  el.announcer.textContent = ''
  if (announceTimer !== undefined) {
    window.clearTimeout(announceTimer)
  }
  announceTimer = window.setTimeout(() => {
    el.announcer.textContent = message
  }, 20)
}

function setBusy(busy: boolean): void {
  busyCount = Math.max(0, busyCount + (busy ? 1 : -1))
  el.app.setAttribute('aria-busy', String(busyCount > 0))
}

function formatError(error: unknown): string {
  if (error instanceof RequestError) {
    const problem: unknown = error.problem
    if (typeof problem === 'object' && problem !== null) {
      const detail = (problem as { detail?: unknown }).detail
      const title = (problem as { title?: unknown }).title
      if (typeof detail === 'string') {
        return detail
      }
      if (typeof title === 'string') {
        return title
      }
    }
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function must<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector)
  if (element === null) {
    throw new Error(`Required element was not found: ${selector}`)
  }
  return element
}
