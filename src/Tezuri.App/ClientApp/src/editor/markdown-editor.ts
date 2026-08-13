import {
  Editor,
  commandsCtx,
  defaultValueCtx,
  editorViewOptionsCtx,
  editorViewCtx,
  rootCtx,
} from '@milkdown/kit/core'
import { gfm } from '@milkdown/kit/preset/gfm'
import {
  commonmark,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  toggleStrongCommand,
} from '@milkdown/kit/preset/commonmark'
import { listener, listenerCtx } from '@milkdown/kit/plugin/listener'
import { getMarkdown, replaceAll } from '@milkdown/kit/utils'

import '@milkdown/kit/prose/view/style/prosemirror.css'

export type MarkdownEditorCommand = 'strong' | 'emphasis' | 'inline-code'

export interface MarkdownEditorOptions {
  readonly host: HTMLElement
  readonly markdown: string
  readonly accessibleName: string
  readonly readonly?: boolean
  readonly onChange?: (markdown: string) => void
}

/**
 * Ephemeral Milkdown projection. Canonical source envelopes and source patches stay outside this
 * adapter so ProseMirror state can never become an accidental persistence contract.
 */
export class MarkdownEditor {
  readonly #editor: Editor
  readonly #host: HTMLElement

  private constructor(editor: Editor, host: HTMLElement) {
    this.#editor = editor
    this.#host = host
  }

  static async create(options: MarkdownEditorOptions): Promise<MarkdownEditor> {
    const editor = await Editor.make()
      .config((context) => {
        context.set(rootCtx, options.host)
        context.set(defaultValueCtx, options.markdown)
        context.update(editorViewOptionsCtx, (previous) => ({
          ...previous,
          editable: () => options.readonly !== true,
        }))
        context.get(listenerCtx).markdownUpdated((_context, markdown, previousMarkdown) => {
          if (options.onChange !== undefined && markdown !== previousMarkdown) {
            options.onChange(markdown)
          }
        })
      })
      .use(commonmark)
      .use(gfm)
      .use(listener)
      .create()

    const editorSurface = options.host.querySelector<HTMLElement>('.ProseMirror')
    editorSurface?.setAttribute('aria-label', options.accessibleName)
    if (options.readonly === true) {
      editorSurface?.setAttribute('role', 'document')
      editorSurface?.setAttribute('aria-readonly', 'true')
      editorSurface?.setAttribute('tabindex', '0')
    } else {
      editorSurface?.setAttribute('role', 'textbox')
      editorSurface?.setAttribute('aria-multiline', 'true')
      editorSurface?.setAttribute('spellcheck', 'true')
    }

    return new MarkdownEditor(editor, options.host)
  }

  getMarkdown(): string {
    return this.#editor.action(getMarkdown())
  }

  replaceMarkdown(markdown: string): void {
    this.#editor.action(replaceAll(markdown, true))
  }

  run(command: MarkdownEditorCommand): boolean {
    const commandKey = {
      strong: toggleStrongCommand.key,
      emphasis: toggleEmphasisCommand.key,
      'inline-code': toggleInlineCodeCommand.key,
    }[command]

    return this.#editor.action((context) => context.get(commandsCtx).call(commandKey))
  }

  focus(): void {
    this.#editor.action((context) => {
      context.get(editorViewCtx).focus()
    })
  }

  async destroy(): Promise<void> {
    await this.#editor.destroy()
    this.#host.replaceChildren()
  }
}
