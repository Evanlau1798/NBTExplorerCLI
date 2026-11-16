# Durable apply and recovery contracts

`WorldApplyEngine` consumes a `PreparedWorldPlan` from the same world preparation
service. Apply and recovery require an explicit offline-world acknowledgement. The
native writer lease excludes cooperating writers; it does not make Minecraft or an
unrelated application obey the tool's lock. Apply consumes and disposes its prepared
candidates, including on failure. Refusing the offline acknowledgement leaves them
available to the caller.

Inventory, storage and prepared candidates also carry an opaque `AccessBinding` for
the world ID and the guarded NTFS root's volume serial number and native directory ID.
The binding is read from a directory handle each time; case-only spelling differences
cannot conflate separate directories, and replacing a root changes its authority.
Wrappers must forward
their underlying binding; unknown or mismatched bindings cannot prepare or apply.
The journal header retains this binding, and receipt/recovery reads check it before
replay. Identical world copies with the same GUID and bytes therefore cannot redirect
a prepared candidate or reuse another root's successful receipt. Semantic plan hashes
remain portable; moving a journal to a different root requires an explicit new plan.
Native file IDs identify existing directories; they are not a permanent identity
across root deletion or volume restoration. A recreated/restored root requires a
fresh plan. The recreation test observes a changed ID in that run, without proving
that NTFS can never reuse one after deletion.

## Persistence and submission

Before the first world replacement, the engine persists and rereads every verified
postimage, captures a verified raw backup of the plan's input images, checks current
preconditions, and publishes a journal. An immutable header contains the exact plan,
PreparedManifest and preimage backup hash. Small progress records bind both hashes,
the header hash, direction, sequence, previous record hash and current receipt hash.
Content-addressed objects hold the header, images, record generations and receipts;
only the current journal pointer uses compare-and-exchange replacement.

The complete candidate set is checked again after backup publication. Every file
commit checks its current image and candidate, performs its explicit native operation,
and reads back the target before advancing progress. Completion rereads every promised
output and non-output precondition before persisting a receipt. A stored progress
counter describes the known prefix; recovery still inspects actual file images because
replacement may succeed before the next record becomes durable.

`Apply` on an already journaled plan releases the supplied candidates and returns the
current receipt or resumes recovery. `ReadReceipt(planHash)` reads the current persisted
receipt; a requested older revision follows the immutable predecessor chain. Old
receipts describe their recorded readbacks and retain their original counts.

## Recovery and rollback

`Recover(planHash, worldOffline)` loads the bound header and verifies the complete
preimage backup. Each current artifact must match its recorded preimage or postimage.
An unknown read or third image prevents new world commits. Recovery stages and verifies
the complete desired set from immutable images before committing; it never trusts an
old candidate path as a writable target.

The default direction is forward. `rollback: true` requests restoration of this plan's
verified preimages, and that direction is durable before the first restoration.
Interrupted rollback resumes in the same direction. A successful standalone restore
plan reports its own operation as `applied`. Restoring an original plan's preimages
reports that plan's operations as `rolledBack`. `ApplyReceipt.Success` describes the
original apply, so a completed rollback has rolledBack proofs and is not apply success.

Missing files and existing empty files remain distinct. A restore to absence uses
the checked handle deletion lifecycle; it does not publish an empty replacement.
Missing output directories are created only during explicit commit.

## Receipts and failure boundaries

Every emitted receipt has exactly the plan's operation IDs and one of the nine outcome
categories per operation. Applied and unchanged results require postimage readbacks;
rolledBack requires preimage readbacks. A newer receipt reclassifies the same operations
and retains the older receipt. Previously unchanged members can become conflict or
recoveryRequired when their later observed content no longer supports the earlier proof.
The same downgrade applies to restored members of an interrupted rollback; it does not
permit reclassifying rolledBack operations as newly applied.

Cancellation before the first commit preserves the original world and persists a
cancelled receipt. Cancellation after a replacement preserves known applied results and
marks incomplete operations recoveryRequired. A required backup that is locked or
corrupt cannot turn already matching postimages into successful recovery: dependency
completion remains false and the journal stays recoverable.

Unverifiable headers, world bindings or journal objects are rejected before they can
authorize writes. I/O failures after a trusted journal exists retain its durable input;
an inability to persist a new receipt remains an error rather than invented success.
Conflict and other terminal failure receipts replay without automatically overwriting
content. Resolving a conflict requires an explicit new plan with current preconditions.

Process-kill verification covers first replacement before/after, a later replacement,
readback before receipt, repeated recovery interruption, rollback interruption and
deletion after handle disposition. This is not a kernel or power-loss guarantee.
Reserved candidates left by killed processes are retained as recovery evidence; this
service does not yet garbage-collect all orphan candidates or immutable history.

Plans containing reference synchronization also recheck the relevant source catalog
before and between physical commits. New sources stop the stale group; interrupted
groups retain their known images and a non-success receipt for explicit recovery.
Existing dependency images are also rechecked before each reference-group commit;
a third image stops further writes and produces a conflict receipt.

`TranslationSessionService.Seal` consumes the current completed forward journal and
its successful receipt, verifies exact session coverage, and persists a replayable
completion in the session ledger. A subset of successful physical writes cannot
complete a session. See [session-contract.md](session-contract.md). Public CLI
integration remains separate.
