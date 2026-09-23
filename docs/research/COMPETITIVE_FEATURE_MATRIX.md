# Competitive feature matrix

Status: Phase 1 scaffold. No competitor feature has been verified or selected
for implementation here. Complete this matrix from pinned evidence during the
competitive audit; do not fill unknown cells by inference.

## Decision vocabulary

- **ADOPT**: implement a feature that fits Seamless's scope.
- **ADAPT**: use the problem or UX insight while designing an independent fit.
- **ALREADY_BETTER**: retain current Seamless behavior with cited evidence.
- **DEFER**: record a potentially useful feature for a later phase/version.
- **REJECT**: explicitly keep the feature out of product scope.

## Feature records

Add one row per meaningful feature and source revision. Use a link to a pinned
source file, release, or documented screen where available. State the current
Seamless behavior with a source or [baseline](../architecture/SEAMLESS_1_BASELINE.md)
reference, then give a decision and rationale. `Unknown` is preferable to an
unsupported claim.

| Feature | Project and inspected revision | UX evidence | Implementation evidence | License constraints | Current Seamless support | Decision | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- |

Preserve guided setup, USB/Wi-Fi/combined mode, automatic failover, native
process/window continuity, retained last frame, explicit reconnect state, and
video/audio/control recovery as [target requirements](../architecture/SEAMLESS_2_TARGET.md).
Do not implement the union of competitor checkboxes or expand into a generic
Android device-management product. Copying code or assets requires the separate
[reuse review](THIRD_PARTY_REUSE.md).
