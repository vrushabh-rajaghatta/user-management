# Ligature — Claude Instructions

This file is intentionally thin. It points to the two documents that govern
work in this repository and records only what Claude must not get wrong.

## Source of Truth

- **`AGENTS.md`** — operational instructions: the owner-driven story workflow,
  repository commands, validation expectations, branch and commit conventions,
  and escalation rules. Authoritative on how to work.
- **`docs/architecture.md`** — the architectural contract: module boundaries,
  packaging and the host application, established implementation patterns.
  Authoritative on architectural questions.
- **`docs/requirements.md`** — the requirement catalogue and the Known Gaps /
  Deliberate Deferrals list. Being back-filled; not a prerequisite for current
  work.

Read the relevant parts of `AGENTS.md` and `docs/architecture.md` before
implementing a story.

## Working Style

Follow the owner-driven workflow in `AGENTS.md` §2 for substantive
implementation work: analyse, explain, clarify and plan first, and do not
implement until the owner has approved the plan. Genuinely trivial
owner-requested changes are exempt as `AGENTS.md` §2 defines; when in doubt,
they are not trivial.

Do not make architectural decisions silently. Follow the established patterns
in `docs/architecture.md` §11 and preserve module boundaries. When an important
requirement or architectural decision is unclear, ask the owner rather than
assuming.

## Commits

Commit only on an approved story branch as part of the `AGENTS.md` workflow, or
when the owner explicitly asks for a commit. The approved workflow proceeds
through commit without a second approval immediately before it.

Do not add `Co-Authored-By` or any other AI attribution trailer
(`AGENTS.md` §12).
