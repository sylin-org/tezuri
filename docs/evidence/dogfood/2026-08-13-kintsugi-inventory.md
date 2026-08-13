# Kintsugi public inventory — 2026-08-13

This is discovery evidence, not an import or publication receipt. The public feed and archive were
read over HTTPS at `2026-08-13T20:34:04Z`; no article text or remote media was written into Tezuri.

## Reviewed result

- Publication: **Kintsugi Architecture**
- Public articles discovered: **3**
- Feed articles: **3**
- Archive article routes: **3**
- Reviewed exclusions: **3** corresponding `/comments` routes
- Feed/archive disagreement: **none** after excluding comment routes
- Paid/private text claimed: **none**

| Published (UTC) | Article | Canonical source | Feed body SHA-256 |
|---|---|---|---|
| 2026-08-03 | MCP 2026-07-28: The Kid Looks Adorable in a Tuxedo | `https://kintsugiarchitecture.substack.com/p/mcp-2026-07-28-the-kid-looks-adorable` | `08e3d289657e25f50d09253b19f74d6f7d7558ec2e502c95bf3b340a9bcbd2f3` |
| 2026-03-21 | Craft, or How You Could Care | `https://kintsugiarchitecture.substack.com/p/craft-or-how-you-could-care` | `4881000408600bbf99c7dc6d2befed47c8b8fc1f42419c8cce983b538afb8c26` |
| 2026-03-17 | Why Your Best Ideas Come in the Shower | `https://kintsugiarchitecture.substack.com/p/why-your-best-ideas-come-in-the-shower` | `088a411a12eb2cb8b315e97a04c5f451fee644951c1349222ef1ecdcb60a6f2e` |

Transport artifact hashes from this observation:

- feed UTF-8 bytes: 52,537; SHA-256
  `f70e6c915bf346755575ada4c58064badda06de6ddec9a63a89608c10ae651cd`
- archive UTF-8 bytes: 127,282; SHA-256
  `5157793b30665a8569332126e491618eee43289e96d352ae595aba933baf12a2`

Each public feed item exposed one Substack-hosted image and an empty `alt` attribute. Migration must
therefore localize three assets and requires human-authored alt text before publication. The public
feed is useful completeness evidence, but the owner export remains the preferred high-fidelity input.

## Remaining gate

No owner export was present in the mounted Tezuri repository during this inventory. A complete
dogfood run still needs the export, deterministic importer preview, reviewed transformation and
asset warnings, canonical website output, byte-identical no-op proof, and visual/editorial review.
