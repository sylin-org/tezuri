# `local/`

Working memory for whoever is at the keyboard. This directory is gitignored apart from this README,
so nothing in it reaches the public repository.

## What belongs here

`NOTES.md` holds the things that are useful to carry between sessions but must not be committed:

- owner and environment context (paths, machine-specific setup, which checkouts are where);
- **locations** of credentials and tokens — never the values themselves;
- session handoffs: what was in flight, what to pick up next;
- scratch observations that have not earned a place in a durable document yet.

## What does not belong here

- Secrets, tokens, keys, or passwords in plain text. Record where a credential lives, not what it is.
- Anything that is durable project truth. Standing preferences and learnings belong in the tracked
  [`docs/MEMORY.md`](../docs/MEMORY.md); decisions belong in [`docs/decisions/`](../docs/decisions);
  current state belongs in the document that owns it.

If a note in `NOTES.md` turns out to matter beyond this machine, promote it to the right tracked
document and delete it here.
