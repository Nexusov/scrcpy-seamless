# Documentation agent guide

Scope: `docs/`.

Documentation is part of the product and part of the agent harness.

Read `/AGENTS.md`.

## Language and authority

Canonical developer/repository documentation is English.

Keep `AGENTS.md` files short and navigational. Detailed knowledge belongs in
structured `docs/` files.

## Documentation voice

Describe architecture and behavior with concrete technical subjects, decisions
with their reasons, and validation with its actual evidence and limits. Address
contributors directly for instructions. Keep progress records factual; avoid
retelling maintainer–assistant conversations or replacing an approver with an
ambiguous "I" or "we". Preserve material approval roles, attribution, and the
distinction between automated tests, manual observations, reported symptoms,
and unresolved hypotheses.

Use "we" only when it clearly states an established project decision; do not
invent a team or consensus. Prefer active voice without banning useful passive
constructions.

Historical approvals are evidence, not standing permission. Obtain explicit
authorization from the requesting maintainer before a push, merge, release-tag
creation, release publication, destructive Git operation, repository-settings
change, or licensing change.

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
The pinned SpecGen `verify` command checks canonical option-spec semantics and
generated native, Core, legacy, resource and reference-document bytes. It runs
separately from DocsCheck; package/config consistency is not DocsCheck coverage.

When documentation paths are moved, update AGENTS references in the same
change.
