import { hasLaunchNonce, sessionApi } from '../api/session-api'
import type {
  GitCommitPlan,
  GitCommitReceipt,
  GitPushReceipt,
  GitRemoteBranch,
  GitRepositorySnapshot,
} from '../api/tezuri-api'

export interface PublishPanelElements {
  readonly status: HTMLElement
  readonly note: HTMLElement
  readonly summary: HTMLElement
  readonly railStatus: HTMLElement
  readonly countBadge: HTMLElement
  readonly pathFieldset: HTMLFieldSetElement
  readonly pathLegend: HTMLElement
  readonly changes: HTMLElement
  readonly commitMessage: HTMLTextAreaElement
  readonly refreshButton: HTMLButtonElement
  readonly reviewButton: HTMLButtonElement
  readonly planReview: HTMLElement
  readonly planSummary: HTMLElement
  readonly planPaths: HTMLUListElement
  readonly commitButton: HTMLButtonElement
  readonly pushPanel: HTMLElement
  readonly pushSummary: HTMLElement
  readonly pushButton: HTMLButtonElement
  readonly alert: HTMLElement
}

export interface PublishPanelHost {
  readonly announce: (message: string) => void
  readonly setBusy: (busy: boolean) => void
  readonly hasUnsavedWork: () => boolean
  readonly formatError: (error: unknown) => string
  readonly setStatusPill: (element: HTMLElement, label: string, tone: StatusTone) => void
}

export type StatusTone = 'default' | 'quiet' | 'warning' | 'success' | 'danger'

interface PendingPush {
  readonly remoteBranch: GitRemoteBranch
  readonly localSha: string
}

/**
 * Publication is a destination, not ambient machinery. Everything here is explicit: the exact paths,
 * the exact message, a review step, then a commit, then an optional push that never forces.
 */
export class PublishPanel {
  readonly #elements: PublishPanelElements
  readonly #host: PublishPanelHost
  #snapshot: GitRepositorySnapshot | undefined
  #plan: GitCommitPlan | undefined
  #pendingPush: PendingPush | undefined
  #loading = false
  #acting = false

  constructor(elements: PublishPanelElements, host: PublishPanelHost) {
    this.#elements = elements
    this.#host = host

    elements.refreshButton.addEventListener('click', () => void this.refresh(true))
    elements.changes.addEventListener('change', () => {
      this.#invalidateReview()
      this.updateControls()
    })
    elements.commitMessage.addEventListener('input', () => {
      this.#invalidateReview()
      this.updateControls()
    })
    elements.reviewButton.addEventListener('click', () => void this.#review())
    elements.commitButton.addEventListener('click', () => void this.#commit())
    elements.pushButton.addEventListener('click', () => void this.#push())
  }

  get changeCount(): number {
    return this.#snapshot?.changes.length ?? 0
  }

  async refresh(announceResult: boolean): Promise<void> {
    if (this.#loading || this.#acting) {
      return
    }

    this.#loading = true
    this.#clearAlert()
    this.#invalidateReview()
    this.#host.setStatusPill(this.#elements.status, 'Checking', 'quiet')
    this.#elements.summary.textContent = 'Reading the mounted repository…'
    this.updateControls()
    this.#host.setBusy(true)

    try {
      const snapshot = await sessionApi.inspectGit()
      if (!isGitSnapshot(snapshot)) {
        throw new Error('The workspace returned an unsupported Git status response.')
      }

      this.#snapshot = snapshot
      this.#renderSnapshot(snapshot)
      if (announceResult) {
        this.#host.announce(describeSnapshot(snapshot))
      }
    } catch (error) {
      this.#snapshot = undefined
      const message = this.#host.formatError(error)
      this.#host.setStatusPill(this.#elements.status, 'Unavailable', 'danger')
      this.#elements.railStatus.textContent = 'Git unavailable'
      this.#elements.summary.textContent = 'The mounted repository could not be inspected.'
      this.#renderChanges(undefined)
      this.#showAlert(message)
      if (announceResult) {
        this.#host.announce(`Git status could not be refreshed. ${message}`)
      }
    } finally {
      this.#loading = false
      this.updateControls()
      this.#host.setBusy(false)
    }
  }

  updateControls(): void {
    const snapshot = this.#snapshot
    const supported =
      snapshot !== undefined &&
      !snapshot.isUnborn &&
      !snapshot.isDetached &&
      snapshot.headSha !== null &&
      snapshot.branch !== null
    const outcomeVisible = !this.#elements.pushPanel.hidden
    const editable = supported && hasLaunchNonce && !this.#loading && !this.#acting && !outcomeVisible

    this.#elements.refreshButton.disabled = this.#loading || this.#acting
    this.#elements.pathFieldset.disabled = !editable
    this.#elements.commitMessage.disabled = !editable
    this.#elements.reviewButton.disabled = !this.#canReview()
    this.#elements.commitButton.disabled =
      this.#plan === undefined ||
      this.#acting ||
      !hasLaunchNonce ||
      this.#host.hasUnsavedWork()
    this.#elements.pushButton.disabled =
      this.#pendingPush === undefined || this.#acting || !hasLaunchNonce

    const count = this.changeCount
    this.#elements.countBadge.hidden = count === 0
    this.#elements.countBadge.textContent = String(count)

    if (!hasLaunchNonce) {
      this.#elements.note.textContent =
        'Changes are visible, but committing needs the launch link Tezuri printed when it started.'
    } else if (this.#host.hasUnsavedWork()) {
      this.#elements.note.textContent = 'Save your draft before committing.'
    } else if (outcomeVisible) {
      this.#elements.note.textContent =
        this.#pendingPush === undefined
          ? 'The commit is local. Refresh to prepare another.'
          : 'The commit is local. Pushing is optional and never forces the remote.'
    } else {
      this.#elements.note.textContent =
        'Choose exactly which files to commit. Nothing is pushed until you say so.'
    }
  }

  #canReview(): boolean {
    const snapshot = this.#snapshot
    return (
      snapshot !== undefined &&
      !snapshot.isUnborn &&
      !snapshot.isDetached &&
      snapshot.headSha !== null &&
      snapshot.branch !== null &&
      hasLaunchNonce &&
      !this.#loading &&
      !this.#acting &&
      this.#elements.pushPanel.hidden &&
      !this.#host.hasUnsavedWork() &&
      this.#elements.commitMessage.value.trim() !== '' &&
      this.#selectedPaths().length > 0
    )
  }

  async #review(): Promise<void> {
    if (!this.#canReview()) {
      return
    }

    const selectedPaths = this.#selectedPaths()
    this.#acting = true
    this.#clearAlert()
    this.#invalidateReview()
    this.#host.setStatusPill(this.#elements.status, 'Reviewing', 'quiet')
    this.updateControls()
    this.#host.setBusy(true)

    try {
      const fresh = await sessionApi.inspectGit()
      if (!isGitSnapshot(fresh)) {
        throw new Error('The workspace returned an unsupported Git status response.')
      }

      this.#snapshot = fresh
      this.#renderSnapshot(fresh, new Set(selectedPaths))
      const stillSelectable = this.#selectedPaths()
      if (
        stillSelectable.length !== selectedPaths.length ||
        selectedPaths.some((path) => !stillSelectable.includes(path))
      ) {
        throw new Error('The repository changed while it was being read. Review the files again.')
      }

      const plan = await sessionApi.planGitCommit({ selectedPaths })
      if (!isGitCommitPlan(plan)) {
        throw new Error('The workspace returned an unsupported Git commit plan.')
      }

      this.#plan = plan
      this.#elements.planSummary.textContent =
        `${plan.branch} at ${shortHash(plan.headSha)} · ${plan.selectedPaths.length} file` +
        `${plan.selectedPaths.length === 1 ? '' : 's'}.`
      this.#elements.planPaths.replaceChildren(
        ...plan.selectedPaths.map((path) => {
          const item = document.createElement('li')
          item.textContent = path
          return item
        }),
      )
      this.#elements.planReview.hidden = false
      this.#host.setStatusPill(this.#elements.status, 'Reviewed', 'default')
      this.#elements.commitButton.focus()
      this.#host.announce('Commit review ready. Creating the commit is a separate step.')
    } catch (error) {
      const message = this.#host.formatError(error)
      this.#host.setStatusPill(this.#elements.status, 'Review failed', 'danger')
      this.#showAlert(message)
      this.#host.announce(`Commit review failed. ${message}`)
    } finally {
      this.#acting = false
      this.updateControls()
      this.#host.setBusy(false)
    }
  }

  async #commit(): Promise<void> {
    const plan = this.#plan
    const message = this.#elements.commitMessage.value.trim()
    if (
      plan === undefined ||
      message === '' ||
      !hasLaunchNonce ||
      this.#acting ||
      this.#host.hasUnsavedWork()
    ) {
      return
    }

    this.#acting = true
    this.#clearAlert()
    this.#elements.commitButton.disabled = true
    this.#elements.commitButton.textContent = 'Creating…'
    this.#host.setStatusPill(this.#elements.status, 'Committing', 'quiet')
    this.updateControls()
    this.#host.setBusy(true)

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

      const remoteBranch = resolveUpstream(this.#snapshot)
      this.#pendingPush =
        remoteBranch === undefined
          ? undefined
          : { remoteBranch, localSha: receipt.afterSha }
      this.#plan = undefined
      this.#elements.planReview.hidden = true
      this.#elements.pushPanel.hidden = false
      this.#renderCommittedPaths(receipt.selectedPaths)
      this.#elements.pushButton.hidden = this.#pendingPush === undefined
      this.#elements.pushButton.textContent =
        this.#pendingPush === undefined
          ? 'Push unavailable'
          : `Push to ${this.#pendingPush.remoteBranch.remote}/${this.#pendingPush.remoteBranch.branch}`
      this.#elements.pushSummary.textContent =
        this.#pendingPush === undefined
          ? `Commit ${shortHash(receipt.afterSha)} is on your machine. No reviewed upstream is available, so Tezuri will not offer a push.`
          : `Commit ${shortHash(receipt.afterSha)} is on your machine. Pushing rechecks ${this.#pendingPush.remoteBranch.remote}/${this.#pendingPush.remoteBranch.branch} and never forces.`
      this.#host.setStatusPill(
        this.#elements.status,
        receipt.created ? 'Committed' : 'Already committed',
        'success',
      )
      this.#elements.railStatus.textContent = `${receipt.branch} · ${shortHash(receipt.afterSha)}`
      this.#elements.summary.textContent =
        `${receipt.selectedPaths.length} file${receipt.selectedPaths.length === 1 ? '' : 's'} committed as ${shortHash(receipt.afterSha)}.`
      if (this.#pendingPush !== undefined) {
        this.#elements.pushButton.focus()
      }
      this.#host.announce(
        this.#pendingPush === undefined
          ? 'Commit created locally. No safe push target is available.'
          : 'Commit created locally. Pushing is optional.',
      )
    } catch (error) {
      const message = this.#host.formatError(error)
      this.#host.setStatusPill(this.#elements.status, 'Commit failed', 'danger')
      this.#showAlert(message)
      this.#host.announce(`Commit failed. ${message}`)
    } finally {
      this.#acting = false
      this.#elements.commitButton.textContent = 'Create commit'
      this.updateControls()
      this.#host.setBusy(false)
    }
  }

  async #push(): Promise<void> {
    const push = this.#pendingPush
    if (push === undefined || !hasLaunchNonce || this.#acting) {
      return
    }

    this.#acting = true
    this.#clearAlert()
    this.#elements.pushButton.disabled = true
    this.#elements.pushButton.textContent = 'Pushing…'
    this.#host.setStatusPill(this.#elements.status, 'Pushing', 'quiet')
    this.updateControls()
    this.#host.setBusy(true)

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

      this.#pendingPush = undefined
      this.#elements.pushButton.hidden = true
      this.#elements.pushSummary.textContent =
        `${shortHash(receipt.remoteAfterSha)} is now on ${receipt.remote}/${receipt.branch}.`
      this.#elements.summary.textContent =
        `${receipt.remote}/${receipt.branch} matches local ${shortHash(receipt.localSha)}.`
      this.#elements.railStatus.textContent = `${receipt.branch} · pushed`
      this.#host.setStatusPill(
        this.#elements.status,
        receipt.pushed ? 'Pushed' : 'Already current',
        'success',
      )
      this.#host.announce(receipt.pushed ? 'Commit pushed.' : 'The remote was already current.')
    } catch (error) {
      const message = this.#host.formatError(error)
      this.#host.setStatusPill(this.#elements.status, 'Push failed', 'danger')
      this.#showAlert(message)
      this.#host.announce(`Push failed. Your commit is safe. ${message}`)
    } finally {
      this.#acting = false
      if (this.#pendingPush !== undefined) {
        this.#elements.pushButton.textContent =
          `Push to ${this.#pendingPush.remoteBranch.remote}/${this.#pendingPush.remoteBranch.branch}`
      }
      this.updateControls()
      this.#host.setBusy(false)
    }
  }

  #renderSnapshot(
    snapshot: GitRepositorySnapshot,
    selectedPaths: ReadonlySet<string> = new Set(),
  ): void {
    this.#elements.pathLegend.textContent = 'Changed files'
    const allowed = snapshot.changes.filter((change) => change.allowed).length
    const branch = snapshot.branch ?? 'No branch'
    const upstream = snapshot.upstream === null ? 'no upstream' : `tracks ${snapshot.upstream}`

    if (snapshot.isUnborn) {
      this.#host.setStatusPill(this.#elements.status, 'No commits yet', 'warning')
      this.#elements.summary.textContent =
        'This repository has no first commit yet. Make one outside Tezuri, then come back.'
    } else if (snapshot.isDetached) {
      this.#host.setStatusPill(this.#elements.status, 'Detached HEAD', 'warning')
      this.#elements.summary.textContent = 'Check out a branch before committing.'
    } else if (snapshot.changes.length === 0) {
      this.#host.setStatusPill(this.#elements.status, 'Clean', 'success')
      this.#elements.summary.textContent =
        `${branch} at ${snapshot.headSha === null ? 'no commit' : shortHash(snapshot.headSha)} · ${upstream} · nothing changed.`
    } else {
      this.#host.setStatusPill(this.#elements.status, `${snapshot.changes.length} changed`, 'default')
      this.#elements.summary.textContent =
        `${branch} at ${snapshot.headSha === null ? 'no commit' : shortHash(snapshot.headSha)} · ${upstream} · ${allowed} of ${snapshot.changes.length} publishable.`
    }

    this.#elements.railStatus.textContent =
      snapshot.changes.length === 0 ? `${branch} · clean` : `${branch} · ${snapshot.changes.length} changed`
    this.#renderChanges(snapshot, selectedPaths)
  }

  #renderChanges(
    snapshot: GitRepositorySnapshot | undefined,
    selectedPaths: ReadonlySet<string> = new Set(),
  ): void {
    this.#elements.changes.replaceChildren()
    if (snapshot === undefined || snapshot.changes.length === 0) {
      const empty = document.createElement('p')
      empty.textContent =
        snapshot === undefined ? 'No Git status loaded.' : 'Nothing has changed in this repository.'
      this.#elements.changes.append(empty)
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
      state.textContent = describeChange(change.indexStatus, change.workTreeStatus, change.allowed)
      label.append(checkbox, path, state)
      this.#elements.changes.append(label)
    }
  }

  #renderCommittedPaths(paths: readonly string[]): void {
    this.#elements.pathLegend.textContent = 'Committed files'
    this.#elements.changes.replaceChildren()
    for (const committed of paths) {
      const row = document.createElement('div')
      row.className = 'publication-change'
      const check = document.createElement('span')
      check.textContent = '✓'
      check.setAttribute('aria-hidden', 'true')
      const path = document.createElement('span')
      path.className = 'publication-change-path'
      path.textContent = committed
      const state = document.createElement('span')
      state.className = 'publication-change-state'
      state.textContent = 'committed'
      row.append(check, path, state)
      this.#elements.changes.append(row)
    }
  }

  #selectedPaths(): string[] {
    return Array.from(
      this.#elements.changes.querySelectorAll<HTMLInputElement>('input[data-git-path]:checked'),
    )
      .filter((input) => !input.disabled)
      .map((input) => input.dataset.gitPath)
      .filter((path): path is string => path !== undefined)
  }

  #invalidateReview(): void {
    this.#plan = undefined
    this.#pendingPush = undefined
    this.#elements.planReview.hidden = true
    this.#elements.pushPanel.hidden = true
    this.#elements.pushButton.hidden = false
    this.#clearAlert()
  }

  #showAlert(message: string): void {
    this.#elements.alert.textContent = message
    this.#elements.alert.hidden = false
  }

  #clearAlert(): void {
    this.#elements.alert.textContent = ''
    this.#elements.alert.hidden = true
  }
}

function describeChange(index: string, workTree: string, allowed: boolean): string {
  const states: string[] = []
  if (index === 'untracked' && workTree === 'untracked') {
    states.push('new')
  } else {
    if (index !== 'none') {
      states.push(`staged ${index}`)
    }
    if (workTree !== 'none') {
      states.push(workTree)
    }
  }
  if (!allowed) {
    states.push('not publishable')
  }
  return states.join(' · ') || 'changed'
}

function describeSnapshot(snapshot: GitRepositorySnapshot): string {
  if (snapshot.isUnborn) {
    return 'Git refreshed. This branch has no commit yet.'
  }
  if (snapshot.isDetached) {
    return 'Git refreshed. HEAD is detached.'
  }
  return snapshot.changes.length === 0
    ? `Git refreshed. ${snapshot.branch ?? 'The repository'} is clean.`
    : `Git refreshed with ${snapshot.changes.length} changed file${snapshot.changes.length === 1 ? '' : 's'}.`
}

function resolveUpstream(
  snapshot: GitRepositorySnapshot | undefined,
): GitRemoteBranch | undefined {
  if (snapshot?.upstream === null || snapshot?.upstream === undefined) {
    return undefined
  }
  return snapshot.remoteBranches.find(
    (candidate) => `${candidate.remote}/${candidate.branch}` === snapshot.upstream,
  )
}

export function shortHash(hash: string): string {
  return hash.length <= 10 ? hash : hash.slice(0, 10)
}

function isGitSnapshot(value: GitRepositorySnapshot): boolean {
  return (
    Array.isArray(value.changes) &&
    Array.isArray(value.remoteBranches)
  )
}

function isGitCommitPlan(value: GitCommitPlan): boolean {
  return (
    typeof value.planSha256 === 'string' &&
    Array.isArray(value.selectedPaths)
  )
}

function isGitCommitReceipt(value: GitCommitReceipt): boolean {
  return (
    typeof value.afterSha === 'string' &&
    Array.isArray(value.selectedPaths)
  )
}

function isGitPushReceipt(value: GitPushReceipt): boolean {
  return (
    typeof value.remoteAfterSha === 'string'
  )
}
