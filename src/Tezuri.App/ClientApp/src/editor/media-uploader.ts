import type { Ctx } from '@milkdown/kit/ctx'
import type { Fragment, Node, Schema } from '@milkdown/kit/prose/model'
export interface MediaReceipt {
  readonly relativePath: string
  readonly fileName: string
  readonly deduplicated: boolean
}

export interface MediaUploadContext {
  /** The article the dropped file belongs to, resolved at drop time. */
  readonly articleId: () => string | undefined
  readonly upload: (articleId: string, file: File) => Promise<MediaReceipt>
  readonly articleRelativePath: () => string | undefined
  readonly onProblem: (message: string) => void
  readonly onStored: (receipt: MediaReceipt) => void
}

const IMAGE_TYPES = new Set([
  'image/avif',
  'image/gif',
  'image/jpeg',
  'image/png',
  'image/webp',
])

/**
 * Copies dropped or pasted images into the article's own media folder and returns image nodes that
 * reference the stored file by its article-relative path. A remote hotlink is never the result: the
 * bytes are owned by the repository before the node exists.
 *
 * The `src` written into the document is the article-relative path that belongs in the Markdown.
 * Rendering it in the browser needs the media endpoint instead, which is why the caller resolves
 * preview URLs separately rather than storing an endpoint URL in the source.
 */
export function createMediaUploader(context: MediaUploadContext) {
  return async (
    files: FileList,
    schema: Schema,
    _ctx: Ctx,
    _insertPos: number,
  ): Promise<Fragment | Node | Node[]> => {
    const articleId = context.articleId()
    const relativePath = context.articleRelativePath()
    if (articleId === undefined || relativePath === undefined) {
      context.onProblem('Open an article before adding an image.')
      return []
    }

    const nodes: Node[] = []
    for (const file of Array.from(files)) {
      if (!IMAGE_TYPES.has(file.type)) {
        context.onProblem(
          `${file.name || 'That file'} is not an image Tezuri can own yet. Supported: AVIF, GIF, JPEG, PNG, WebP.`,
        )
        continue
      }

      try {
        const receipt = await context.upload(articleId, file)
        const src = articleRelativeMediaPath(relativePath, receipt)
        const imageType = schema.nodes.image
        if (imageType === undefined) {
          context.onProblem('This document cannot hold images.')
          continue
        }

        context.onStored(receipt)
        nodes.push(
          imageType.createAndFill({
            src,
            // Alt is intentionally empty: the writer is prompted for it on the block, where the
            // picture is in front of them, rather than in a dialog before they have seen it.
            alt: '',
            title: null,
          }) ?? imageType.create({ src, alt: '' }),
        )
      } catch (error) {
        context.onProblem(
          error instanceof Error
            ? `${file.name || 'That image'} was not stored. ${error.message}`
            : `${file.name || 'That image'} was not stored.`,
        )
      }
    }

    return nodes
  }
}

/**
 * Expresses a stored asset as a path relative to the article file, and refuses anything that would
 * point outside the article's own folder.
 */
export function articleRelativeMediaPath(
  articleSourcePath: string,
  receipt: MediaReceipt,
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
