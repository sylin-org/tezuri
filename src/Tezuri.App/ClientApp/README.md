# Tezuri client

This directory is the bundled, client-only Tezuri writing-room shell. It uses vanilla TypeScript,
Vite, and direct `@milkdown/kit` integration. It intentionally does not use Crepe, an editor
framework wrapper, or an editor-native JSON persistence format.

```powershell
npm ci
npm run check
npm test
npm run build
```

The production build replaces `../wwwroot` with the generated app shell that ASP.NET Core serves.
Production source maps are intentionally disabled; local Vite development retains normal browser
debugging support without publishing source maps in the container image.

`src/source-protocol.ts` owns the browser side of the permanent source boundary. Milkdown state is
an ephemeral editing projection behind `src/editor/markdown-editor.ts`; only Tezuri
`ArticleSourceEnvelopeV1` and `SourcePatchSetV1` contracts may cross the API boundary.

The client loads the article list and canonical source envelopes from `/api/v1/articles`. Markdown
source mode exposes only the body slice. A supported edit produces one localized, expected-byte
replacement against the opened SHA-256 base; frontmatter and surrounding canonical bytes never
pass through the textarea. The client emits no patch for an unchanged body, preserves homogeneous
CRLF source, and disables save when a replacement crosses a server-declared protected raw segment
or cannot be encoded losslessly. A 409 keeps the unsaved draft visible alongside the returned
repository body.

Milkdown is deliberately a read-only rich preview in this slice. Its serialization is not trusted
for persistence. The current server codec does not yet discover unsupported blocks into protected
segments; runtime block discovery, site proof, and broad golden-corpus fidelity remain separate
evidence gates.
