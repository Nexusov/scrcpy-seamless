# Phase 4 migration characterization

These frozen fixtures were extracted once from the accepted Phase 3 merge
`3207892ddd37cdd6b20bafc7c770a8470b6cb81f`, before SpecGen and
`spec/options/options.yaml` existed. They are independent migration evidence,
not inputs to SpecGen or another source of truth for future option development.

- `scrcpy-v4.0-cli-baseline.json` records all 109 rows of
  `src/scrcpy/app/src/cli.c`: ordered long/short spellings, required/optional/no
  argument and hint, and release/debug help text. Adjacent C string literals
  were joined, and the two port-range macros became named placeholders.
- `native-cli-inventory-baseline.inc` records the compact compiled-table
  projection of the same old 109 rows. `test_cli.c` compares each compiled row
  against it. The rendered-help FNV-1a fingerprint in that test came from the
  accepted `ea193f2` DEV binary; `cli.c` did not change between `ea193f2` and
  the Phase 3 merge. The executable path and version prelude were removed and
  line endings were normalized before computing the fingerprint. The full
  native help text remains readable in the JSON fixture.
- `legacy-option-catalog-baseline.json` is a verbatim copy of
  `launcher/option-catalog.json` at that merge: 106 ordered entries with all
  13 fields and values. The SpecGen test compares every field semantically.

When Phase 9 intentionally changes upstream options, keep these Phase 3
fixtures intact. Review the explicit behavior delta and update the parity
assertions or add a new versioned fixture for the new accepted baseline. Do not
regenerate these files from the current specification or silently replace the
old characterization data.
