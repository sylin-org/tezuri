import { mkdir, readFile, writeFile } from 'node:fs/promises'

const articleUrl = new URL('../src/writing/first-proof/index.md', import.meta.url)
const outputDirectoryUrl = new URL('../dist/', import.meta.url)
const outputUrl = new URL('index.html', outputDirectoryUrl)
const article = await readFile(articleUrl, 'utf8')

if (!article.includes('title: The first proof') || !article.includes('## The boundary')) {
  throw new Error('The sample article is missing the source expected by its target proof.')
}

await mkdir(outputDirectoryUrl, { recursive: true })
await writeFile(
  outputUrl,
  '<!doctype html><html lang="en"><meta charset="utf-8"><title>The first proof</title><main><h1>The first proof</h1><p>The repository target proof passed.</p></main></html>\n',
  'utf8',
)

console.log('Sample target proof passed: dist/index.html')
