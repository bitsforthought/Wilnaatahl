# Domain Docs

## Before exploring

This repository uses a single-context layout: `GLOSSARY.md` at the root and ADRs
under `docs/adr/`.

Read the glossary and ADRs relevant to the area you are exploring. If a root
`GLOSSARY-MAP.md` exists later, follow it to the relevant context glossaries and
also read their ADRs under `src/<context>/docs/adr/`.

If these documents are absent, proceed silently. The `/domain-modeling` skill
creates them lazily when terms or decisions are resolved; setup does not create
placeholders.

## Vocabulary

Use glossary terms when naming domain concepts in issues, proposals, hypotheses,
and tests. If a needed concept is missing, reconsider whether it belongs to this
domain or note the gap for `/domain-modeling`.

## ADR conflicts

Surface a conflict with an existing ADR explicitly, naming the ADR and why its
decision should be reconsidered, rather than silently overriding it.
