# Scan measurement entry

The benchmark executable is separate from the product CLI. It calls the accepted
query service and fully consumes its snapshot through the real cache repository:

```text
dotnet Next.Benchmarks.dll measure-scan WORLD CACHE full|reuse WORKERS [all|language.value]
```

Use the built executable under `benchmarks/Next.Benchmarks/bin/Release/net10.0`.
Paths are single arguments. The world must exist; the owned cache must be outside
it. Invalid mode, scope or worker count (1–8) is rejected before creating cache
state. Reparse, ownership, source hashes, CAS, parser and storage limits remain the
underlying service's checks. This entry never writes the source world.

`full` forces parsing; `reuse` permits the normal verified warm/incremental path.
The returned `before` and query telemetry tell whether it was actually fresh,
dirty, missing or rebuilt. Calling an empty cache `reuse` does not establish a warm
sample. Optional scope is `all` by default; `language.value` matches the G06 LangKey
control. Each comparison must use the same full source set, scope and workload.

Stdout is one UTF-8 `nbtcli.next.benchmark`, schemaVersion 1 envelope, with the actual
configuration and sample under `data`. A complete measured source workload exits
0. An incomplete workload exits 4 with its sample, counts, diagnostic histogram
and hashes intact. Invalid arguments exit 2; failed measurement exits 5 and
cancellation exits 130. No measurement error is a successful zero-work sample.
This contract is distinct from the product CLI's outcome and receipt protocol.

Ctrl+C during `measure-scan` requests cancellation and returns the structured
`CANCELLED` response after the current work observes the request. A cache snapshot
already published before cancellation remains available to a later run.

`ScanMeasurement.RunAsync` is also available to the benchmark harness. Invalid
execution limits and precancellation refuse before any inventory/cache callback.
Source cancellation can throw without returning a completed sample; callers retain
the error rather than substituting a partial success.

The sample separates correctness from resource observations:

| Field | Meaning |
| --- | --- |
| `semanticHash` | Hash of the snapshot input/policy digest, full canonical cache shard hashes, per-shard counts/completeness and catalog diagnostic. Shards bind all values, exact addresses and diagnostics. |
| `occurrenceHash` | SHA-256 of every ordered 32-byte OccurrenceId digest while consuming all sources, without retaining a world-wide text list. |
| `coverageComplete` | Complete source discovery/extraction, with no failed, unsupported or cancelled source. It grants no world reference-synchronization authority. |
| `counts` | Sources, occurrences, translation keys, command references, failed/unsupported/cancelled sources, incomplete text and unresolved command-reference source counts. |
| `diagnostics` | Source diagnostic/outcome histogram; `catalog:` entries preserve root catalog failures even if all returned payloads are readable. Fine-grained source details remain in the verified cache shards. |
| `query` / `execution` | Existing parse/reuse, queue, worker and reserved-buffer telemetry; execution can be null on a fresh cache hit. |

Hashes bind the selected physical world identity. Compare workers/cache modes on
the same preserved root. Do not compare different roots by deleting identities;
use an explicitly defined portable projection when that is the intended experiment.
`referenceCoverage=not-assessed` remains explicit in the process configuration.

Resource fields describe their actual observation boundary:

| Cost | Observation |
| --- | --- |
| `wallMilliseconds`, `cpuMilliseconds` | Query construction/execution, complete snapshot consumption and semantic hashing. Repository construction and process startup precede this phase; this is not total CLI latency. |
| `managedAllocatedBytes` | Process-wide cumulative managed allocation delta across all worker threads for that phase. It is not live memory or RSS. |
| `peakWorkingSetBytes` | Windows process lifetime peak working set, including startup and earlier activity. It cannot be reset per phase. Use a fresh process for each independent RSS sample; repeated in-process unit calls cannot establish independent phase peaks. |
| `handlesBefore`, `handlesAfter` | Current process handles around the phase; both include the measurement process handle. These two points alone do not prove sustained convergence. |
| `sourceReads`, `sourceReadBytes` | Successful inventory payload-return calls and bytes, including repeated freshness/hash checks. They exclude failed calls and OS/cache-internal activity. |
| `uniqueSourceBytes` | First returned payload length for each distinct physical relative path, counted once. |
| `cacheReadBytes` | Returned manifest/shard payload bytes at the cache port. |
| `cacheSubmittedBytes` | Payload bytes successfully submitted to shard/head APIs. An idempotent store can avoid physical writes; this is not device write volume. |

Counters use the existing inventory/cache ports, forward identity and CAS arguments,
and include measurement overhead. Data/semantic verification remains inside the
measured phase. Keep this instrumentation and validation work identical between
control and candidate. Buffer reservations are separate from process working set.

The `benchmark` verification suite calibrates full/warm/worker equality, real source
and cache traffic, incremental reparsing, malformed payloads, root catalog failures,
precancellation, invalid configuration and the actual executable's stdout/exit
contract. It is tool validation, not a performance sample matrix.

The [measured results and operating limits](performance-results.md) describe the
frozen environment, complete scenario comparisons, workflow timing, capacity,
cancellation and sustained-operation observations. Interpret measurements within
their stated intervals and workload sizes. Minecraft behavior, power-loss
durability and release installation have separate acceptance requirements.
The G06 correctness reference remains [control.json](../benchmarks/control.json).
