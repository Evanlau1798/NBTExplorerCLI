using System.Collections.Immutable;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class SessionApplyTests
{
    private static (TranslationSessionService Service, Guid Id, FileSystemTranslationSessionStore Store) Session(string state, WorldQuerySnapshot snapshot, Func<SessionPageSlot, string?>? translation = null)
    {
        var store = new FileSystemTranslationSessionStore(Path.Combine(state, "sessions")); var service = new TranslationSessionService(store); var id = Guid.NewGuid();
        service.Start(snapshot, id, "en_us", "zh_tw", Guid.NewGuid());
        for (var page = 0; page < service.Read(id).Pages.Length; page++)
        {
            var source = service.Export(id, page);
            var reply = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(source, Guid.NewGuid()));
            reply = reply with { Slots = reply.Slots.Select(slot =>
            {
                var original = source.Slots.Single(item => item.SlotId == slot.SlotId); var text = translation is null ? "譯:" + original.SourceText : translation(original);
                return slot with { State = text is null ? SessionReplyState.Unchanged : SessionReplyState.Translated, Text = text };
            }).ToImmutableArray() };
            service.Submit(id, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
        }
        return (service, id, store);
    }
    private static PatchOperation[] Operations(TranslationSession session, bool mixed)
    {
        var operations = session.Slots.Select((slot, index) => (PatchOperation)new LiteralPatch("slot-" + index, new(slot.Slot, slot.Reply!))).ToList();
        if (mixed)
        {
            var path = new TypedPath([PathSegment.ForKey("Data"), PathSegment.ForKey("Counter")]);
            operations.Add(new TypedPatch("typed-counter", new(new(World, "level.dat", ArtifactKind.Nbt), path, "typed.value"), TypedEdit.Replace(path, new NbtInt(7), new NbtInt(8))));
        }
        return operations.ToArray();
    }
    public static void SuccessfulPureAndMixedAppliesSealOneDurableSession()
    {
        foreach (var mixed in new[] { false, true })
        {
            var root = mixed ? WorldPreparationTests.CreateWorld() : WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
                var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, mixed));
                using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
                var engine = WorldApplyTests.Engine(root, state); var applied = engine.Apply(prepared, true);
                Require(applied.Success && applied.Requested == translated.Slots.Length + (mixed ? 1 : 0), "real scan/session replies reach one complete pure or mixed world receipt");
                var operation = Guid.NewGuid(); var sealedReceipt = service.Seal(id, translated.Revision, operation, plan.PlanHash, engine);
                var restarted = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(state, "sessions"))); var sealedState = restarted.Read(id);
                Require(sealedState.Revision == translated.Revision + 1 && sealedState.Slots.All(slot => slot.State == SessionSlotState.Applied)
                    && sealedState.Slots.Select(slot => slot.Reply).SequenceEqual(translated.Slots.Select(slot => slot.Reply)), "restart replays the durable completion and retains every validated translation");
                Require(translated.Operations.Zip(sealedState.Operations).All(pair => pair.First.InputBase64 == pair.Second.InputBase64 && pair.First.InputHash == pair.Second.InputHash), "original reply inputs and prior operation receipts remain archived intact");
                var bytes = store.Read(id)!;
                var replay = restarted.Seal(id, translated.Revision, operation, plan.PlanHash, WorldApplyTests.Engine(root, state));
                Require(replay.InputHash == sealedReceipt.InputHash && replay.Revision == sealedReceipt.Revision
                    && store.Read(id)!.SequenceEqual(bytes), "same completion request replays without advancing or rewriting session state");
                Reject<ArtifactConflictException>(() => restarted.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine));
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void PartialApplyKeepsRepliesUntilRecoveryCompletesEveryOutput()
    {
        var root = WorldPreparationTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, true));
            var storage = new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(2, true));
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state, storage); var partial = engine.Apply(prepared, true);
            Require(!partial.Success, "fixture interrupts a real mixed physical apply after its second replace");
            var before = store.Read(id)!; var operation = Guid.NewGuid();
            Reject<InvalidOperationException>(() => service.Seal(id, translated.Revision, operation, plan.PlanHash, engine));
            Require(store.Read(id)!.SequenceEqual(before) && service.Read(id).Slots.All(slot => slot.State == SessionSlotState.Translated), "partial world results preserve reply inputs and all untranslated completion state");
            var fresh = WorldApplyTests.Engine(root, state); Require(fresh.Recover(plan.PlanHash, true).Success, "normal recovery readbacks every remaining mixed output");
            service.Seal(id, translated.Revision, operation, plan.PlanHash, fresh);
            Require(service.Read(id).Slots.All(slot => slot.State == SessionSlotState.Applied), "the same completion identity becomes usable only after durable recovery succeeds");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void ACompleteSubsetReceiptCannotSealUnwrittenTranslations()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false).Take(1));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state); Require(engine.Apply(prepared, true).Success, "a legitimate subset plan succeeds for its own declared work");
            var before = store.Read(id)!;
            Reject<ArgumentException>(() => service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine));
            Require(store.Read(id)!.SequenceEqual(before), "a subset success never archives unrelated uncommitted translations");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void AppliedRepliesNeedResetAndRemainMemoryCandidates()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id); var target = Guid.NewGuid();
            service.Start(snapshot, target, "en_us", "zh_tw", Guid.NewGuid());
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state); engine.Apply(prepared, true); service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine);
            var applied = service.Read(id); var slot = applied.Slots[0];
            var memoryRetained = service.Memory(target, slot.Slot.Id.Digest, [id], "glossary1").Any(candidate => candidate.Text == slot.Reply!.Text);
            var page = service.Export(id, 0); var reply = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            reply = reply with { Slots = reply.Slots.Select(item => item with { State = SessionReplyState.Translated, Text = "Overwrite" }).ToImmutableArray() };
            var before = store.Read(id)!; var rejected = false;
            try { service.Submit(id, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply))); }
            catch (InvalidOperationException) { rejected = true; }
            Require(memoryRetained && rejected && store.Read(id)!.SequenceEqual(before), $"applied knowledge remains available and new replies require explicit reset; memory={memoryRetained}, rejected={rejected}");
            service.Execute(new(Guid.NewGuid(), id, applied.Revision, SessionCommandKind.Reset, Confirmed: true, ResetSlots: applied.Slots.Select(item => item.Slot.Id.Digest).ToImmutableArray()));
            var reset = service.Read(id);
            Require(reset.Slots.All(item => item.State == SessionSlotState.Pending) && reset.Operations.Any(item => item.Kind == "apply" && item.InputBase64 is not null), "explicit reset reopens progress while preserving the complete apply and reply history");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    private sealed class InterceptStore(ITranslationSessionStore inner) : ITranslationSessionStore
    {
        internal Action? BeforeWrite { get; set; }
        internal Action? BeforeRead { get; set; }
        public byte[]? Read(Guid id) { BeforeRead?.Invoke(); return inner.Read(id); }
        public void Write(Guid id, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            var callback = BeforeWrite; BeforeWrite = null; callback?.Invoke(); inner.Write(id, bytes, expected);
        }
    }
    public static void ExactSealCasReplayNeedsNoAdditionalStorageRead()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state);
            Require(engine.Apply(prepared, true).Success, "exact-CAS replay starts with a complete forward journal");
            var operation = Guid.NewGuid(); var intercepted = new InterceptStore(store); var reads = 0;
            SessionOperationReceipt? winner = null;
            intercepted.BeforeRead = () =>
            {
                if (++reads > 2) throw new IOException("Unexpected read after the exact CAS replay was confirmed");
            };
            intercepted.BeforeWrite = () => winner = service.Seal(id, translated.Revision, operation, plan.PlanHash, engine);
            var actual = new TranslationSessionService(intercepted).Seal(id, translated.Revision, operation, plan.PlanHash, engine);
            Require(reads == 2 && winner is not null && actual.OperationId == winner.OperationId && actual.InputHash == winner.InputHash
                && actual.Revision == winner.Revision && service.Read(id).Slots.All(slot => slot.State == SessionSlotState.Applied),
                "an exact competing seal returns its durable receipt from the captured CAS fallback state");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void SealStorageFailuresAndCasKeepTheAcceptedReply()
    {
        foreach (var race in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
                var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false));
                using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
                var engine = WorldApplyTests.Engine(root, state); engine.Apply(prepared, true); var before = store.Read(id)!;
                var intercepted = new InterceptStore(store); var sealing = new TranslationSessionService(intercepted); var operation = Guid.NewGuid();
                intercepted.BeforeWrite = race ? () => service.Execute(new(Guid.NewGuid(), id, translated.Revision, SessionCommandKind.Note, Text: "concurrent note", Origin: "human"))
                    : () => throw new IOException("Injected completion storage failure");
                if (race) Reject<ArtifactConflictException>(() => sealing.Seal(id, translated.Revision, operation, plan.PlanHash, engine));
                else Reject<IOException>(() => sealing.Seal(id, translated.Revision, operation, plan.PlanHash, engine));
                var retained = service.Read(id);
                Require(retained.Slots.All(slot => slot.State == SessionSlotState.Translated) && translated.Operations.Zip(retained.Operations).All(pair => pair.First.InputBase64 == pair.Second.InputBase64)
                    && (race ? retained.Workspace!.Notes.Single().Text == "concurrent note" : store.Read(id)!.SequenceEqual(before)), "failed completion never loses the accepted reply or a concurrent note");
                service.Seal(id, retained.Revision, operation, plan.PlanHash, engine);
                Require(service.Read(id).Slots.All(slot => slot.State == SessionSlotState.Applied), "retry safely seals after storage recovery or a fresh CAS revision");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    private sealed class StopRollbackProgress(IApplyJournalStore inner) : IApplyJournalStore
    {
        public byte[]? Read(Hash256 hash) => inner.Read(hash);
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            var journal = ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span));
            if (journal.Direction == ApplyDirection.Rollback && journal.Phase == ApplyJournalPhase.Committing && journal.CompletedCount > 0)
                throw new InvalidOperationException("Injected stop before rollback progress publication");
            inner.Write(hash, bytes, expected);
        }
    }
    public static void AnOlderSuccessDuringRollbackCannotSealCurrentState()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state); Require(engine.Apply(prepared, true).Success, "the plan first has a genuine successful durable receipt");
            var rollback = new WorldApplyEngine(inventory, new ArtifactStorage(root, World), new FileSystemWorldBackupStore(Path.Combine(state, "objects")),
                new StopRollbackProgress(new FileSystemApplyJournalStore(Path.Combine(state, "journals"))));
            Reject<InvalidOperationException>(() => rollback.Recover(plan.PlanHash, true, rollback: true));
            Require(engine.ReadReceipt(plan.PlanHash)!.Success, "the interrupted rollback record still references the historical successful receipt");
            var before = store.Read(id)!;
            Reject<InvalidOperationException>(() => service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine));
            Require(store.Read(id)!.SequenceEqual(before), "a nonterminal rollback journal cannot seal a session using its older success");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void KeyPackFailureCannotBeHiddenBySuccessfulWorldWrites()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString("Hello"))]))]);
            Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            const string pack = "z-resources.zip"; const string entry = "assets/test/lang/zh_tw.json";
            Put(root, pack, Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"), (entry, "{}")));
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, store) = Session(state, snapshot); var translated = service.Read(id);
            var selected = translated.Slots.Single(item => item.Slot.Address.Artifact.RelativePath == "level.dat" && item.Slot.SourceText == "Hello");
            var profile = MinecraftProfiles.ForDataVersion(4325)!; var outer = new ArtifactAddress(World, pack, ArtifactKind.Archive);
            var key = new KeyConversionPatch("key", new(selected.Slot.Address, new NbtString("Hello"), KeyConversionRule.LiteralToTranslateComponent, profile.Id, profile.Version,
                selected.Slot, selected.Reply!, new(new(World, pack, ArtifactKind.Archive, entry), outer, "zh_tw", "test.greeting", selected.Reply!.Text)));
            var operations = Operations(translated, false).Where(operation => operation is not LiteralPatch literal || literal.Translation.Slot.Id != selected.Slot.Id).Append(key);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            Require(plan.Outputs[^1].Artifact == outer, "the pack dependency is the final physical commit in this bounded fault fixture");
            var storage = new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(plan.Outputs.Length, false));
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state, storage); var receipt = engine.Apply(prepared, true);
            Require(!receipt.Success && receipt.Counts[OperationOutcome.Applied] > 0, "some world outputs have read back while the final resource pack has not committed");
            var before = store.Read(id)!;
            Reject<InvalidOperationException>(() => service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine));
            Require(store.Read(id)!.SequenceEqual(before), "successful NBT/text outputs cannot archive replies while their language/pack dependency failed");
            var recovered = WorldApplyTests.Engine(root, state); Require(recovered.Recover(plan.PlanHash, true).Success, "recovery verifies the remaining exact ZIP output");
            service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, recovered);
            using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(root, pack))); using var stream = zip.GetEntry(entry)!.Open(); using var reader = new StreamReader(stream);
            using var language = BoundedJson.ReadDecoded(Encoding.UTF8.GetBytes(reader.ReadToEnd()));
            Require(NativeJsonStrings.Read(language.RootElement.GetProperty("test.greeting")) == selected.Reply.Text && service.Read(id).Slots.All(slot => slot.State == SessionSlotState.Applied), "actual language-key readback and complete receipt precede durable session sealing");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void LiveReferenceAndUnchangedNamesJoinTheSameSealedSession()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, _) = Session(state, snapshot, slot => slot.Context == "entity.name" ? null : slot.Context == "armor_stand.name" ? "Sentinel" : "譯:" + slot.SourceText);
            var translated = service.Read(id);
            var names = translated.Slots.Where(slot => slot.Slot.Role == "armor_stand.name").Select(slot => new LiteralTranslation(slot.Slot, slot.Reply!));
            var group = new WorldReferenceService(inventory).Create(snapshot, "rename", names);
            var literals = translated.Slots.Where(slot => slot.State == SessionSlotState.Translated && slot.Slot.Role != "armor_stand.name")
                .Select((slot, index) => (PatchOperation)new LiteralPatch("literal-" + index, new(slot.Slot, slot.Reply!)));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, literals.Append(group));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state); Require(engine.Apply(prepared, true).Success, "native/direct reference outputs and ordinary session literals share one complete verified plan");
            service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine);
            var completed = service.Read(id);
            Require(completed.Slots.All(slot => slot.State == SessionSlotState.Applied) && completed.Slots.Single(slot => slot.Slot.Role == "entity.name").Reply is null,
                "the same durable completion seals the selected identifying translation and preserves explicit unchanged names");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void RetainedCompletionMustReplayItsSuccessfulExactBinding()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var (service, id, _) = Session(state, snapshot); var translated = service.Read(id);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(translated, false));
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state); engine.Apply(prepared, true); service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine);
            var sealedState = service.Read(id); var retained = sealedState.Operations[^1];
            var completion = SessionApplyCompletionJson.Read(Encoding.UTF8.GetString(Convert.FromBase64String(retained.InputBase64!)));
            var incomplete = new ApplyReceipt(completion.Receipt.PlanHash, completion.Receipt.Revision, completion.Receipt.RequestedOperationIds, completion.Receipt.Results, false);
            foreach (var changed in new[] { completion with { Receipt = incomplete }, completion with { Revision = completion.Revision + 1 } })
            {
                var bytes = Encoding.UTF8.GetBytes(SessionApplyCompletionJson.Write(changed));
                var forged = retained with { InputBase64 = Convert.ToBase64String(bytes), InputHash = Hash256.Compute(bytes) };
                Reject<System.Text.Json.JsonException>(() => TranslationSessionJson.Write(sealedState with { Operations = sealedState.Operations.SetItem(sealedState.Operations.Length - 1, forged) }));
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
}
