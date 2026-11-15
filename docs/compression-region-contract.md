# Compression and Region contract

## Dependency decision

Use BCL GZipStream/ZLibStream writers for nonempty input and SharpZipLib **1.4.2**, pinned with NuGet lock files, for inflation and empty-frame writing. BCL's empty Write/Flush/Dispose emits no frame; SharpZipLib's mature writers finish a valid frame through the same output quota, with gzip time fixed at Unix epoch. The MIT notice from the v1.4.2 tag is shipped in `licenses/SharpZipLib.md`. Only Next.Formats may directly reference this assembly; Domain remains framework-only. No legacy code or runtime is involved.

The .NET 10.0.15 probe (`tmp/compression-probe`) showed that ordinary BCL readers accept incomplete streams, and even strict validation accepts trailing bytes while consuming the underlying stream. A custom Deflate implementation would add unnecessary risk. SharpZipLib exposes completion and remaining input, so framing can be checked without relying on seek position. Sources: [Inflater](https://github.com/icsharpcode/SharpZipLib/blob/v1.4.2/src/ICSharpCode.SharpZipLib/Zip/Compression/Inflater.cs), [license](https://github.com/icsharpcode/SharpZipLib/blob/v1.4.2/LICENSE.txt).

## Frames

Raw, gzip and zlib are explicit choices, never autodetected or fallback. Decode consumes the complete source, returns decoded bytes, consumed bytes and member count, and rejects incomplete frames, wrong checksums and trailing data. Gzip supports concatenated members with one cumulative decoded budget, optional headers and FHCRC. Zlib dictionaries are rejected. Empty raw content is a valid frame; NBT document parsing separately rejects invalid NBT. Caller streams stay open; short reads and non-seek streams have the same semantics.

Compressed input/output is capped at 64 MiB and decoded content at 128 MiB, or a caller's smaller validated limits. Each output block is checked before accumulation. Writers finish and check the trailer before returning bytes. Errors carry a source byte offset. LZ4, external chunks and unknown codecs are unsupported; they never become empty NBT.

One decode owns a single inflater, scratch buffer and CRC workspace. Each gzip member resets these while retaining the cumulative output budget; empty members do not allocate another window. The regression uses 10,000 valid empty members (200 KB input, zero decoded content) and an 8 MiB cumulative-allocation ceiling; it does not claim peak RSS or impose a member-count rejection rule.

Python's independent zlib/gzip oracle and frozen regression F01 check full stream completion as well as contents. Product writer output is also read by BCL in the verification suite.

Malformed dynamic Huffman data can make SharpZipLib 1.4.2 throw IndexOutOfRangeException instead of its compression exception. Only the direct Inflate call maps these observed decoder failures to CorruptCompression. It also accepts illegal backward distances before history exists. A second bounded BCL read must agree byte-for-byte with the framed decoded result, so these inputs reject. Neither reader alone satisfies the contract: BCL misses framing boundaries, SharpZipLib misses this Deflate validity check. This costs a second decode pass without a second full decoded buffer; optimization is deferred to measured G07 work. The frozen Python oracle and deterministic mutation test retain both failures.

## Region

RegionFile first validates the entire 8192-byte header and all 1024 descriptors, including allocation bounds, overlaps, length/flag and external marker shape. Artifacts must contain complete 4096-byte sectors and stay within 64 MiB. Arithmetic uses checked wide offsets; payload buffers are allocated only after validation. The resulting snapshot and descriptors are immutable; returned payloads are copies.

Chunk indices are `localX + localZ * 32`. World/negative coordinate and filename matching uses Domain's existing ChunkPosition and SourceAddress, never a guessed zero. An absent chunk raises KeyNotFoundException; a present unsupported chunk raises UnsupportedProfile. This distinguishes both from corrupt data.

ReadChunk/WithChunk are explicit single-chunk scope. WithChunk validates the selected original chunk and new typed document, then appends sectors to an in-memory candidate. It never performs file I/O or commits on disposal. Unselected sectors, compressed payloads, codec flags, locations and timestamps remain byte-for-byte intact, including opaque unsupported chunks; their semantics are not declared validated. No-op same-codec/same-document returns exact original bytes and ignores the proposed timestamp. Real changes use the caller's explicit uint timestamp. Encoded payload plus framing must fit at most 255 sectors; 256 is rejected before narrowing, and the final artifact must still fit 64 MiB. Appending deliberately trades fragmentation for a small preservation rule; compaction is outside this operation.
