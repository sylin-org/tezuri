export interface PromptOptions {
  readonly title: string
  readonly copy?: string
  readonly label: string
  readonly initialValue?: string
  readonly confirmLabel: string
  readonly destructive?: boolean
  /** When set, the typed value must match exactly before the action is allowed. */
  readonly requireExactly?: string
}

const dialog = document.querySelector<HTMLDialogElement>('#prompt-dialog')
const form = document.querySelector<HTMLFormElement>('#prompt-form')
const titleEl = document.querySelector<HTMLElement>('#prompt-title')
const copyEl = document.querySelector<HTMLElement>('#prompt-copy')
const labelEl = document.querySelector<HTMLElement>('#prompt-label')
const inputEl = document.querySelector<HTMLInputElement>('#prompt-input')
const errorEl = document.querySelector<HTMLElement>('#prompt-error')
const confirmEl = document.querySelector<HTMLButtonElement>('#prompt-confirm')

/**
 * A focused dialog for the few moments that genuinely need a decision. It replaces `window.confirm`,
 * which offered to throw away a person's work in a browser chrome box they cannot read carefully.
 *
 * Resolves to the entered value, or undefined when the person backs out.
 */
export function openPrompt(options: PromptOptions): Promise<string | undefined> {
  if (
    dialog === null ||
    form === null ||
    titleEl === null ||
    copyEl === null ||
    labelEl === null ||
    inputEl === null ||
    errorEl === null ||
    confirmEl === null
  ) {
    return Promise.resolve(undefined)
  }

  titleEl.textContent = options.title
  copyEl.textContent = options.copy ?? ''
  copyEl.hidden = options.copy === undefined
  labelEl.textContent = options.label
  inputEl.value = options.initialValue ?? ''
  inputEl.placeholder = options.requireExactly ?? ''
  confirmEl.textContent = options.confirmLabel
  confirmEl.classList.toggle('button--danger', options.destructive === true)
  errorEl.hidden = true
  errorEl.textContent = ''

  return new Promise((resolve) => {
    const validate = (): boolean => {
      const value = inputEl.value.trim()
      if (value === '') {
        showError('Enter a value to continue.')
        return false
      }
      if (options.requireExactly !== undefined && value !== options.requireExactly) {
        showError(`Type ${options.requireExactly} exactly to confirm.`)
        return false
      }
      return true
    }

    const showError = (message: string): void => {
      errorEl.textContent = message
      errorEl.hidden = false
    }

    const onSubmit = (event: SubmitEvent): void => {
      const submitter = event.submitter as HTMLButtonElement | null
      if (submitter?.value === 'confirm' && !validate()) {
        event.preventDefault()
      }
    }

    const onClose = (): void => {
      form.removeEventListener('submit', onSubmit)
      dialog.removeEventListener('close', onClose)
      inputEl.removeEventListener('input', onInput)
      resolve(dialog.returnValue === 'confirm' ? inputEl.value.trim() : undefined)
    }

    const onInput = (): void => {
      errorEl.hidden = true
    }

    form.addEventListener('submit', onSubmit)
    dialog.addEventListener('close', onClose)
    inputEl.addEventListener('input', onInput)
    dialog.returnValue = ''
    dialog.showModal()
    inputEl.focus()
    inputEl.select()
  })
}
