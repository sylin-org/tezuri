# Repository settings

The public repository is `sylin-org/tezuri`; the verified sole public organization member at project
startup is `@lbotinelly`. `CODEOWNERS` uses that identity. Recheck membership before changing owners.

Repository metadata and protection are external state. Inspect with:

```sh
gh repo view sylin-org/tezuri --json description,homepageUrl,visibility,defaultBranchRef
gh api repos/sylin-org/tezuri/rulesets
gh api repos/sylin-org/tezuri/tags/protection
```

After the first coherent foundation commit and push, an owner should explicitly authorize setting:

- description: `A local writing room for repository-native Bundling Ways.`
- homepage: `https://sylin.org/tezuri/` only after that route is real (otherwise leave blank);
- topics: `local-first`, `markdown`, `writing`, `static-site`, `dotnet`;
- Discussions if the owner wants the support route described in `SUPPORT.md`;
- private vulnerability reporting;
- a `main` ruleset requiring green `CI / Restore, build, test, and smoke`, blocking force pushes and
  deletion, without requiring unavailable reviewers;
- protected `v*` release tags; and
- public GHCR visibility after the first verified image is published.

Apply through narrow `gh api` calls or the GitHub UI, capture the request/response without tokens,
and immediately read every setting back. Do not guess an empty-branch ruleset target or publish a
release simply to make documentation true.

