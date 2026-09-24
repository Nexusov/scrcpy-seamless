# Third-party research and reuse policy

This policy applies when studying external projects for Seamless 2.0. The
existing packaged dependency inventory and notices remain in
[THIRD_PARTY.md](../../THIRD_PARTY.md) and [licenses](../../licenses/).
Research does not change those inventories by itself.

## Classify the intended use

1. **Inspiration:** observe a problem or UX pattern without copying protected
   expression, code, or assets. Record the source and pinned revision in the
   [UI audit](UI_REFERENCE_AUDIT.md).
2. **Independent reimplementation:** implement the desired behavior from
   independently defined requirements. Document the behavior and tests, and
   preserve evidence that no external code or assets were copied.
3. **Copied or adapted material:** require a file-level provenance and license
   review before import. Approval of a repository-level license alone is not
   enough.

## Required record for copied or adapted material

Before importing each item, record:

| Field | Required evidence |
| --- | --- |
| Source | Exact repository URL and inspected commit SHA or immutable tag |
| Original material | Source path and, for assets, identification of the actual file |
| Rights | File and repository license evidence; copyright holder/author; any conflicting notices |
| Destination | Repository path where the material will be placed |
| Changes | What was copied, modified, or translated |
| Obligations | License/NOTICE text, attribution, source or distribution obligations, and any compatibility decision |
| Verification | Reviewer and evidence that the shipped package includes required notices |

Update [THIRD_PARTY.md](../../THIRD_PARTY.md), the appropriate files in
[licenses](../../licenses/), and add a clear acknowledgement to the
[repository README](../../README.md) when material is actually reused.
Preserve upstream notices in copied files.

Do not copy GPL/AGPL implementations into the Apache-2.0 project without an
explicit owner licensing decision. Do not copy proprietary or closed runtime
components to match a competitor. If rights are unclear, defer the import and
record the uncertainty; an independent implementation may still be considered.
