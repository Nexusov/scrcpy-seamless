# Test agent guide

Scope: `tests/`.

Read:

- `/AGENTS.md`
- `/docs/development/debugging.md`

Tests should protect observable behavior and architectural invariants rather
than mirror implementation details.

## Regression bugs

For a non-trivial bug, prefer this sequence:

1. reproduce;
2. add a regression test that fails for the expected reason;
3. verify the failure;
4. implement the root-cause fix;
5. verify the regression test and neighboring tests;
6. run broader validation appropriate to the bug class.

If hardware is required, create the strongest deterministic synthetic coverage
possible and add/maintain the hardware scenario separately.

## Determinism

Do not make tests "stable" by:

- large arbitrary sleeps;
- blind whole-test retries;
- weakened assertions;
- swallowing exceptions;
- ignoring sanitizer/compiler failures.

Use explicit synchronization, fake clocks, controllable adapters, fixtures,
and bounded semantic waits.

A flaky test is a defect that requires investigation.

## Test integrity

Do not:

- parse source text to extract functions under test;
- reimplement production algorithms inside tests and then test the copy;
- silently skip failing tests because they became inconvenient.

Prefer public/internal test seams and explicit injected dependencies.

## Privacy

Fixtures must not contain real pairing codes, ADB private keys, personal device
identifiers, or private network information.

Use synthetic identifiers and redacted samples.
