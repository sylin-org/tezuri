import './styles.css'

import { hasLaunchNonce, sessionApi } from './api/session-api'
import {
  SourcePatchConflictError,
  TezuriApiError,
  type GitCommitPlanV1,
  type GitCommitReceiptV1,
  type GitPushReceiptV1,
  type GitRemoteBranchV1,
  type GitRepositorySnapshotV1,
  type MediaAssetReceiptV1,
} from './api/tezuri-api'
import { MarkdownEditor } from './editor/markdown-editor'
import {
  SITE_PROOF_PROTOCOL,
  SITE_PROOF_PROTOCOL_VERSION,
  type SiteProofCommandResultV1,
  type SiteProofRunReceiptV1,
  type SiteProofStatusV1,
} from './proof-protocol'
import {
  planBodySourceEdit,
  prepareArticleBody,
  type BodySourceEditPlan,
  type PreparedArticleBodyV1,
} from './source-edit'
import type {
  ArticleSourceEnvelopeV1,
  ArticleSummaryV1,
  SourcePatchConflictV1,
} from './source-protocol'

type EditorMode = 'rich' | 'source'

interface PendingGitPush {
  readonly remoteBranch: GitRemoteBranchV1
  readonly localSha: string
}

const app = mustQuery<HTMLElement>('#app')
const announcer = mustQuery<HTMLElement>('#announcer')
const workspaceStatus = mustQuery<HTMLElement>('#workspace-status')
const articleFilter = mustQuery<HTMLInputElement>('#article-filter')
const articleListNote = mustQuery<HTMLElement>('#article-list-note')
const articleList = mustQuery<HTMLUListElement>('#article-list')
const articlePath = mustQuery<HTMLElement>('#article-path')
const articleTitle = mustQuery<HTMLElement>('#article-title')
const sourceCapabilityState = mustQuery<HTMLElement>('#source-capability-state')
const dirtyState = mustQuery<HTMLElement>('#dirty-state')
const documentCount = mustQuery<HTMLElement>('#document-count')
const authorityState = mustQuery<HTMLElement>('#authority-state')
const richTab = mustQuery<HTMLButtonElement>('#rich-mode-tab')
const sourceTab = mustQuery<HTMLButtonElement>('#source-mode-tab')
const richPanel = mustQuery<HTMLElement>('#rich-editor-panel')
const sourcePanel = mustQuery<HTMLElement>('#source-editor-panel')
const milkdownHost = mustQuery<HTMLElement>('#milkdown-editor')
const sourceInput = mustQuery<HTMLTextAreaElement>('#markdown-source')
const saveButton = mustQuery<HTMLButtonElement>('#save-button')
const saveExplanation = mustQuery<HTMLElement>('#save-explanation')
const conflictPanel = mustQuery<HTMLElement>('#source-conflict')
const conflictMessage = mustQuery<HTMLElement>('#conflict-message')
const conflictCurrentSource = mustQuery<HTMLElement>('#conflict-current-source')
const metadataState = mustQuery<HTMLElement>('#metadata-state')
const metadataTitle = mustQuery<HTMLInputElement>('#metadata-title')
const metadataPublication = mustQuery<HTMLInputElement>('#metadata-publication')
const metadataTags = mustQuery<HTMLInputElement>('#metadata-tags')
const metadataPath = mustQuery<HTMLInputElement>('#metadata-path')
const publishShortcut = mustQuery<HTMLButtonElement>('#publish-shortcut')
const mediaForm = mustQuery<HTMLFormElement>('#media-form')
const mediaStatus = mustQuery<HTMLElement>('#media-status')
const mediaNote = mustQuery<HTMLElement>('#media-note')
const mediaFile = mustQuery<HTMLInputElement>('#media-file')
const mediaAlt = mustQuery<HTMLInputElement>('#media-alt')
const mediaUploadButton = mustQuery<HTMLButtonElement>('#media-upload-button')
const proofStatus = mustQuery<HTMLElement>('#proof-status')
const proofNote = mustQuery<HTMLElement>('#proof-note')
const proofEvidence = mustQuery<HTMLDListElement>('#proof-evidence')
const proofRunButton = mustQuery<HTMLButtonElement>('#proof-run-button')
const gitHeading = mustQuery<HTMLElement>('#git-heading')
const gitStatus = mustQuery<HTMLElement>('#git-status')
const gitNote = mustQuery<HTMLElement>('#git-note')
const gitRailStatus = mustQuery<HTMLElement>('#git-rail-status')
const gitSummary = mustQuery<HTMLElement>('#git-summary')
const gitPathFieldset = mustQuery<HTMLFieldSetElement>('#git-path-fieldset')
const gitPathLegend = mustQuery<HTMLElement>('#git-path-legend')
const gitChanges = mustQuery<HTMLElement>('#git-changes')
const gitCommitMessage = mustQuery<HTMLInputElement>('#git-commit-message')
const gitRefreshButton = mustQuery<HTMLButtonElement>('#git-refresh-button')
const gitReviewButton = mustQuery<HTMLButtonElement>('#git-review-button')
const gitPlanReview = mustQuery<HTMLElement>('#git-plan-review')
const gitPlanSummary = mustQuery<HTMLElement>('#git-plan-summary')
const gitPlanPaths = mustQuery<HTMLUListElement>('#git-plan-paths')
const gitCommitButton = mustQuery<HTMLButtonElement>('#git-commit-button')
const gitPushPanel = mustQuery<HTMLElement>('#git-push-panel')
const gitPushSummary = mustQuery<HTMLElement>('#git-push-summary')
const gitPushButton = mustQuery<HTMLButtonElement>('#git-push-button')
const gitAlert = mustQuery<HTMLElement>('#git-alert')
const commandButtons = Array.from(
  document.querySelectorAll<HTMLButtonElement>('[data-editor-command]'),
)

let editor: MarkdownEditor | undefined
let editorFailed = false
let currentMode: EditorMode = 'rich'
let articles: readonly ArticleSummaryV1[] = []
let activeArticleId: string | undefined
let openedEnvelope: ArticleSourceEnvelopeV1 | undefined
let preparedBody: PreparedArticleBodyV1 | undefined
let currentPlan: BodySourceEditPlan | undefined
let currentConflict: SourcePatchConflictV1 | undefined
let openController: AbortController | undefined
let openGeneration = 0
let loadingWorkspace = true
let openingArticle = false
let saving = false
let saveFailure: string | undefined
let proofRunning = false
let mediaUploading = false
let gitLoading = false
let gitActing = false
let gitSnapshot: GitRepositorySnapshotV1 | undefined
let gitPlan: GitCommitPlanV1 | undefined
let pendingPush: PendingGitPush | undefined

sourceInput.disabled = true
commandButtons.forEach((button) => {
  button.disabled = true
})

articleFilter.addEventListener('input', () => renderArticleList())
richTab.addEventListener('click', () => void selectMode('rich'))
sourceTab.addEventListener('click', () => void selectMode('source'))
sourceInput.addEventListener('input', () => {
  saveFailure = undefined
  invalidateGitReview()
  updateDocumentCount(sourceInput.value)
  updateEditPlan()
})
saveButton.addEventListener('click', () => void saveSource())
proofRunButton.addEventListener('click', () => void runSiteProof())
mediaForm.addEventListener('submit', (event) => {
  event.preventDefault()
  void uploadAndInsertMedia()
})
mediaFile.addEventListener('change', () => updateMediaControls())
mediaAlt.addEventListener('input', () => updateMediaControls())
publishShortcut.addEventListener('click', () => void openPublicationPanel())
gitRefreshButton.addEventListener('click', () => void loadGitStatus(true))
gitChanges.addEventListener('change', () => {
  invalidateGitReview()
  updateGitControls()
})
gitCommitMessage.addEventListener('input', () => {
  invalidateGitReview()
  updateGitControls()
})
gitReviewButton.addEventListener('click', () => void reviewGitCommit())
gitCommitButton.addEventListener('click', () => void createReviewedCommit())
gitPushButton.addEventListener('click', () => void pushReviewedCommit())

proofRunButton.disabled = !hasLaunchNonce
if (!hasLaunchNonce) {
  proofNote.textContent = 'Relaunch Tezuri with its nonce-bearing URL to run the declared repository proof.'
  mediaNote.textContent = 'Relaunch Tezuri with its nonce-bearing URL to upload article-owned media.'
  gitNote.textContent = 'Git changes remain visible, but commit and push need a nonce-bearing Tezuri launch URL.'
}

for (const tab of [richTab, sourceTab]) {
  tab.addEventListener('keydown', (event) => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      return
    }

    event.preventDefault()
    const prefersRich = event.key === 'ArrowLeft' || event.key === 'Home'
    const nextMode: EditorMode = prefersRich && !richTab.disabled ? 'rich' : 'source'
    const nextTab = nextMode === 'rich' ? richTab : sourceTab
    nextTab.focus()
    void selectMode(nextMode, false)
  })
}

window.addEventListener('beforeunload', (event) => {
  if (hasUnsavedSource()) {
    event.preventDefault()
  }
})

window.addEventListener('pagehide', () => {
  openController?.abort()
  if (editor !== undefined) {
    void editor.destroy()
  }
})

publishShortcut.disabled = false
updateMediaControls()
updateGitControls()
void loadWorkspace()
void loadGitStatus(false)

async function loadWorkspace(): Promise<void> {
  loadingWorkspace = true
  syncBusyState()
  setStatusPill(workspaceStatus, 'Connecting', 'quiet')
  articleListNote.textContent = 'Loading articles from the mounted workspace.'

  try {
    const response = await sessionApi.listArticles()
    if (
      response.protocol !== 'tezuri.article-list' ||
      response.version !== 1 ||
      !Array.isArray(response.articles)
    ) {
      throw new Error('The workspace returned an unsupported article-list response.')
    }

    articles = response.articles
    articleFilter.disabled = false
    setStatusPill(
      workspaceStatus,
      hasLaunchNonce ? 'Workspace connected' : 'Read-only session',
      hasLaunchNonce ? 'default' : 'warning',
    )
    renderArticleList()

    const firstArticle = articles[0]
    if (firstArticle === undefined) {
      showEmptyWorkspace()
      return
    }

    await openArticle(firstArticle.id, false)
  } catch (error) {
    showWorkspaceError(error)
  } finally {
    loadingWorkspace = false
    syncBusyState()
  }
}

async function openArticle(articleId: string, confirmDiscard = true): Promise<void> {
  if (mediaUploading) {
    announce('Wait for the current media upload to finish before opening another article.')
    return
  }

  if (saving) {
    announce('Wait for the current source save to finish before opening another article.')
    return
  }

  if (articleId === activeArticleId && currentConflict === undefined) {
    return
  }

  if (
    confirmDiscard &&
    hasUnsavedSource() &&
    !window.confirm('Discard the unsaved source draft and open another article?')
  ) {
    return
  }

  openController?.abort()
  const controller = new AbortController()
  openController = controller
  const generation = ++openGeneration
  openingArticle = true
  saveFailure = undefined
  clearConflict()
  sourceInput.disabled = true
  dirtyState.textContent = 'Opening canonical source…'
  dirtyState.classList.remove('has-change')
  saveButton.disabled = true
  renderArticleList(articleId)
  syncBusyState()

  try {
    const envelope = await sessionApi.getArticleSource(articleId, controller.signal)
    const prepared = prepareArticleBody(envelope)
    if (generation !== openGeneration) {
      return
    }

    adoptOpenedEnvelope(envelope, prepared)
    if (envelope.capabilities.richEditing === 'source-only') {
      richTab.disabled = true
      await selectMode('source', false)
    } else {
      await syncRichPreview(prepared.editorBody)
      if (editorFailed) {
        richTab.disabled = true
        await selectMode('source', false)
      } else {
        richTab.disabled = false
      }
    }

    app.dataset.editorState = 'ready'
    announce(`${envelope.article.displayTitle} opened from exact UTF-8 source.`)
  } catch (error) {
    if (!isAbortError(error) && generation === openGeneration) {
      showArticleError(error)
    }
  } finally {
    if (generation === openGeneration) {
      openingArticle = false
      syncBusyState()
      updateEditPlan()
    }
  }
}

function adoptOpenedEnvelope(
  envelope: ArticleSourceEnvelopeV1,
  prepared: PreparedArticleBodyV1,
): void {
  openedEnvelope = envelope
  preparedBody = prepared
  activeArticleId = envelope.article.id
  currentConflict = undefined
  currentPlan = { kind: 'unchanged' }
  saveFailure = undefined
  conflictPanel.hidden = true
  resetMediaForm()

  sourceInput.value = prepared.editorBody
  sourceInput.disabled = false
  updateDocumentCount(sourceInput.value)

  articlePath.textContent = envelope.article.relativePath
  articleTitle.textContent = envelope.article.displayTitle
  metadataTitle.value = envelope.article.displayTitle
  metadataPublication.value = publicationLabel(
    articles.find((article) => article.id === envelope.article.id)?.publicationState ?? 'unknown',
  )
  metadataTags.value = 'Not projected by the V1 source contract'
  metadataPath.value = envelope.article.relativePath
  metadataState.textContent = 'API source'

  const protectedCount = envelope.capabilities.protectedSegmentCount
  setStatusPill(
    sourceCapabilityState,
    protectedCount === 0 ? 'Body source editable' : `${protectedCount} protected raw segment${protectedCount === 1 ? '' : 's'}`,
    protectedCount === 0 ? 'default' : 'warning',
  )
  authorityState.textContent = `${envelope.base.byteLength} canonical UTF-8 bytes · ${shortHash(envelope.base.sha256)}`
  renderArticleList()
  updateEditPlan()
}

async function syncRichPreview(markdown: string): Promise<void> {
  const previewMarkdown = resolveOwnedMediaForPreview(markdown)
  if (editor !== undefined) {
    editor.replaceMarkdown(previewMarkdown)
    return
  }
  if (editorFailed) {
    return
  }

  try {
    editor = await MarkdownEditor.create({
      host: milkdownHost,
      markdown: previewMarkdown,
      accessibleName: 'Read-only rich preview of the article body',
      readonly: true,
    })
  } catch (error) {
    console.error('Milkdown could not start.', error)
    editorFailed = true
    app.dataset.editorState = 'source-only'
    announce('The rich preview could not start. Markdown body source remains available.')
  }
}

async function selectMode(mode: EditorMode, moveFocus = true): Promise<void> {
  if (mode === 'rich' && (richTab.disabled || editor === undefined)) {
    announce('Rich preview is unavailable for this article. Markdown body source remains available.')
    return
  }

  currentMode = mode
  if (mode === 'source') {
    setSelectedTab(sourceTab, richTab)
    richPanel.hidden = true
    sourcePanel.hidden = false
    if (moveFocus) {
      sourceInput.focus()
    }
    announce('Markdown body source selected. Supported edits can be saved as one byte-range patch.')
  } else {
    editor?.replaceMarkdown(resolveOwnedMediaForPreview(sourceInput.value))
    setSelectedTab(richTab, sourceTab)
    sourcePanel.hidden = true
    richPanel.hidden = false
    if (moveFocus) {
      editor?.focus()
    }
    announce('Read-only rich preview selected. It never writes canonical source.')
  }

  updateEditPlan()
}

async function saveSource(): Promise<void> {
  if (
    openedEnvelope === undefined ||
    preparedBody === undefined ||
    currentPlan?.kind !== 'ready' ||
    currentConflict !== undefined ||
    currentMode !== 'source' ||
    !hasLaunchNonce ||
    saving ||
    mediaUploading
  ) {
    return
  }

  const envelopeAtSave = openedEnvelope
  const patchSet = currentPlan.patchSet
  invalidateGitReview()
  saving = true
  sourceInput.readOnly = true
  saveFailure = undefined
  updateEditPlan()
  syncBusyState()

  try {
    const applied = await sessionApi.applySourcePatches(
      envelopeAtSave.article.id,
      patchSet,
    )
    const refreshedBody = prepareArticleBody(applied.current)
    articles = articles.map((article) =>
      article.id === applied.current.article.id
        ? {
            ...article,
            displayTitle: applied.current.article.displayTitle,
            relativePath: applied.current.article.relativePath,
            sourceSha256: applied.current.base.sha256,
            updatedAt: applied.savedAt,
          }
        : article,
    )
    adoptOpenedEnvelope(applied.current, refreshedBody)
    if (applied.current.capabilities.richEditing === 'available') {
      await syncRichPreview(refreshedBody.editorBody)
      richTab.disabled = editorFailed
    } else {
      richTab.disabled = true
    }
    dirtyState.textContent = `Saved ${formatSavedTime(applied.savedAt)}`
    dirtyState.classList.remove('has-change')
    announce('Source saved. The returned canonical envelope is now the opened base.')
    void loadGitStatus(false)
  } catch (error) {
    if (error instanceof SourcePatchConflictError) {
      showConflict(error.conflict)
    } else {
      saveFailure = formatError(error)
      announce(`Save failed. ${saveFailure}`)
    }
  } finally {
    saving = false
    sourceInput.readOnly = false
    syncBusyState()
    updateEditPlan()
  }
}

async function uploadAndInsertMedia(): Promise<void> {
  const file = mediaFile.files?.[0]
  const alt = mediaAlt.value.trim()
  const articleId = activeArticleId
  if (
    file === undefined ||
    alt === '' ||
    articleId === undefined ||
    openedEnvelope === undefined ||
    preparedBody === undefined ||
    !hasLaunchNonce ||
    mediaUploading
  ) {
    return
  }

  const insertStart = currentMode === 'source' ? sourceInput.selectionStart : sourceInput.value.length
  const insertEnd = currentMode === 'source' ? sourceInput.selectionEnd : sourceInput.value.length
  const envelopeAtUpload = openedEnvelope
  const preparedAtUpload = preparedBody
  const sourceAtUpload = sourceInput.value

  const previewInsertion = surroundBlock(
    sourceAtUpload,
    insertStart,
    insertEnd,
    `![${escapeMarkdownAlt(alt)}](media/tezuri-upload-preview.png)`,
  )
  const previewPlan = planBodySourceEdit(envelopeAtUpload, preparedAtUpload, previewInsertion.value)
  if (previewPlan.kind !== 'ready') {
    const message = previewPlan.kind === 'unsupported'
      ? previewPlan.reason
      : 'The proposed Markdown insertion did not create a source change.'
    setStatusPill(mediaStatus, 'Cannot insert', 'warning')
    mediaNote.textContent = message
    announce(`Media was not uploaded. ${message}`)
    return
  }

  mediaUploading = true
  sourceInput.readOnly = true
  setStatusPill(mediaStatus, 'Uploading', 'quiet')
  mediaNote.textContent = `Storing ${file.name} inside this article.`
  updateEditPlan()
  syncBusyState()

  let mediaWasStored = false
  try {
    const receipt = await sessionApi.uploadArticleMedia(articleId, file)
    if (!isMediaReceipt(receipt) || receipt.articleId !== articleId) {
      throw new Error('The workspace returned an unsupported media receipt.')
    }
    mediaWasStored = true
    if (
      activeArticleId !== articleId ||
      openedEnvelope !== envelopeAtUpload ||
      preparedBody !== preparedAtUpload ||
      sourceInput.value !== sourceAtUpload
    ) {
      throw new Error('The image was stored, but the article draft changed before its Markdown could be inserted. Reopen the article before referencing the uploaded asset.')
    }

    const markdownPath = articleRelativeMediaPath(envelopeAtUpload.article.relativePath, receipt)
    const imageMarkdown = `![${escapeMarkdownAlt(alt)}](${markdownPath})`
    const insertion = surroundBlock(sourceInput.value, insertStart, insertEnd, imageMarkdown)
    sourceInput.value = insertion.value
    invalidateGitReview()
    await selectMode('source', false)
    sourceInput.focus()
    sourceInput.setSelectionRange(insertion.cursor, insertion.cursor)
    updateDocumentCount(sourceInput.value)
    updateEditPlan()

    mediaFile.value = ''
    mediaAlt.value = ''
    setStatusPill(mediaStatus, receipt.deduplicated ? 'Already stored' : 'Uploaded', 'default')
    mediaNote.textContent = `${receipt.fileName} is stored and its Markdown is inserted. The source draft is still unsaved; choose Save source to reference it.`
    announce('Image uploaded and inserted into the Markdown draft. The source draft still needs to be saved.')
    void loadGitStatus(false)
  } catch (error) {
    const message = formatError(error)
    setStatusPill(mediaStatus, mediaWasStored ? 'Stored, not inserted' : 'Upload failed', 'warning')
    mediaNote.textContent = mediaWasStored
      ? `The image is stored but is not referenced by the source draft. ${message}`
      : message
    announce(mediaWasStored
      ? `The image was stored but not inserted. ${message}`
      : `Media upload failed. ${message}`)
  } finally {
    mediaUploading = false
    sourceInput.readOnly = false
    updateEditPlan()
    syncBusyState()
  }
}

async function openPublicationPanel(): Promise<void> {
  gitHeading.scrollIntoView({
    behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
    block: 'start',
  })
  gitHeading.focus({ preventScroll: true })
  if (gitSnapshot === undefined && !gitLoading) {
    await loadGitStatus(false)
  }
}

async function loadGitStatus(announceResult: boolean): Promise<void> {
  if (gitLoading || gitActing) {
    return
  }

  gitLoading = true
  clearGitAlert()
  invalidateGitReview()
  setStatusPill(gitStatus, 'Checking', 'quiet')
  gitSummary.textContent = 'Reading the mounted repository…'
  updateGitControls()
  syncBusyState()

  try {
    const snapshot = await sessionApi.inspectGit()
    if (!isGitSnapshot(snapshot)) {
      throw new Error('The workspace returned an unsupported Git status response.')
    }

    gitSnapshot = snapshot
    renderGitSnapshot(snapshot)
    if (announceResult) {
      announce(describeGitSnapshot(snapshot))
    }
  } catch (error) {
    gitSnapshot = undefined
    const message = formatError(error)
    setStatusPill(gitStatus, 'Unavailable', 'warning')
    gitRailStatus.textContent = 'Git status unavailable'
    gitSummary.textContent = 'The mounted repository could not be inspected.'
    renderGitChanges(undefined)
    showGitAlert(message)
    if (announceResult) {
      announce(`Git status could not be refreshed. ${message}`)
    }
  } finally {
    gitLoading = false
    updateGitControls()
    syncBusyState()
  }
}

async function reviewGitCommit(): Promise<void> {
  if (!canReviewGit()) {
    return
  }

  const selectedPaths = selectedGitPaths()
  gitActing = true
  clearGitAlert()
  invalidateGitReview()
  setStatusPill(gitStatus, 'Reviewing', 'quiet')
  updateGitControls()
  syncBusyState()

  try {
    const freshSnapshot = await sessionApi.inspectGit()
    if (!isGitSnapshot(freshSnapshot)) {
      throw new Error('The workspace returned an unsupported Git status response.')
    }

    gitSnapshot = freshSnapshot
    renderGitSnapshot(freshSnapshot, new Set(selectedPaths))
    const stillSelectable = selectedGitPaths()
    if (
      stillSelectable.length !== selectedPaths.length ||
      selectedPaths.some((path) => !stillSelectable.includes(path))
    ) {
      throw new Error('The selected repository paths changed while Git status was refreshed. Review them again.')
    }

    const plan = await sessionApi.planGitCommit({ selectedPaths })
    if (!isGitCommitPlan(plan)) {
      throw new Error('The workspace returned an unsupported Git commit plan.')
    }

    gitPlan = plan
    gitPlanSummary.textContent = `${plan.branch} at ${shortHash(plan.headSha)} · ${plan.selectedPaths.length} exact path${plan.selectedPaths.length === 1 ? '' : 's'}. This is a path review, not a content diff.`
    gitPlanPaths.replaceChildren(...plan.selectedPaths.map(renderPathItem))
    gitPlanReview.hidden = false
    setStatusPill(gitStatus, 'Reviewed', 'default')
    gitCommitButton.focus()
    announce('Commit path review is ready. Creating the commit remains a separate action.')
  } catch (error) {
    const message = formatError(error)
    setStatusPill(gitStatus, 'Review failed', 'warning')
    showGitAlert(message)
    announce(`Commit review failed. ${message}`)
  } finally {
    gitActing = false
    updateGitControls()
    syncBusyState()
  }
}

async function createReviewedCommit(): Promise<void> {
  const plan = gitPlan
  const message = gitCommitMessage.value.trim()
  if (
    plan === undefined ||
    message === '' ||
    !hasLaunchNonce ||
    gitActing ||
    saving ||
    mediaUploading ||
    hasUnsavedSource()
  ) {
    return
  }

  gitActing = true
  clearGitAlert()
  gitCommitButton.disabled = true
  gitCommitButton.textContent = 'Creating commit…'
  setStatusPill(gitStatus, 'Committing', 'quiet')
  updateGitControls()
  syncBusyState()

  try {
    const receipt = await sessionApi.prepareGitCommit({
      expectedHeadSha: plan.headSha,
      expectedPlanSha256: plan.planSha256,
      message,
      selectedPaths: plan.selectedPaths,
    })
    if (!isGitCommitReceipt(receipt)) {
      throw new Error('The workspace returned an unsupported Git commit receipt.')
    }

    const remoteBranch = resolveUpstreamRemoteBranch(gitSnapshot)
    pendingPush = remoteBranch === undefined
      ? undefined
      : { remoteBranch, localSha: receipt.afterSha }
    gitPlan = undefined
    gitPlanReview.hidden = true
    gitPushPanel.hidden = false
    renderCommittedGitPaths(receipt.selectedPaths)
    gitPushButton.hidden = pendingPush === undefined
    gitPushButton.disabled = pendingPush === undefined
    gitPushButton.textContent = pendingPush === undefined
      ? 'Push unavailable'
      : `Push to ${pendingPush.remoteBranch.remote}/${pendingPush.remoteBranch.branch}`
    gitPushSummary.textContent = pendingPush === undefined
      ? `Commit ${shortHash(receipt.afterSha)} is local. No exact reviewed upstream tip is available, so Tezuri will not offer a push.`
      : `Commit ${shortHash(receipt.afterSha)} is local. Push will recheck ${pendingPush.remoteBranch.remote}/${pendingPush.remoteBranch.branch} at ${shortHash(pendingPush.remoteBranch.sha)} and never force.`
    setStatusPill(gitStatus, receipt.created ? 'Committed' : 'Already committed', 'default')
    gitRailStatus.textContent = `${receipt.branch} · local ${shortHash(receipt.afterSha)}`
    gitSummary.textContent = `${receipt.selectedPaths.length} reviewed path${receipt.selectedPaths.length === 1 ? '' : 's'} committed as ${shortHash(receipt.afterSha)}.`
    if (pendingPush !== undefined) {
      gitPushButton.focus()
    } else {
      gitRefreshButton.focus()
    }
    announce(pendingPush === undefined
      ? 'Reviewed commit created locally. No safe push target is available.'
      : 'Reviewed commit created locally. Push remains optional.')
  } catch (error) {
    const message = formatError(error)
    setStatusPill(gitStatus, 'Commit failed', 'warning')
    showGitAlert(message)
    announce(`Commit creation failed. ${message}`)
  } finally {
    gitActing = false
    gitCommitButton.textContent = 'Create reviewed commit'
    updateGitControls()
    syncBusyState()
  }
}

async function pushReviewedCommit(): Promise<void> {
  const push = pendingPush
  if (push === undefined || !hasLaunchNonce || gitActing) {
    return
  }

  gitActing = true
  clearGitAlert()
  gitPushButton.disabled = true
  gitPushButton.textContent = 'Pushing…'
  setStatusPill(gitStatus, 'Pushing', 'quiet')
  updateGitControls()
  syncBusyState()

  try {
    const receipt = await sessionApi.pushGit({
      remote: push.remoteBranch.remote,
      branch: push.remoteBranch.branch,
      expectedHeadSha: push.localSha,
      expectedRemoteSha: push.remoteBranch.sha,
    })
    if (!isGitPushReceipt(receipt)) {
      throw new Error('The workspace returned an unsupported Git push receipt.')
    }

    pendingPush = undefined
    gitPushButton.hidden = true
    gitPushSummary.textContent = `${shortHash(receipt.remoteAfterSha)} is now on ${receipt.remote}/${receipt.branch}. The push was non-force and divergence-checked.`
    gitSummary.textContent = `${receipt.remote}/${receipt.branch} now matches local ${shortHash(receipt.localSha)}.`
    gitRailStatus.textContent = `${receipt.branch} · pushed ${shortHash(receipt.remoteAfterSha)}`
    setStatusPill(gitStatus, receipt.pushed ? 'Pushed' : 'Already current', 'default')
    announce(receipt.pushed ? 'Commit pushed successfully.' : 'The reviewed remote was already current.')
  } catch (error) {
    const message = formatError(error)
    setStatusPill(gitStatus, 'Push failed', 'warning')
    showGitAlert(message)
    announce(`Push failed. The local commit remains intact. ${message}`)
  } finally {
    gitActing = false
    if (pendingPush !== undefined) {
      gitPushButton.textContent = `Push to ${pendingPush.remoteBranch.remote}/${pendingPush.remoteBranch.branch}`
    }
    updateGitControls()
    syncBusyState()
  }
}

async function runSiteProof(): Promise<void> {
  if (!hasLaunchNonce || proofRunning) {
    return
  }

  proofRunning = true
  proofRunButton.disabled = true
  proofRunButton.textContent = 'Running proof…'
  setStatusPill(proofStatus, 'Running', 'quiet')
  proofNote.textContent = 'Running the mounted repository\'s declared commands in an isolated copy.'
  replaceProofEvidence('Repository proof', 'Running declared commands…', 'pending')
  syncBusyState()

  try {
    const receipt = await sessionApi.runSiteProof()
    if (
      receipt.protocol !== SITE_PROOF_PROTOCOL ||
      receipt.version !== SITE_PROOF_PROTOCOL_VERSION ||
      !Array.isArray(receipt.result.commands)
    ) {
      throw new Error('The workspace returned an unsupported site-proof response.')
    }

    renderProofReceipt(receipt)
  } catch (error) {
    const message = formatError(error)
    setStatusPill(proofStatus, 'Failed', 'warning')
    proofNote.textContent = `Proof could not complete. ${message}`
    replaceProofEvidence('Repository proof', 'No valid proof receipt was returned.', 'failed')
    announce(`Site proof failed. ${message}`)
  } finally {
    proofRunning = false
    proofRunButton.disabled = !hasLaunchNonce
    proofRunButton.textContent = 'Run site proof'
    syncBusyState()
  }
}

function renderProofReceipt(receipt: SiteProofRunReceiptV1): void {
  const passed = receipt.status === 'passed' && receipt.result.succeeded
  setStatusPill(proofStatus, passed ? 'Passed' : 'Failed', passed ? 'default' : 'warning')
  proofNote.textContent = passed
    ? `${receipt.progress.completedCommands} of ${receipt.progress.totalCommands} declared commands passed in an isolated copy.`
    : `${receipt.progress.completedCommands} of ${receipt.progress.totalCommands} declared commands completed. The repository proof did not pass.`

  proofEvidence.replaceChildren()
  if (receipt.result.commands.length === 0) {
    appendProofEvidence('Repository proof', 'No command evidence was returned.', 'failed')
  } else {
    for (const command of receipt.result.commands) {
      appendProofEvidence(
        command.id,
        describeProofCommand(command),
        command.status === 'passed' ? 'passed' : 'failed',
      )
    }
  }

  announce(passed ? 'Site proof passed.' : 'Site proof failed. Command evidence is available in the Proof panel.')
}

function describeProofCommand(command: SiteProofCommandResultV1): string {
  const commandLine = [command.executable, ...command.arguments].join(' ')
  const exit = command.exitCode === null ? '' : ` · exit ${command.exitCode}`
  const output = command.outputDirectory === null
    ? 'no output directory declared'
    : `${command.outputDirectory} ${command.outputDirectoryExists ? 'found' : 'missing'}`
  const diagnostic = firstProofDiagnostic(command)

  return `${commandLine} · ${proofStatusLabel(command.status)}${exit} · ${command.durationMilliseconds} ms · ${output}${diagnostic === undefined ? '' : ` · ${diagnostic}`}`
}

function firstProofDiagnostic(command: SiteProofCommandResultV1): string | undefined {
  const output = command.standardError.trim() || command.standardOutput.trim()
  const firstLine = output.split(/\r?\n/u).find((line) => line.trim() !== '')?.trim()
  return firstLine === undefined ? undefined : firstLine.slice(0, 180)
}

function proofStatusLabel(status: SiteProofStatusV1): string {
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

function replaceProofEvidence(
  title: string,
  detail: string,
  tone: 'pending' | 'passed' | 'failed',
): void {
  proofEvidence.replaceChildren()
  appendProofEvidence(title, detail, tone)
}

function appendProofEvidence(
  title: string,
  detail: string,
  tone: 'pending' | 'passed' | 'failed',
): void {
  const row = document.createElement('div')
  const term = document.createElement('dt')
  const description = document.createElement('dd')
  const dot = document.createElement('span')
  term.textContent = title
  dot.className = `evidence-dot evidence-dot--${tone}`
  dot.setAttribute('aria-hidden', 'true')
  description.append(dot, detail)
  row.append(term, description)
  proofEvidence.append(row)
}

function showConflict(conflict: SourcePatchConflictV1): void {
  currentConflict = conflict
  conflictPanel.hidden = false
  conflictMessage.textContent = `${conflict.message} Your unsaved body remains in the source editor; the repository version is shown below.`

  try {
    const currentBody = prepareArticleBody(conflict.current)
    conflictCurrentSource.textContent = currentBody.editorBody
  } catch {
    conflictCurrentSource.textContent = 'The repository version could not be decoded for comparison.'
  }

  announce('Save conflict. Your source draft and the current repository body are both preserved on screen.')
}

function clearConflict(): void {
  currentConflict = undefined
  conflictPanel.hidden = true
  conflictMessage.textContent = ''
  conflictCurrentSource.textContent = ''
}

function updateEditPlan(): void {
  if (openedEnvelope === undefined || preparedBody === undefined) {
    currentPlan = undefined
    saveButton.disabled = true
    updateMediaControls()
    updateGitControls()
    return
  }

  try {
    currentPlan = planBodySourceEdit(openedEnvelope, preparedBody, sourceInput.value)
  } catch (error) {
    currentPlan = {
      kind: 'unsupported',
      reason: formatError(error),
    }
  }

  dirtyState.classList.remove('has-change')
  if (currentConflict !== undefined) {
    dirtyState.textContent = 'Conflict · draft preserved'
    dirtyState.classList.add('has-change')
    saveExplanation.textContent = 'The repository changed after this article was opened. Save is paused; compare the preserved versions before reopening.'
    saveButton.disabled = true
    updateMediaControls()
    updateGitControls()
    return
  }

  if (saveFailure !== undefined) {
    dirtyState.textContent = 'Save failed · draft preserved'
    dirtyState.classList.add('has-change')
    saveExplanation.textContent = `${saveFailure} The source draft remains available for retry.`
  } else if (currentPlan.kind === 'unchanged') {
    dirtyState.textContent = 'Canonical source unchanged'
    saveExplanation.textContent = 'No patch is emitted for an unchanged body. Rich preview remains read-only.'
  } else if (currentPlan.kind === 'unsupported') {
    dirtyState.textContent = 'Source edit not saveable'
    dirtyState.classList.add('has-change')
    saveExplanation.textContent = currentPlan.reason
  } else {
    dirtyState.textContent = 'Unsaved source change'
    dirtyState.classList.add('has-change')
    const { start, endExclusive } = currentPlan.operation.range
    saveExplanation.textContent = !hasLaunchNonce
      ? 'This browser session has no launch nonce. Relaunch Tezuri to obtain mutation authority; the draft remains local.'
      : currentMode === 'source'
        ? `Ready to replace canonical byte range ${start}–${endExclusive}; frontmatter and surrounding bytes stay untouched.`
        : 'Return to Markdown source to save this supported body edit. Rich preview cannot save.'
  }

  saveButton.disabled =
    currentPlan.kind !== 'ready' ||
    currentMode !== 'source' ||
    !hasLaunchNonce ||
    saving ||
    openingArticle ||
    mediaUploading
  saveButton.textContent = saving ? 'Saving…' : 'Save source'
  updateMediaControls()
  updateGitControls()
}

function updateMediaControls(): void {
  const unavailable =
    activeArticleId === undefined ||
    openedEnvelope === undefined ||
    !hasLaunchNonce ||
    mediaUploading ||
    openingArticle ||
    saving ||
    currentConflict !== undefined ||
    sourceInput.disabled
  mediaFile.disabled = unavailable
  mediaAlt.disabled = unavailable
  mediaUploadButton.disabled =
    unavailable || mediaFile.files?.[0] === undefined || mediaAlt.value.trim() === ''
  mediaUploadButton.textContent = mediaUploading ? 'Uploading…' : 'Upload and insert'
}

function resetMediaForm(): void {
  mediaForm.reset()
  setStatusPill(mediaStatus, 'No file', 'quiet')
  mediaNote.textContent = hasLaunchNonce
    ? 'Upload an image into this article, then insert its Markdown at the source cursor.'
    : 'Relaunch Tezuri with its nonce-bearing URL to upload article-owned media.'
  updateMediaControls()
}

function updateGitControls(): void {
  const snapshot = gitSnapshot
  const supported =
    snapshot !== undefined &&
    !snapshot.isUnborn &&
    !snapshot.isDetached &&
    snapshot.headSha !== null &&
    snapshot.branch !== null
  const commitOutcomeVisible = !gitPushPanel.hidden
  const editable = supported && hasLaunchNonce && !gitLoading && !gitActing && !commitOutcomeVisible

  gitRefreshButton.disabled = gitLoading || gitActing
  gitPathFieldset.disabled = !editable
  gitCommitMessage.disabled = !editable
  gitReviewButton.disabled = !canReviewGit()
  gitCommitButton.disabled =
    gitPlan === undefined ||
    gitActing ||
    !hasLaunchNonce ||
    saving ||
    mediaUploading ||
    hasUnsavedSource()
  gitPushButton.disabled = pendingPush === undefined || gitActing || !hasLaunchNonce

  if (!hasLaunchNonce) {
    gitNote.textContent = 'Git changes are visible, but commit and push require a nonce-bearing Tezuri launch URL.'
  } else if (hasUnsavedSource()) {
    gitNote.textContent = 'Save or discard the browser source draft before reviewing repository paths for a commit.'
  } else if (commitOutcomeVisible) {
    gitNote.textContent = pendingPush === undefined
      ? 'The local commit is complete. Refresh changes before preparing another commit.'
      : 'The local commit is complete. Push is optional and will never force the remote.'
  } else {
    gitNote.textContent = 'Review exact repository paths, create a local commit, then choose whether to push it. Site Proof remains a separate check.'
  }
}

function canReviewGit(): boolean {
  const snapshot = gitSnapshot
  return (
    snapshot !== undefined &&
    !snapshot.isUnborn &&
    !snapshot.isDetached &&
    snapshot.headSha !== null &&
    snapshot.branch !== null &&
    hasLaunchNonce &&
    !gitLoading &&
    !gitActing &&
    gitPushPanel.hidden &&
    !hasUnsavedSource() &&
    gitCommitMessage.value.trim() !== '' &&
    selectedGitPaths().length > 0
  )
}

function renderGitSnapshot(
  snapshot: GitRepositorySnapshotV1,
  selectedPaths: ReadonlySet<string> = new Set(),
): void {
  gitPathLegend.textContent = 'Changed paths'
  const allowedCount = snapshot.changes.filter((change) => change.allowed).length
  const branch = snapshot.branch ?? 'No branch'
  const upstream = snapshot.upstream === null ? 'no upstream' : `tracks ${snapshot.upstream}`

  if (snapshot.isUnborn) {
    setStatusPill(gitStatus, 'Unborn branch', 'warning')
    gitSummary.textContent = 'Create the repository’s first commit outside Tezuri before using this publication POC.'
  } else if (snapshot.isDetached) {
    setStatusPill(gitStatus, 'Detached HEAD', 'warning')
    gitSummary.textContent = 'Check out a normal publication branch before creating a reviewed commit.'
  } else if (snapshot.changes.length === 0) {
    setStatusPill(gitStatus, 'Clean', 'default')
    gitSummary.textContent = `${branch} at ${snapshot.headSha === null ? 'no commit' : shortHash(snapshot.headSha)} · ${upstream} · no changed paths.`
  } else {
    setStatusPill(gitStatus, `${snapshot.changes.length} changed`, 'default')
    gitSummary.textContent = `${branch} at ${snapshot.headSha === null ? 'no commit' : shortHash(snapshot.headSha)} · ${upstream} · ${allowedCount} of ${snapshot.changes.length} changed paths are publishable.`
  }

  gitRailStatus.textContent = snapshot.changes.length === 0
    ? `${branch} · clean`
    : `${branch} · ${snapshot.changes.length} changed`
  renderGitChanges(snapshot, selectedPaths)
}

function renderCommittedGitPaths(paths: readonly string[]): void {
  gitPathLegend.textContent = 'Reviewed paths'
  gitChanges.replaceChildren()
  for (const reviewedPath of paths) {
    const row = document.createElement('div')
    row.className = 'publication-change'
    const check = document.createElement('span')
    check.textContent = '✓'
    check.setAttribute('aria-hidden', 'true')
    const path = document.createElement('span')
    path.className = 'publication-change-path'
    path.textContent = reviewedPath
    const state = document.createElement('span')
    state.className = 'publication-change-state'
    state.textContent = 'committed'
    row.append(check, path, state)
    gitChanges.append(row)
  }
}

function renderGitChanges(
  snapshot: GitRepositorySnapshotV1 | undefined,
  selectedPaths: ReadonlySet<string> = new Set(),
): void {
  gitChanges.replaceChildren()
  if (snapshot === undefined || snapshot.changes.length === 0) {
    const empty = document.createElement('p')
    empty.textContent = snapshot === undefined ? 'No Git status loaded.' : 'The repository has no changed paths.'
    gitChanges.append(empty)
    return
  }

  for (const change of snapshot.changes) {
    const label = document.createElement('label')
    label.className = `publication-change${change.allowed ? '' : ' publication-change--blocked'}`
    const checkbox = document.createElement('input')
    checkbox.type = 'checkbox'
    checkbox.checked = change.allowed && selectedPaths.has(change.path)
    checkbox.disabled = !change.allowed
    checkbox.dataset.gitPath = change.path
    const path = document.createElement('span')
    path.className = 'publication-change-path'
    path.textContent = change.path
    const state = document.createElement('span')
    state.className = 'publication-change-state'
    state.textContent = describeGitChange(change.indexStatus, change.workTreeStatus, change.allowed)
    label.append(checkbox, path, state)
    gitChanges.append(label)
  }
}

function describeGitChange(index: string, workTree: string, allowed: boolean): string {
  const states: string[] = []
  if (index === 'untracked' && workTree === 'untracked') {
    states.push('untracked')
  } else {
    if (index !== 'none') {
      states.push(`index ${index}`)
    }
    if (workTree !== 'none') {
      states.push(`worktree ${workTree}`)
    }
  }
  if (!allowed) {
    states.push('not allowed')
  }
  return states.join(' · ') || 'changed'
}

function selectedGitPaths(): string[] {
  return Array.from(
    gitChanges.querySelectorAll<HTMLInputElement>('input[data-git-path]:checked'),
  ).filter((input) => !input.disabled)
    .map((input) => input.dataset.gitPath)
    .filter((path): path is string => path !== undefined)
}

function invalidateGitReview(): void {
  gitPlan = undefined
  pendingPush = undefined
  gitPlanReview.hidden = true
  gitPushPanel.hidden = true
  gitPushButton.hidden = false
  clearGitAlert()
}

function showGitAlert(message: string): void {
  gitAlert.textContent = message
  gitAlert.hidden = false
}

function clearGitAlert(): void {
  gitAlert.textContent = ''
  gitAlert.hidden = true
}

function resolveUpstreamRemoteBranch(
  snapshot: GitRepositorySnapshotV1 | undefined,
): GitRemoteBranchV1 | undefined {
  if (snapshot?.upstream === null || snapshot?.upstream === undefined) {
    return undefined
  }
  return snapshot.remoteBranches.find(
    (candidate) => `${candidate.remote}/${candidate.branch}` === snapshot.upstream,
  )
}

function describeGitSnapshot(snapshot: GitRepositorySnapshotV1): string {
  if (snapshot.isUnborn) {
    return 'Git status refreshed. The current branch has no commit yet.'
  }
  if (snapshot.isDetached) {
    return 'Git status refreshed. The repository has a detached HEAD.'
  }
  return snapshot.changes.length === 0
    ? `Git status refreshed. ${snapshot.branch ?? 'The repository'} is clean.`
    : `Git status refreshed with ${snapshot.changes.length} changed path${snapshot.changes.length === 1 ? '' : 's'}.`
}

function renderPathItem(path: string): HTMLLIElement {
  const item = document.createElement('li')
  item.textContent = path
  return item
}

function articleRelativeMediaPath(
  articleSourcePath: string,
  receipt: MediaAssetReceiptV1,
): string {
  const sourcePath = articleSourcePath.replaceAll('\\', '/')
  const mediaPath = receipt.relativePath.replaceAll('\\', '/')
  const separator = sourcePath.lastIndexOf('/')
  const articleDirectory = separator === -1 ? '' : sourcePath.slice(0, separator + 1)
  if (!mediaPath.startsWith(articleDirectory)) {
    throw new Error('The stored media path is not owned by the opened article.')
  }

  const relativePath = mediaPath.slice(articleDirectory.length)
  if (relativePath === '' || relativePath.startsWith('/') || relativePath.split('/').includes('..')) {
    throw new Error('The stored media path cannot be represented safely in article Markdown.')
  }
  return relativePath
}

function resolveOwnedMediaForPreview(markdown: string): string {
  if (activeArticleId === undefined) {
    return markdown
  }

  const mediaEndpoint = `/api/v1/articles/${encodeURIComponent(activeArticleId)}/media/`
  return markdown.replace(
    /(!\[(?:\\.|[^\]\r\n])*\]\()media\/([a-f0-9]{64}\.(?:avif|gif|jpe?g|png|webp))(\))/giu,
    (_match, opening: string, fileName: string, closing: string) =>
      `${opening}${mediaEndpoint}${encodeURIComponent(fileName)}${closing}`,
  )
}

function escapeMarkdownAlt(value: string): string {
  return value.replace(/\s+/gu, ' ').replace(/[\\\[\]]/gu, '\\$&')
}

function surroundBlock(
  value: string,
  start: number,
  end: number,
  block: string,
): { readonly value: string; readonly cursor: number } {
  const before = value.slice(0, start)
  const after = value.slice(end)
  const leading = before === '' || before.endsWith('\n\n')
    ? ''
    : before.endsWith('\n') ? '\n' : '\n\n'
  const trailing = after === ''
    ? '\n'
    : after.startsWith('\n\n') ? '' : after.startsWith('\n') ? '\n' : '\n\n'
  const inserted = `${leading}${block}${trailing}`
  return {
    value: `${before}${inserted}${after}`,
    cursor: before.length + leading.length + block.length,
  }
}

function isMediaReceipt(value: MediaAssetReceiptV1): boolean {
  return value.protocol === 'tezuri.media-asset-receipt' &&
    value.version === 1 &&
    typeof value.relativePath === 'string' &&
    typeof value.fileName === 'string'
}

function isGitSnapshot(value: GitRepositorySnapshotV1): boolean {
  return value.protocol === 'tezuri.git-repository-snapshot' &&
    value.version === 1 &&
    Array.isArray(value.changes) &&
    Array.isArray(value.remoteBranches)
}

function isGitCommitPlan(value: GitCommitPlanV1): boolean {
  return value.protocol === 'tezuri.git-commit-plan' &&
    value.version === 1 &&
    typeof value.planSha256 === 'string' &&
    Array.isArray(value.selectedPaths)
}

function isGitCommitReceipt(value: GitCommitReceiptV1): boolean {
  return value.protocol === 'tezuri.git-commit-receipt' &&
    value.version === 1 &&
    typeof value.afterSha === 'string' &&
    Array.isArray(value.selectedPaths)
}

function isGitPushReceipt(value: GitPushReceiptV1): boolean {
  return value.protocol === 'tezuri.git-push-receipt' &&
    value.version === 1 &&
    typeof value.remoteAfterSha === 'string'
}

function renderArticleList(pendingArticleId?: string): void {
  const query = articleFilter.value.trim().toLocaleLowerCase()
  const visibleArticles = articles.filter((article) =>
    `${article.displayTitle} ${article.relativePath}`.toLocaleLowerCase().includes(query),
  )
  articleList.replaceChildren()

  if (visibleArticles.length === 0) {
    const item = document.createElement('li')
    item.className = 'article-empty'
    item.textContent = articles.length === 0 ? 'No articles found.' : 'No articles match this filter.'
    articleList.append(item)
  } else {
    for (const article of visibleArticles) {
      const item = document.createElement('li')
      const button = document.createElement('button')
      button.className = 'article-item'
      button.type = 'button'
      if (article.id === (pendingArticleId ?? activeArticleId)) {
        button.classList.add('is-active')
        button.setAttribute('aria-current', 'page')
      }
      button.addEventListener('click', () => void openArticle(article.id))

      const title = document.createElement('span')
      title.className = 'article-title'
      title.textContent = article.displayTitle
      const detail = document.createElement('span')
      detail.className = 'article-detail'
      const state = document.createElement('span')
      state.textContent = publicationLabel(article.publicationState)
      detail.append(state, articleTime(article))
      button.append(title, detail)
      item.append(button)
      articleList.append(item)
    }
  }

  const count = articles.length
  const countLabel = `${count} article${count === 1 ? '' : 's'} loaded from the workspace.`
  articleListNote.textContent = query === ''
    ? countLabel
    : `${visibleArticles.length} of ${count} articles match the filter.`
}

function articleTime(article: ArticleSummaryV1): HTMLElement {
  if (article.updatedAt === undefined) {
    const value = document.createElement('span')
    value.textContent = 'Time unavailable'
    return value
  }

  const date = new Date(article.updatedAt)
  if (Number.isNaN(date.valueOf())) {
    const value = document.createElement('span')
    value.textContent = 'Time unavailable'
    return value
  }

  const value = document.createElement('time')
  value.dateTime = article.updatedAt
  value.textContent = new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
  }).format(date)
  return value
}

function showEmptyWorkspace(): void {
  activeArticleId = undefined
  openedEnvelope = undefined
  preparedBody = undefined
  currentPlan = undefined
  sourceInput.value = ''
  sourceInput.disabled = true
  articlePath.textContent = 'No canonical article path'
  articleTitle.textContent = 'No articles in this workspace'
  dirtyState.textContent = 'Nothing opened'
  saveExplanation.textContent = 'Add an article to the mounted repository before editing source.'
  saveButton.disabled = true
  updateDocumentCount('')
  app.dataset.editorState = 'empty'
  announce('The mounted workspace contains no articles.')
}

function showWorkspaceError(error: unknown): void {
  const message = formatError(error)
  articles = []
  renderArticleList()
  setStatusPill(workspaceStatus, 'Connection failed', 'warning')
  articleListNote.textContent = message
  showArticleError(error)
}

function showArticleError(error: unknown): void {
  const message = formatError(error)
  openedEnvelope = undefined
  preparedBody = undefined
  currentPlan = undefined
  sourceInput.value = ''
  sourceInput.disabled = true
  articleTitle.textContent = 'Article source unavailable'
  articlePath.textContent = 'The canonical source could not be opened'
  dirtyState.textContent = 'Open failed'
  dirtyState.classList.add('has-change')
  saveExplanation.textContent = message
  saveButton.disabled = true
  app.dataset.editorState = 'error'
  announce(`Article source could not be opened. ${message}`)
}

function setSelectedTab(selected: HTMLButtonElement, unselected: HTMLButtonElement): void {
  selected.classList.add('is-selected')
  selected.setAttribute('aria-selected', 'true')
  selected.tabIndex = 0
  unselected.classList.remove('is-selected')
  unselected.setAttribute('aria-selected', 'false')
  unselected.tabIndex = -1
}

function updateDocumentCount(markdown: string): void {
  const words = markdown
    .replace(/[`#>*_|[\]()-]/gu, ' ')
    .trim()
    .split(/\s+/u)
    .filter(Boolean).length
  const wordLabel = words === 1 ? 'word' : 'words'
  const characterLabel = markdown.length === 1 ? 'character' : 'characters'
  documentCount.textContent = `${words} ${wordLabel} · ${markdown.length} ${characterLabel}`
}

function setStatusPill(
  element: HTMLElement,
  label: string,
  tone: 'default' | 'quiet' | 'warning',
): void {
  element.textContent = label
  element.classList.remove('status-pill--quiet', 'status-pill--warning')
  if (tone !== 'default') {
    element.classList.add(`status-pill--${tone}`)
  }
}

function publicationLabel(state: ArticleSummaryV1['publicationState']): string {
  return state === 'draft' ? 'Draft' : state === 'published' ? 'Published' : 'Unknown'
}

function hasUnsavedSource(): boolean {
  return preparedBody !== undefined && sourceInput.value !== preparedBody.editorBody
}

function shortHash(hash: string): string {
  return hash.length <= 12 ? hash : `${hash.slice(0, 12)}…`
}

function formatSavedTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.valueOf())
    ? 'successfully'
    : new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(date)
}

function formatError(error: unknown): string {
  if (error instanceof TezuriApiError) {
    const problem = error.problem
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
  return error instanceof Error ? error.message : 'An unexpected error occurred.'
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function syncBusyState(): void {
  const busy =
    loadingWorkspace || openingArticle || saving || proofRunning || mediaUploading || gitLoading || gitActing
  app.setAttribute('aria-busy', String(busy))
}

let announcementTimer: number | undefined
function announce(message: string): void {
  announcer.textContent = ''
  if (announcementTimer !== undefined) {
    window.clearTimeout(announcementTimer)
  }
  announcementTimer = window.setTimeout(() => {
    announcer.textContent = message
  }, 20)
}

function mustQuery<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector)
  if (element === null) {
    throw new Error(`Required element was not found: ${selector}`)
  }
  return element
}
