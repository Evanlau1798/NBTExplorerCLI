# Measured performance and operating limits

These measurements use synthetic corpora on Windows x64, NTFS, an Intel
Core i5-13400F, 34,163,642,368 bytes of RAM, SDK 10.0.404 and .NET 10.0.15,
with Release builds. The workspace volume's physical medium mapping was
unverified. Windows file-cache state was uncontrolled. Keep these conditions
with any interpretation of the results.

The correct G06 implementation supplies the control. Scan measurements use the
accepted native-candidate streaming build from `f858f75`. Public session workflows
use `755db99`. Public pack measurements include the same normal writer path and
the earlier lock-proof closure, before the replacement-proof retention fix.
That later fix changes recovery of a mismatched candidate identity.

## Scanning

The scan freeze has SHA-256
`0abf7351cd776b4c0abc81ffad0cca51628d123b5819835fc70dd976155d0783`.
It selects `language.value`, workers 1, 2 and 8, a total queue capacity of 16,
and 256 MiB of reserved in-flight buffers. Each observation uses a fresh process.
S and M have two warmups and five formal samples per mode/worker combination.
L has one warmup and three formal samples. All warmup and formal receipts remain
in the acceptance evidence.

| Corpus | Semantic sources | Slots | Input bytes | Frozen process budget |
| --- | ---: | ---: | ---: | ---: |
| S | 7 | 256 | 34,032 | 256 MiB |
| M | 291 | 32,768 | 2,246,902 | 512 MiB |
| L | 579 | 524,288 | 35,672,438 | 1 GiB |

The following medians use workers=2. The measured interval includes query
execution, complete snapshot consumption and semantic hashing. Process startup
and repository construction precede that interval.

| Corpus | Full milliseconds | Verified warm milliseconds | One-file incremental milliseconds |
| --- | ---: | ---: | ---: |
| S | 543.016 | 409.239 | 534.080 |
| M | 9,710.473 | 5,470.080 | 6,531.960 |
| L | 54,414.945 | 34,955.937 | 42,355.205 |

Observed maxima from the same formal samples, also with workers=2, are below.
Process high-water is the maximum across the three modes for that corpus/worker.

| Corpus | Full maximum ms | Warm maximum ms | Incremental maximum ms | Process high-water bytes |
| --- | ---: | ---: | ---: | ---: |
| S | 556.719 | 422.847 | 561.356 | 65,781,760 |
| M | 9,748.996 | 5,535.998 | 6,642.205 | 98,635,776 |
| L | 57,910.356 | 35,276.226 | 42,489.693 | 211,660,800 |

All 88 S, 88 M and 52 L paired receipts preserve complete semantic and occurrence
hashes, source diagnostics, coverage and the expected parse/reuse workload on the
same physical root. The observed medians satisfy the frozen allocation-per-byte
and allocation-per-slot regression budgets. Every worker/mode group stays within
its process, queue and buffer budget. Verified warm queries still hash source
content and decode cached shards. The M warm sample returns 4,497,644 source
payload bytes through the inventory port. Payload bytes describe interface work.

A separate native sixteen-slot control carries workers 1, 2 and 8 through full
query, canonical plan, staging, apply, receipt replay and rollback on one physical
world. Occurrence/slot IDs, plan bytes, staged hashes and durable receipts agree
across all three executions. Each rollback restores the complete original file
set, including raw hashes of unselected data.

A paused-store control reaches a total queued-artifact count of 16. A separate
native control uses eight workers and pauses the first function store callback.
With a 2 MiB in-flight budget, one function retains the only fitting reservation
until the callback is released. With 16 MiB, all three function reads proceed
while that same consumer stays paused. Both runs finish after release within
their queue/buffer budgets, with identical source outcomes, occurrence values
and original file hashes.

## Public workflows

Each public CLI command starts a fresh process. Session workflow time includes
backup, start, export, submit, get, plan, apply, replay, seal, rollback, reply
construction and independent applied/rollback byte verification. Each row has
two warmups and five formal samples on the same preserved world root.

| Session slots | G06 workflow median ms | Candidate workflow median ms | G06 workflow maximum ms | Candidate workflow maximum ms | Candidate observed process high-water bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| 16 | 9,587.455 | 7,106.075 | 18,935.593 | 7,127.950 | 66,727,936 |
| 256 | 17,434.288 | 13,916.662 | 18,252.951 | 14,219.247 | 130,330,624 |
| 2,048 | Incomplete at the original seal memory budget | 46,266.082 | Undefined | 46,558.300 | 461,963,264 |

The 16/256-slot comparisons preserve canonical plans, durable apply/replay/rollback
receipts and final session coverage. The candidate completes all 21 workflows.
The original 2,048-slot seal exceeded 512 MiB. Its complete-workflow timing ratio
therefore remains undefined. The configured 100,000-slot schema limit requires
separate capacity evidence for workloads beyond the measured session sizes.

Public pack generation uses a complete 2,048-slot session and 64 opaque merge
members plus the frozen `pack.mcmeta`. The merge-member hash oracle checks all
65 members, alongside both generated language files. Serial AB/BA order uses
two warmups and five formal samples for each implementation and format.

| Format | G06 apply median ms | Candidate apply median ms | Candidate observed process high-water bytes |
| --- | ---: | ---: | ---: |
| ZIP | 3,801.435 | 3,769.325 | 160,649,216 |
| Directory | 8,002.963 | 8,295.039 | 164,999,168 |

The complete verified pack workflow includes apply, independent member/value
verification, rollback and source-byte verification. Its formal observations are
below, with the same AB/BA samples as the apply table.

| Format | G06 workflow median ms | Candidate workflow median ms | G06 workflow maximum ms | Candidate workflow maximum ms |
| --- | ---: | ---: | ---: | ---: |
| ZIP | 4,481.611 | 4,488.911 | 4,620.499 | 4,614.436 |
| Directory | 12,447.314 | 12,756.558 | 12,540.924 | 26,697.596 |

Both formats satisfy the original 1.10 apply-time ratio and 512 MiB budget.
Their same-root plans, receipts, language values, merge bytes and exact rollback
agree. The slower directory sample and every other sample remain in the record.
CLI high-water sampling observes a live process, with a remaining unobserved
interval before exit. Different measurement periods and OS cache conditions limit
causal claims about speedups.

## Diagnosed repeated work

For 128 function files with 2,048 command slots, batching source validation reduces
Bind inventory reads from 2,822 to 902. Eight file/slot/translation-cardinality
diagnostic cases preserve their complete canonical plans.

Those earlier inventory diagnostics record enumeration, scope, read calls and
returned payload bytes. They leave per-artifact staging parse, chunk-load,
candidate-comparison and decoded read/write counts unobserved. The additional
internal observations below use a separately identified candidate and interval.

With a 4 MiB retained resource dependency and nine outputs, streamed image
validation reduces cumulative managed allocation from 86,545,248 to 40,095,128
bytes. Required scope, catalog, preimage and readback checks remain in the path.
A native 16 MiB Stage/Flush/Verify/Commit diagnostic reduces its measured
same-thread allocation from 67,135,960 to 31,184 bytes. These intervals measure
cumulative allocation. Their results describe the particular inputs and phases.

The six public region-edit scenarios preserve all 42 paired plans and exact
postimages. The initial comparison across different measurement periods exceeds
the 1.10 latency ratio in four groups. A separately frozen, same-root AB/BA check
retains those adverse samples and adds 56 executions for the four groups. Its
candidate/control median ratios are 0.966, 0.986, 0.942 and 0.971. The variation
limits attribution of the original timing difference to the implementation.

## Internal preparation work

Optional `nbtcli.next.work` activities separate discovery, binding, construction,
in-memory projection and persisted verification. Observer-on/off calibration keeps
canonical plan bytes, every candidate hash and source preimages equal. A typed-only
three-chunk/two-target witness originally loads five chunks during Bind; validating
fresh source descriptors directly reduces this to the two selected chunks, retaining
typed/profile checks and final preimage checks. Literal/key/reference binding keeps
its semantic extraction. This is a bounded removal of unrelated text work.

Six frozen raw-codec region cases independently vary artifact size and target
count. Each has one discovery index parse, one Bind index parse and one staging
index parse. Bind and staging each load exactly the selected chunk count. Each
candidate readback reparses the index and checks all 1,024 descriptors:

| Input chunks | Selected chunks | Staging decoded NBT read bytes | Staging serialized NBT bytes | Persisted payload comparison bytes | Physical candidate bytes written |
| --- | --- | --- | --- | --- | --- |
| 64 | 1 | 36 | 36 | 4,572 | 274,432 |
| 64 | 16 | 576 | 576 | 4,032 | 335,872 |
| 64 | 64 | 2,304 | 2,304 | 2,304 | 532,480 |
| 512 | 1 | 36 | 36 | 36,828 | 2,109,440 |
| 512 | 16 | 576 | 576 | 36,288 | 2,170,880 |
| 512 | 64 | 2,304 | 2,304 | 34,560 | 2,367,488 |

Payload comparison reads both original and candidate unselected payloads; selected
payloads compare against the verified image. This explains the different scaling
of necessary readback and selected document decoding. All original sector bytes
remain intact, and every selected `Counter` has its independently expected value.
Physical candidate size still includes the retained region and appended sectors.
These byte counts describe codec/port work, not device traffic or RSS.

Eight function cases independently vary physical files (16/128), slots per file
(1/16) and distinct translated values (1/16). The following counts hold separately
for both translation cardinalities. Indexed Bind lookups and exact JSON target
checks follow slot count, while full staging command parses follow file count:

| Function files | Slots | Bind occurrence checks | Staging command parses | Staging JSON target checks | Projection / persisted command parses | Persisted candidate image checks |
| --- | --- | --- | --- | --- | --- | --- |
| 16 | 16 | 16 | 16 | 16 | 32 / 32 | 32 |
| 16 | 256 | 256 | 16 | 256 | 32 / 32 | 32 |
| 128 | 128 | 128 | 128 | 128 | 256 / 256 | 256 |
| 128 | 2,048 | 2,048 | 128 | 2,048 | 256 / 256 | 256 |

Nested JSON validation/span passes are recorded separately; a command-parse count
is not a claim that every JSON fragment is parsed only once. Projection and
persisted checks remain intact. These are complete observer-on diagnostics with a
100,000-key aggregation cap, and do not enter observer-disabled timing denominators.

For these fourteen cases, observer-disabled before/after capsules use identical
runner code, input roots, query/plan semantics and candidate oracles. Each side has
two warmup and five formal fresh-process samples, in serial AB/BA order. The
interval is synchronous `Prepare`, including Bind, staging and persisted readback;
query, the initial plan Build, process startup and the external oracle are outside
it. OS file-cache state is uncontrolled. Formal median time ratios span
0.9041–1.0418 and same-thread cumulative allocation ratios span 0.8207–1.0004,
within the predeclared 1.10 thresholds. These compare the typed-Bind improvement
and disabled observation together; they do not isolate a universal tracing cost,
claim a full-workflow speedup or establish peak RSS.

The labels below encode region chunks/targets or function files/slots-per-file/
translation kinds. Times are the five formal samples' median / observed maximum,
in milliseconds. Allocation ratios compare formal medians, not live memory:

| Case | Before Prepare ms | After Prepare ms | Allocation ratio |
| --- | --- | --- | --- |
| region-64-1 | 70.335 / 72.530 | 69.910 / 71.687 | 0.8384 |
| region-64-16 | 75.136 / 77.543 | 71.867 / 73.538 | 0.8431 |
| region-64-64 | 79.103 / 84.876 | 77.365 / 80.143 | 0.9002 |
| region-512-1 | 97.085 / 108.113 | 90.393 / 91.383 | 0.8207 |
| region-512-16 | 96.178 / 101.401 | 88.505 / 93.473 | 0.8268 |
| region-512-64 | 102.157 / 105.740 | 92.356 / 92.786 | 0.8381 |
| function-16-1-1 | 218.567 / 225.826 | 215.375 / 222.310 | 1.0003 |
| function-16-1-16 | 224.904 / 288.643 | 209.612 / 222.212 | 0.9968 |
| function-16-16-1 | 321.188 / 336.190 | 326.062 / 360.366 | 1.0004 |
| function-16-16-16 | 329.127 / 340.132 | 342.867 / 352.037 | 1.0000 |
| function-128-1-1 | 1,273.373 / 1,277.672 | 1,260.603 / 1,276.263 | 1.0003 |
| function-128-1-16 | 1,260.678 / 1,348.201 | 1,251.603 / 1,397.472 | 1.0003 |
| function-128-16-1 | 1,774.732 / 11,239.317 | 1,689.411 / 1,866.690 | 0.9996 |
| function-128-16-16 | 1,727.003 / 1,761.920 | 1,700.668 / 1,753.490 | 0.9994 |

The 11,239.317 ms before sample is retained. No percentile or general speedup is
inferred from these five-sample distributions.

Eight resource diagnostics reuse the frozen language/ZIP/directory scenarios.
An outer harness interval separates Build, Prepare and Apply while retaining each
activity's own phase. The following rows cover Prepare only and the output world's
GUID, excluding internal backup/journal namespace writes. All original/merge
members, including metadata, and every generated value are checked; rollback
restores the complete original file set. Each declared physical candidate has one
write and two persisted image comparisons during Prepare:

| Case | Physical outputs | Language-key checks | Candidate bytes written | Persisted image comparisons | Existing-output inventory payload bytes |
| --- | --- | --- | --- | --- | --- |
| lang-small | 1 | 1 | 26 | 2 | 0 |
| lang-many | 64 | 64 | 1,664 | 128 | 0 |
| zip-merge | 1 | 8 | 26,099 | 2 | 0 |
| directory-base | 9 | 8 | 267 | 18 | 6,321 |
| directory-outputs | 65 | 64 | 1,723 | 130 | 6,321 |
| directory-retained | 9 | 8 | 267 | 18 | 49,329 |
| directory-bytes | 9 | 8 | 267 | 18 | 12,583,089 |
| directory-merge | 73 | 8 | 16,651 | 146 | 6,321 |

Both merge cases load 16,443 frozen member bytes during rendering, counted
separately from inventory traffic and serialized ZIP bytes. A zero inventory
payload count still includes absence checks and says nothing about immutable
object-store traffic or device I/O. This diagnostic retains the original inventory
wrapper's array-based image fallback identically on both sides. Its allocation
comparisons do not measure the native inventory's streamed-image allocation gains.

Their observer-disabled paired samples use the same two-warmup/five-formal AB/BA
policy, full output/member hashes and exact rollback. Only the Prepare phase enters
this additional cost comparison. Its time median ratios span 0.9789–1.0412 and
allocation median ratios span 1.0003–1.0304, within the frozen 1.10 limits:

| Case | Before Prepare median / maximum ms | After Prepare median / maximum ms | Allocation ratio |
| --- | --- | --- | --- |
| lang-small | 62.574 / 66.779 | 63.343 / 64.845 | 1.0277 |
| lang-many | 409.776 / 413.934 | 406.297 / 439.523 | 1.0078 |
| zip-merge | 95.762 / 97.727 | 97.297 / 100.926 | 1.0170 |
| directory-base | 125.866 / 146.412 | 131.043 / 134.153 | 1.0303 |
| directory-outputs | 461.428 / 528.229 | 454.820 / 473.499 | 1.0016 |
| directory-retained | 203.285 / 209.754 | 207.740 / 220.142 | 1.0003 |
| directory-bytes | 222.517 / 286.991 | 217.812 / 223.451 | 1.0024 |
| directory-merge | 519.907 / 577.568 | 519.508 / 529.172 | 1.0010 |

A final trace-context fix tags validation of an existing output ZIP during Bind.
These eight scenarios have no existing output ZIP, so that branch is outside this
matrix; its observer-on/off candidate equality and physical identity have a separate
focused verification. That functional witness establishes no formal latency result
for existing-ZIP input.

## Capacity and recovery boundaries

A 63 MiB region with one typed edit originally observed 1,518,764,032 bytes during
apply, exceeding its 1 GiB budget. Native streamed candidate verification observed
853,544,960 bytes while preserving the exact plan, selected edit, unselected bytes
and raw rollback image. The later owned-cleanup run observed 853,573,632 bytes for
apply and 240,832,512 bytes for rollback on the same input/root.

The mixed capacity corpus preserves 4,230 slots from 32 ZIP packs and a large
book. An over-budget compressed ZIP returns explicit `LimitExceeded` coverage.
Native Ctrl+C witnesses on the L corpus cover workers 1, 2 and 8, with exit 130,
no published manifest and a maximum observed convergence of 474.304 ms against
the frozen five-second limit.

## Sustained operation

One scanner process completes 236 full/reuse observations in 900,912.6503 ms on
the M corpus. Its lifetime high-water is 104,914,944 bytes. Handles rise from 278
after warmup to an observed maximum of 300 against the frozen growth allowance
of 128. Cache storage stays at 294 files and 33,657,415 bytes, with zero candidate
files. The apply fault suite runs concurrently after two minutes and exits 0.
A brief independent read-only directory enumeration contributes possible metadata
I/O during this run.

A separate writer process completes 1,163 distinct-plan cycles in 900,230.1653 ms.
Each cycle generates two 16-entry language files, validates every value, replays
the exact durable receipt and rolls back to expected absence. Every sixteenth
cycle blocks the initial candidate verification with a native sharing lock and
checks release/retry collection. Registered inactive candidates converge to zero
after each cycle, while the fixed unknown candidate stays byte-identical.

Writer lifetime high-water is 101,015,552 bytes. Post-warmup handles rise from 275
to a maximum of 286. Retained state ends at 19,776 files and 21,210,458 bytes,
within the predeclared 256 MiB scenario budget. The 18 immutable objects captured
after the first cycle remain byte-identical at the end. The growing retained
state includes required journal, receipt and backup evidence. This workload
establishes the measured fifteen-minute operating range.

Candidate reclamation requires an owned proof, physical root binding, exact NTFS
identity and image hash, and an exclusive writer lease. Immutable journal, backup
and receipt history is retained. Storage planning must include that history and
unregistered candidates. Process-kill tests cover the documented NTFS recovery
model. Minecraft loading, power-loss durability and release installation have
their own acceptance requirements.

See [measurement semantics](benchmark-measurement-contract.md),
[budgets](../benchmarks/budgets.json) and [recovery](apply-recovery.md).
