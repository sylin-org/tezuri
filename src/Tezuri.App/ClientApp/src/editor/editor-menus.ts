import { commandsCtx, editorViewCtx } from '@milkdown/kit/core'
import type { Ctx } from '@milkdown/kit/ctx'
import {
  createCodeBlockCommand,
  insertHrCommand,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  toggleStrongCommand,
  turnIntoTextCommand,
  wrapInBlockquoteCommand,
  wrapInBulletListCommand,
  wrapInHeadingCommand,
  wrapInOrderedListCommand,
} from '@milkdown/kit/preset/commonmark'
import { toggleStrikethroughCommand } from '@milkdown/kit/preset/gfm'
import type { EditorState } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'

export interface MenuAction {
  readonly id: string
  readonly label: string
  /** Words a person is likely to type for this block, matched against the slash query. */
  readonly keywords: readonly string[]
  readonly hint: string
  readonly run: (ctx: Ctx) => void
}

/** Blocks the slash menu offers, limited to the vocabulary the content contract sanctions. */
export const SLASH_ACTIONS: readonly MenuAction[] = [
  {
    id: 'heading-2',
    label: 'Heading',
    keywords: ['heading', 'h2', 'title', 'section'],
    hint: 'Section heading',
    run: (ctx) => ctx.get(commandsCtx).call(wrapInHeadingCommand.key, 2),
  },
  {
    id: 'heading-3',
    label: 'Subheading',
    keywords: ['subheading', 'h3', 'sub'],
    hint: 'Smaller heading',
    run: (ctx) => ctx.get(commandsCtx).call(wrapInHeadingCommand.key, 3),
  },
  {
    id: 'text',
    label: 'Text',
    keywords: ['text', 'paragraph', 'body', 'plain'],
    hint: 'Plain paragraph',
    run: (ctx) => ctx.get(commandsCtx).call(turnIntoTextCommand.key),
  },
  {
    id: 'bullet-list',
    label: 'Bulleted list',
    keywords: ['bullet', 'list', 'unordered', 'ul'],
    hint: 'Unordered list',
    run: (ctx) => ctx.get(commandsCtx).call(wrapInBulletListCommand.key),
  },
  {
    id: 'ordered-list',
    label: 'Numbered list',
    keywords: ['number', 'ordered', 'list', 'ol'],
    hint: 'Ordered list',
    run: (ctx) => ctx.get(commandsCtx).call(wrapInOrderedListCommand.key),
  },
  {
    id: 'quote',
    label: 'Quote',
    keywords: ['quote', 'blockquote', 'citation'],
    hint: 'Block quotation',
    run: (ctx) => ctx.get(commandsCtx).call(wrapInBlockquoteCommand.key),
  },
  {
    id: 'code',
    label: 'Code block',
    keywords: ['code', 'fence', 'pre', 'snippet'],
    hint: 'Fenced code',
    run: (ctx) => ctx.get(commandsCtx).call(createCodeBlockCommand.key),
  },
  {
    id: 'divider',
    label: 'Divider',
    keywords: ['divider', 'rule', 'hr', 'separator', 'break'],
    hint: 'Horizontal rule',
    run: (ctx) => ctx.get(commandsCtx).call(insertHrCommand.key),
  },
]

/** Inline marks offered by the selection bubble. */
export const BUBBLE_ACTIONS: readonly MenuAction[] = [
  {
    id: 'strong',
    label: 'B',
    keywords: [],
    hint: 'Bold',
    run: (ctx) => ctx.get(commandsCtx).call(toggleStrongCommand.key),
  },
  {
    id: 'emphasis',
    label: 'I',
    keywords: [],
    hint: 'Italic',
    run: (ctx) => ctx.get(commandsCtx).call(toggleEmphasisCommand.key),
  },
  {
    id: 'strike',
    label: 'S',
    keywords: [],
    hint: 'Strikethrough',
    run: (ctx) => ctx.get(commandsCtx).call(toggleStrikethroughCommand.key),
  },
  {
    id: 'inline-code',
    label: '</>',
    keywords: [],
    hint: 'Inline code',
    run: (ctx) => ctx.get(commandsCtx).call(toggleInlineCodeCommand.key),
  },
]

export function matchSlashActions(query: string): readonly MenuAction[] {
  const normalized = query.trim().toLocaleLowerCase()
  if (normalized === '') {
    return SLASH_ACTIONS
  }

  return SLASH_ACTIONS.filter(
    (action) =>
      action.label.toLocaleLowerCase().includes(normalized) ||
      action.keywords.some((keyword) => keyword.startsWith(normalized)),
  )
}

/**
 * A keyboard-complete list. The slash menu owns arrow keys, Enter, and Escape while it is open, so
 * a block can be inserted without reaching for the mouse.
 */
export class MenuList {
  readonly element: HTMLElement
  #actions: readonly MenuAction[] = []
  #activeIndex = 0
  #onPick: (action: MenuAction) => void

  constructor(className: string, onPick: (action: MenuAction) => void) {
    this.#onPick = onPick
    this.element = document.createElement('div')
    this.element.className = className
    this.element.setAttribute('role', 'listbox')
    this.element.setAttribute('aria-label', 'Insert a block')
    // Keep focus in the document so typing continues to reach ProseMirror.
    this.element.addEventListener('mousedown', (event) => event.preventDefault())
  }

  get activeAction(): MenuAction | undefined {
    return this.#actions[this.#activeIndex]
  }

  get isEmpty(): boolean {
    return this.#actions.length === 0
  }

  render(actions: readonly MenuAction[]): void {
    this.#actions = actions
    this.#activeIndex = 0
    this.#paint()
  }

  move(delta: number): void {
    if (this.#actions.length === 0) {
      return
    }

    const count = this.#actions.length
    this.#activeIndex = (this.#activeIndex + delta + count) % count
    this.#paint()
  }

  #paint(): void {
    this.element.replaceChildren(
      ...this.#actions.map((action, index) => {
        const item = document.createElement('button')
        item.type = 'button'
        item.className = 'menu-item'
        item.setAttribute('role', 'option')
        item.setAttribute('aria-selected', String(index === this.#activeIndex))
        if (index === this.#activeIndex) {
          item.classList.add('is-active')
        }

        const label = document.createElement('span')
        label.className = 'menu-item-label'
        label.textContent = action.label
        const hint = document.createElement('span')
        hint.className = 'menu-item-hint'
        hint.textContent = action.hint
        item.append(label, hint)
        item.addEventListener('click', () => this.#onPick(action))
        item.addEventListener('mouseenter', () => {
          this.#activeIndex = index
          this.#paint()
        })
        return item
      }),
    )
  }
}

/** True when the selection is a non-empty text range, which is when the bubble earns its place. */
export function hasTextSelection(view: EditorView, _previous?: EditorState): boolean {
  const { state } = view
  const { selection, doc } = state
  if (selection.empty || !view.editable) {
    return false
  }

  return doc.textBetween(selection.from, selection.to).trim() !== ''
}

export function focusEditor(ctx: Ctx): void {
  ctx.get(editorViewCtx).focus()
}
