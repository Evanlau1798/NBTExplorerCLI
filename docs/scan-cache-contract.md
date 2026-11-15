# Scan and cache contract

The scan coordinator uses bounded physical artifact work groups. Region chunks and ZIP
entries keep their separate semantic source results; they share one container read.
One store callback owns output, while workers perform independent reads and extraction.

## Execution limits

Defaults are 2 workers, a maximum of 8, 16 queued artifact groups and a 256 MiB shared
buffer reservation budget. For each group, a worker reserves the catalogued raw bytes
plus the configured maximum decoded bytes before reading it. Completed groups retain
that reservation until the store callback finishes. This conservative reservation can
reduce active concurrency when a group's maximum decode size is large.

The reservation is a buffer policy, not an RSS measurement. Per-group retained extracted
text/address records are also bounded by the configured `MaxDecodedBytes` (128 MiB
default). A group exceeding either reservation capacity or retention capacity yields
explicit `Failed/LimitExceeded` source results. Ordinary capacity contention waits;
it does not reject a source merely because another worker holds the capacity.

Queue and active-worker peaks are observed execution telemetry. Artifact and expanded
source counts remain separate. Worker count and completion order are not occurrence IDs.
Within a group the existing decoder/profile/scope rules remain authoritative.
Workers claim the next catalog group directly; one bounded result queue holds up to
16 completed groups in total. There is no additional input queue consuming another
16 slots. The active workers and store callback retain their buffer reservations.

Cancellation stops scheduling and storage, drains workers and releases reservations.
The result partitions the discovered catalog into stored artifact groups and explicit
cancelled/unprocessed sources. A store failure propagates after worker cleanup. The
original world is read only. An incomplete run must not publish a new cache manifest.

## One verified query contract

`WorldQueryService` accepts the same `WorldQueryRequest` for scan, extract, session
and convert. Its opaque `WorldQuerySnapshot` reads validated shard results rebound
to the current discovered catalog. Deserializing cache JSON does not create this
snapshot or authorize world writes. Public CLI wiring belongs to G06; G05 must
independently validate write support, preimages and reference completeness.

Every result retains its semantic outcome, diagnostics, exact address, text and
separate name references. `CoverageComplete` concerns the selected extraction
scope, not global reference synchronization. A Fresh cache may faithfully describe
failed or unsupported sources and have incomplete semantic coverage.

`Fresh`, `Dirty(reason)`, `Missing`, `Corrupt` and `UnsupportedSchema` are distinct.
Known codec/profile/rules/policy changes are Dirty; unknown manifest or shard
serialization is UnsupportedSchema. The manifest binds the world, normalized
scope, raw artifact hashes, source-set and interpretation versions. Parse keys
also bind layout, source locations/chunks, limits, explicit storage rules and
pack metadata, including dependencies outside an exact artifact filter.
Internal writer markers are excluded from this input identity.
Readable malformed sources retain their raw hash even when metadata or container
parsing fails. Sources whose content could not be verified (including synthetic
missing-pack root failures) keep a null hash and remain `Dirty/UnverifiedSources`;
incomplete semantic coverage alone never permits an unverified manifest hit.

Verified warm queries still enumerate and hash sources; they save parsing rather
than claiming to read only dirty bytes. Sources are revalidated after shard reads
and before publishing a rebuilt manifest. A concurrent source change produces a
conflict (or Dirty inspection), so the caller can explicitly retry. This is a
snapshot validation boundary, not a lock on later external world edits.

## Durable cache and accounting

The cache uses an owned, guarded local NTFS directory, separate from world and
session state. Immutable content-addressed artifact shards are verified before a
manifest CAS uses the existing stage/flush/verify/replace primitive. Cancellation,
source conflict or storage failure preserves the previous published head; verified
but unreferenced shards may remain for reuse. Foreign directories are refused.
Corrupt or unsupported shards are rebuilt individually; valid shards are reused.

Canonical cache JSON preserves UTF-16 and typed/syntax addresses with the shared
contract codecs. Each serialized document is bounded to 64 MiB and one million
tokens. A result model that exceeds that serialized shard capacity becomes an
explicit `Failed/CacheSerializationLimit` artifact result, not partial success.

`ScannedArtifacts` counts stored cache misses processed by the pipeline, including
forwarded known failures; it is not a count of successful parser calls. Reused
artifact/source counts and scanned source counts only include stored batches.
Manifest hits/misses, rebuild count, elapsed time and publication are execution
telemetry; they do not participate in semantic equality. On cancellation the scan
completion retains stored artifacts and cancelled/unprocessed source partition.
Discovery/cache-validation cancellation is checked between bounded operations;
an individual synchronous file read or codec operation completes before checking.
