# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

Layout: single-context. All agent-managed docs live in `agent-docs/`, not at the repo root or in `docs/`
(`docs/` holds the project's own architecture and protocol documentation).

## Before exploring, read these

- **`agent-docs/GLOSSARY.md`**: the domain vocabulary.
- **`agent-docs/adr/`**: read ADRs that touch the area you're about to work in.

If either doesn't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved, in `agent-docs/`.

For how the system works today, read `docs/reference/joinfs-architecture.md`; the protocol decision records live in `docs/` (see `CLAUDE.md`). Those are not ADRs in this sense, but do not contradict them silently either.

## File structure

```
/
├── agent-docs/
│   ├── GLOSSARY.md
│   ├── adr/
│   │   └── 0001-<slug>.md
│   └── features/<feature>/...   ← issues and specs, see issue-tracker.md
├── docs/                         ← project documentation (architecture, protocols)
└── JoinFS/ ...
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `GLOSSARY.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders), but worth reopening because…_
