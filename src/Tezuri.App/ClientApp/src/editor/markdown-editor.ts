import {
  Editor,
  defaultValueCtx,
  editorViewCtx,
  editorViewOptionsCtx,
  rootCtx,
} from '@milkdown/kit/core'
import type { Ctx } from '@milkdown/kit/ctx'
import { commonmark } from '@milkdown/kit/preset/commonmark'
import { gfm } from '@milkdown/kit/preset/gfm'
import { block, blockConfig, BlockProvider } from '@milkdown/kit/plugin/block'
import { clipboard } from '@milkdown/kit/plugin/clipboard'
import { cursor } from '@milkdown/kit/plugin/cursor'
import { history } from '@milkdown/kit/plugin/history'
import { indent } from '@milkdown/kit/plugin/indent'
import { listener, listenerCtx } from '@milkdown/kit/plugin/listener'
import { slashFactory, SlashProvider } from '@milkdown/kit/plugin/slash'
import { tooltipFactory, TooltipProvider } from '@milkdown/kit/plugin/tooltip'
import { trailing } from '@milkdown/kit/plugin/trailing'
import { upload, uploadConfig } from '@milkdown/kit/plugin/upload'
import type { Uploader } from '@milkdown/kit/plugin/upload'
import type { EditorState } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'
import { getMarkdown, replaceAll } from '@milkdown/kit/utils'

import '@milkdown/kit/prose/view/style/prosemirror.css'
import '@milkdown/kit/prose/gapcursor/style/gapcursor.css'

import {
  BUBBLE_ACTIONS,
  MenuList,
  hasTextSelection,
  matchSlashActions,
  type MenuAction,
} from './editor-menus'

const slash = slashFactory('tezuri-slash')
const bubble = tooltipFactory('tezuri-bubble')

export interface MarkdownEditorOptions {
  readonly host: HTMLElement
  readonly markdown: string
  readonly accessibleName: string
  readonly readonly?: boolean
  readonly uploader?: Uploader
  readonly onChange?: (markdown: string) => void
}

/**
 * Ephemeral Milkdown projection. Canonical source envelopes and source patches stay outside this
 * adapter so ProseMirror state can never become an accidental persistence contract.
 *
 * The editing affordances live in the document rather than in a permanent toolbar: a gutter handle
 * for dragging and inserting, a slash menu for blocks, and a bubble on selection for inline marks.
 */
export class MarkdownEditor {
  readonly #editor: Editor
  readonly #host: HTMLElement
  readonly #teardown: (() => void)[] = []
  readonly #gate: { suppress: boolean }

  private constructor(
    editor: Editor,
    host: HTMLElement,
    teardown: (() => void)[],
    gate: { suppress: boolean },
  ) {
    this.#editor = editor
    this.#host = host
    this.#teardown = teardown
    this.#gate = gate
  }

  static async create(options: MarkdownEditorOptions): Promise<MarkdownEditor> {
    const teardown: (() => void)[] = []
    const editable = options.readonly !== true
    // Parsing the initial document emits an update whose markdown is the serializer's rendering of
    // the source, not something a person typed. Reporting that as an edit would make every open
    // look dirty and race an autosave against the file it just read.
    const gate = { suppress: true }

    const editor = await Editor.make()
      .config((ctx) => {
        ctx.set(rootCtx, options.host)
        ctx.set(defaultValueCtx, options.markdown)
        ctx.update(editorViewOptionsCtx, (previous) => ({
          ...previous,
          editable: () => editable,
          attributes: {
            ...(previous.attributes as Record<string, string> | undefined),
            'aria-label': options.accessibleName,
            class: 'tezuri-prose',
          },
        }))

        ctx.get(listenerCtx).markdownUpdated((_ctx, markdown, previousMarkdown) => {
          if (options.onChange !== undefined && markdown !== previousMarkdown && !gate.suppress) {
            options.onChange(markdown)
          }
        })

        // The handle attaches to whole blocks only; inline nodes keep the gutter quiet.
        ctx.set(blockConfig.key, {
          filterNodes: (_pos, node) => node.type.name !== 'listItem',
        })

        if (options.uploader !== undefined) {
          ctx.update(uploadConfig.key, (previous) => ({
            ...previous,
            uploader: options.uploader!,
            enableHtmlFileUploader: false,
          }))
        }

        if (editable) {
          configureSlash(ctx, teardown)
          configureBubble(ctx, teardown)
          configureBlockHandle(ctx, options.host, teardown)
        }
      })
      .use(commonmark)
      .use(gfm)
      .use(listener)
      .use(history)
      .use(clipboard)
      .use(cursor)
      .use(indent)
      .use(trailing)
      .use(block)
      .use(upload)
      .use(slash)
      .use(bubble)
      .create()

    const surface = options.host.querySelector<HTMLElement>('.ProseMirror')
    if (surface !== null) {
      if (editable) {
        surface.setAttribute('role', 'textbox')
        surface.setAttribute('aria-multiline', 'true')
        surface.setAttribute('spellcheck', 'true')
      } else {
        surface.setAttribute('role', 'document')
        surface.setAttribute('aria-readonly', 'true')
        surface.setAttribute('tabindex', '0')
      }
    }

    const instance = new MarkdownEditor(editor, options.host, teardown, gate)
    gate.suppress = false
    return instance
  }

  getMarkdown(): string {
    return this.#editor.action(getMarkdown())
  }

  /**
   * Replaces the whole document from a source of truth outside the editor. Marked so the change
   * listener does not report it back as if a person had typed it.
   */
  replaceMarkdown(markdown: string): void {
    this.#gate.suppress = true
    try {
      this.#editor.action(replaceAll(markdown, true))
    } finally {
      this.#gate.suppress = false
    }
  }

  focus(): void {
    this.#editor.action((ctx) => {
      ctx.get(editorViewCtx).focus()
    })
  }

  async destroy(): Promise<void> {
    for (const dispose of this.#teardown.splice(0)) {
      dispose()
    }
    await this.#editor.destroy()
    this.#host.replaceChildren()
  }
}

function configureSlash(ctx: Ctx, teardown: (() => void)[]): void {
  let provider: SlashProvider | undefined
  const list = new MenuList('tezuri-menu tezuri-slash-menu', (action) => {
    runAction(ctx, action, () => provider?.hide())
  })

  ctx.set(slash.key, {
    view: () => {
      provider = new SlashProvider({
        content: list.element,
        trigger: '/',
        shouldShow(view) {
          if (!view.editable) {
            return false
          }

          const query = provider?.getContent(view) ?? ''
          if (!query.startsWith('/')) {
            return false
          }

          const matches = matchSlashActions(query.slice(1))
          list.render(matches)
          return matches.length > 0
        },
      })

      return {
        update: (updatedView: EditorView, prevState?: EditorState) =>
          provider?.update(updatedView, prevState),
        destroy: () => provider?.destroy(),
      }
    },
  })

  const onKeyDown = (event: KeyboardEvent) => {
    if (list.element.parentElement === null || list.isEmpty) {
      return
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      list.move(event.key === 'ArrowDown' ? 1 : -1)
      return
    }

    if (event.key === 'Enter' || event.key === 'Tab') {
      const action = list.activeAction
      if (action !== undefined) {
        event.preventDefault()
        runAction(ctx, action, () => provider?.hide())
      }
      return
    }

    if (event.key === 'Escape') {
      event.preventDefault()
      provider?.hide()
    }
  }

  document.addEventListener('keydown', onKeyDown, true)
  teardown.push(() => document.removeEventListener('keydown', onKeyDown, true))
}

function configureBubble(ctx: Ctx, teardown: (() => void)[]): void {
  let provider: TooltipProvider | undefined
  const bar = document.createElement('div')
  bar.className = 'tezuri-menu tezuri-bubble'
  bar.setAttribute('role', 'toolbar')
  bar.setAttribute('aria-label', 'Text formatting')
  bar.addEventListener('mousedown', (event) => event.preventDefault())

  for (const action of BUBBLE_ACTIONS) {
    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'bubble-button'
    button.textContent = action.label
    button.title = action.hint
    button.setAttribute('aria-label', action.hint)
    if (action.id === 'inline-code') {
      button.classList.add('bubble-button--code')
    }
    button.addEventListener('click', () => runAction(ctx, action))
    bar.append(button)
  }

  ctx.set(bubble.key, {
    view: () => {
      provider = new TooltipProvider({
        content: bar,
        shouldShow: hasTextSelection,
      })

      return {
        update: (updatedView: EditorView, prevState?: EditorState) =>
          provider?.update(updatedView, prevState),
        destroy: () => provider?.destroy(),
      }
    },
  })

  teardown.push(() => provider?.destroy())
}

function configureBlockHandle(ctx: Ctx, host: HTMLElement, teardown: (() => void)[]): void {
  const handle = document.createElement('div')
  handle.className = 'tezuri-block-handle'

  const insert = document.createElement('button')
  insert.type = 'button'
  insert.className = 'block-handle-button'
  insert.textContent = '+'
  insert.title = 'Insert a block'
  insert.setAttribute('aria-label', 'Insert a block below')

  const grip = document.createElement('button')
  grip.type = 'button'
  grip.className = 'block-handle-button block-handle-button--grip'
  grip.textContent = '⠿'
  grip.title = 'Drag to move this block'
  grip.setAttribute('aria-label', 'Drag to move this block')
  grip.draggable = true

  handle.append(insert, grip)

  let provider: BlockProvider | undefined
  insert.addEventListener('click', () => {
    // Typing the trigger is what opens the slash menu, so the plus writes it for the writer.
    const view = ctx.get(editorViewCtx)
    const { state, dispatch } = view
    const active = provider?.active
    if (active === null || active === undefined) {
      return
    }

    const insertPos = active.$pos.pos + active.node.nodeSize - 1
    const paragraph = state.schema.nodes.paragraph
    if (paragraph === undefined) {
      return
    }

    const transaction = state.tr.insert(insertPos, paragraph.create())
    dispatch(transaction.scrollIntoView())
    view.focus()
  })

  queueMicrotask(() => {
    provider = new BlockProvider({ ctx, content: handle })
    provider.update()
  })

  teardown.push(() => {
    provider?.destroy()
    handle.remove()
  })
  void host
}

function runAction(ctx: Ctx, action: MenuAction, afterRun?: () => void): void {
  action.run(ctx)
  afterRun?.()
  ctx.get(editorViewCtx).focus()
}
