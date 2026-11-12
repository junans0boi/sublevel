# Domain Docs

How the engineering skills should consume this repo's domain documentation. This repo is **single-context**, but the layout differs from the skill defaults (see CLAUDE.md "읽는 순서").

## Before exploring, read these

- **`docs/GLOSSARY.md`**: domain vocabulary (not a root `GLOSSARY.md`).
- **`docs/DECISIONS.md`**: the ADR log (a single table of `ADR-NNN` rows plus an agent-notes section), not `docs/adr/`.
- **`docs/SPEC.md`**: the single source of truth for game rules. If it conflicts with anything else, SPEC wins; changing it requires an ADR first.

If any of these files don't exist, proceed silently.

## Where new things go

- New terms: append a row to `docs/GLOSSARY.md`.
- New decisions / assumptions: append an `ADR-NNN` row to `docs/DECISIONS.md` (status `확정` or `가정`). Do not create `docs/adr/`.
- Specs: GitHub issue (via `to-spec`) and, if durable, a section in `docs/`.

## Use the glossary's vocabulary

When your output names a domain concept (issue title, test name, ticket), use the term as defined in `docs/GLOSSARY.md`.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding, e.g. "Contradicts ADR-006 (radiation damage instead of instant death), but worth reopening because…".
