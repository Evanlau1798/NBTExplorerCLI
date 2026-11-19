using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public enum SessionSlotState { Pending, Translated, Unchanged, Conflict, Applied }
public sealed record SessionPolicy(int MaximumPageSlots = 30, int MaximumPageBytes = 65_536, bool PreservePlaceholderOrder = false,
    bool GroupRelatedText = false);
public sealed record SessionGroupRoot(Hash256 SlotId, SourceAddress Container);
public sealed record SessionSourceSnapshot(Guid WorldId, StableIdentity SnapshotId, ImmutableArray<ManifestSource> Sources,
    ImmutableArray<string> Dimensions, ImmutableArray<string> Regions, ImmutableArray<string> Types, ImmutableArray<string> ArtifactPaths,
    InterpretationVersions Versions, bool CoverageComplete, ImmutableArray<SessionGroupRoot> ContextRoots = default);
public sealed record SessionSlot(TextSlot Slot, SessionSlotState State = SessionSlotState.Pending, SlotReply? Reply = null, string? Reason = null, PlaceholderFormat Format = PlaceholderFormat.Literal);
public sealed record SessionSlotFormat(Hash256 SlotId, PlaceholderFormat Format);
public sealed record SessionOperationReceipt(Guid OperationId, Hash256 InputHash, long FromRevision, long Revision, string Kind,
    ImmutableArray<Hash256> Slots, string? InputBase64);
public sealed record TranslationSession(Guid SessionId, long Revision, SessionSourceSnapshot Source, string SourceLocale, string TargetLocale,
    SessionPolicy Policy, ImmutableArray<SessionSlot> Slots, ImmutableArray<ImmutableArray<Hash256>> Pages, ImmutableArray<SessionOperationReceipt> Operations, SessionWorkspace? Workspace = null);
public sealed record SessionStartRequest(Guid SessionId, SessionSourceSnapshot Source, string SourceLocale, string TargetLocale, SessionPolicy Policy, ImmutableArray<TextSlot> Slots, ImmutableArray<SessionSlotFormat> Formats = default);
