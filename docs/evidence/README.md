# Evidence records

Evidence is dated proof of an actual run. Add records only after the event; never create aspirational
digests, release URLs, test results, corpus counts, or deployment revisions.

Suggested layout:

```text
evidence/
  dogfood/YYYY-MM-DD-kintsugi.md
  releases/vX.Y.Z.md
  browser/YYYY-MM-DD-<slice>.md
```

Each record names source revisions, environment, exact commands, inputs/manifests, summarized output,
artifact hashes/URLs, reviewed warnings, failures/retries, and remaining limitations. Exclude secrets,
private content, raw credentials, and unnecessarily sensitive logs.

