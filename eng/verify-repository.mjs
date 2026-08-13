import { readFile, readdir } from 'node:fs/promises'
import { extname, join, relative } from 'node:path'
import process from 'node:process'

const root = process.cwd()
const failures = []
const ignored = new Set(['.git', 'bin', 'obj', 'node_modules', 'dist', 'wwwroot'])
const markdownLinkPattern = /!?(?:\[[^\]]*\])\(([^)\s]+)(?:\s+"[^"]*")?\)/g

async function walk(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []
  for (const entry of entries) {
    if (ignored.has(entry.name)) continue
    const path = join(directory, entry.name)
    if (entry.isDirectory()) files.push(...await walk(path))
    else files.push(path)
  }
  return files
}

const files = await walk(root)
const relativeFiles = new Set(files.map(path => relative(root, path).replaceAll('\\', '/')))

for (const file of files) {
  const extension = extname(file).toLowerCase()
  if (extension === '.json') {
    try {
      JSON.parse(await readFile(file, 'utf8'))
    } catch (error) {
      failures.push(`${relative(root, file)}: invalid JSON (${error.message})`)
    }
  }

  if (extension !== '.md') continue
  const source = await readFile(file, 'utf8')
  for (const match of source.matchAll(markdownLinkPattern)) {
    const target = match[1]
    if (/^(?:[a-z][a-z0-9+.-]*:|#)/i.test(target)) continue
    const cleanTarget = decodeURIComponent(target.split('#', 1)[0])
    if (!cleanTarget) continue
    const sourceDirectory = relative(root, file).replaceAll('\\', '/').split('/').slice(0, -1)
    const pieces = [...sourceDirectory, ...cleanTarget.split('/')]
    const normalized = []
    for (const piece of pieces) {
      if (piece === '.' || piece === '') continue
      if (piece === '..') normalized.pop()
      else normalized.push(piece)
    }
    const candidate = normalized.join('/')
    if (!relativeFiles.has(candidate) && ![...relativeFiles].some(item => item.startsWith(`${candidate}/`))) {
      failures.push(`${relative(root, file)}: missing local link ${target}`)
    }
  }
}

const required = [
  'AGENTS.md', 'CHANGELOG.md', 'CODE_OF_CONDUCT.md', 'CONTRIBUTING.md', 'LICENSE', 'README.md',
  'SECURITY.md', 'SUPPORT.md', 'docs/contracts/README.md',
  'schemas/tezuri-common-v1.schema.json', 'schemas/tezuri-workspace-v1.schema.json',
  'tests/Tezuri.Contracts.Tests/Tezuri.Contracts.Tests.csproj',
  'src/Tezuri.App/ClientApp/package-lock.json',
]
for (const path of required) {
  if (!relativeFiles.has(path)) failures.push(`missing required repository file ${path}`)
}

const workflowFiles = files.filter(path => relative(root, path).replaceAll('\\', '/').startsWith('.github/workflows/'))
const actionUsePattern = /^\s*uses:\s*[^#\s]+@([^\s#]+)(?:\s+#.*)?$/gm
for (const file of workflowFiles) {
  const source = await readFile(file, 'utf8')
  for (const match of source.matchAll(actionUsePattern)) {
    if (!/^[0-9a-f]{40}$/.test(match[1])) {
      failures.push(`${relative(root, file)}: action reference is not a full immutable SHA (${match[0].trim()})`)
    }
  }
}

if (failures.length > 0) {
  console.error(failures.join('\n'))
  process.exit(1)
}
console.log(`Repository contracts verified across ${files.length} files.`)
