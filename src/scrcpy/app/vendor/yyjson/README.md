# yyjson source for Phase 6A native protocol tests

Source: https://github.com/ibireme/yyjson

Pin: release 0.12.0, commit
8b4a38dc994a110abaec8a400615567bd996105f.

Imported without modifications:

| File | SHA-256 |
| --- | --- |
| yyjson.c | ac2e9bbb2e2d9149d90878d40506a1d624fa0b33c979a11b61075c54782c6d6a |
| yyjson.h | 175867c5493a5df648cec566717fa1c29aa2f6096f5f0cf1efad0b65e1f6d7b3 |
| LICENSE | 45e384d3d52c73cba3a64d6e6c25d47cd738cd8a55c30629e3201046eda62947 |

License: MIT, copyright YaoYuan, with the complete upstream notice in
LICENSE. The library is a maintained, compact C parser/writer with strict
UTF-8 defaults, explicit parse errors, a maximum-memory estimate and caller
allocator. Phase 6A compiles it only into an isolated Meson test target; the
current native executable and packages do not yet include it. Phase 6B must
review the shipped binary/notice boundary when connecting the runtime.

The protocol module rejects payloads over 1,048,576 bytes before parsing.
With read flags 0, yyjson's pool estimate is `13 * payload_bytes + 256`,
at most 13,631,744 bytes at the maximum payload, below the separate 32 MiB
pool cap. It frees the document and pool on every path. The vendored reader
has no `YYJSON_READER_DEPTH_LIMIT` definition or configured depth cutoff:
the protocol module rejects disallowed nesting during message-shape validation
after bounded parsing. No permissive JSON flags are enabled.
