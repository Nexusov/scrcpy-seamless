# Documentation agent guide

Scope: `docs/`.

Documentation is part of the product and part of the agent harness.

Read `/AGENTS.md`.

## Language and authority

Canonical developer/repository documentation is English.

Keep `AGENTS.md` files short and navigational. Detailed knowledge belongs in
structured `docs/` files.

Do not duplicate volatile facts in prose when they can come from a canonical
manifest/spec and be generated or mechanically checked.

## Documentation change rules

When code changes:

- architecture/ownership -> update architecture docs/ADR;
- public behavior -> update user docs/README;
- build/test commands -> update development docs and applicable AGENTS;
- protocol/spec -> update canonical spec and protocol docs;
- release/package layout -> update release/package docs;
- third-party reuse -> update provenance/acknowledgements.

Do not leave future-tense docs claiming behavior that is not implemented.

Clearly mark plans/research as plans/research.

## ADRs

Use an ADR for durable decisions with meaningful tradeoffs.

An ADR should record:

- context;
- decision;
- alternatives considered;
- consequences;
- supersession relationship if replaced.

Do not create an ADR for trivial implementation detail.

## Research documents

For external projects record exact repository URL and inspected commit/tag.

For code/assets actually reused, record exact source path, license, copyright,
destination, and modifications. Do not infer file-level licensing solely from
a repository title.

## Links and checks

Use relative links for in-repository navigation where practical.

Run the initial DocsCheck from the repository root for documentation changes:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\docs-check.ps1
```

It checks local links and Markdown heading anchors in tracked Markdown files.
Package/config/spec consistency checks remain future extensions, not current
DocsCheck coverage.

When documentation paths are moved, update AGENTS references in the same
change.
