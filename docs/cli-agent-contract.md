# External-agent questionnaire and preferences

`agent <setup|submit|status|help> <world>` manages world-scoped, versioned client
preferences. It neither calls a model/web service nor stages/applies world edits.
`agent help` and `help agent` need no world and list the implemented actions.

All non-help actions accept `--state-root`, `--output <new-file>`, `--help` and
global `--ui-locale`. State and exports must be outside the world; an owned `agent`
repository stores configuration. Existing files and unowned contents are preserved.

| Action | Additional options and result |
| --- | --- |
| `setup` | Optional `source-language`, `target-language`, `operation-id`. Returns a versioned questionnaire; `output` exports that raw JSON document. |
| `submit` | Required `request <file-or-dash>`. Confirms answers under exact revision CAS; returns its receipt and the current configuration. |
| `status` | Returns current revision, configuration and retained submission count. Missing configuration is `NOT_FOUND`, exit 5, with `repair=agent-setup`. |

Generate the questionnaire with setup and retain its operation/world identities
and revision. Family is `nbtcli.next.agent-questionnaire`, schemaVersion 1. Its
data contains exactly `operationId`, `worldId`, `revision`, `configuration`,
`instructions`. Instructions explain the slot/file contract; they confer no
permission and are not executed. Configuration has seven required fields:

| Field | Accepted value and purpose |
| --- | --- |
| `agentName` | Nonblank, at most 256 UTF-16 units; client identity. |
| `maxLinesPerPage` | Integer 1–500; the agent's reading preference. |
| `preferredItemsPerPage` | Integer 1–100000; default maximum slots per new session page, subject to its serialized-byte budget. |
| `canSearchWeb` | Boolean client capability metadata, not browsing authorization. |
| `sourceLanguage`, `targetLanguage` | Nonblank, at most 128 UTF-16 units each; defaults for new sessions. |
| `lowConfidenceThreshold` | Integer 0–10; client review/annotation preference. |

A first template has blank name/source language; fill them before submitting.
Target defaults to `zh_tw`, reading limit to 500, preferred page size to 30 and
confidence threshold to 6. UI locale changes explanatory instructions, not
translation languages or machine keys. Unknown/missing members, wrong types and
malformed JSON are rejected; unsupported family/version uses exit 4, invalid
semantic preferences use exit 2. Nothing silently truncates or drops fields.

Revision is -1 before the first accepted submission, whose receipt revision is 0.
Further setup uses the current revision and a new operation ID. Stale revisions,
foreign world scope and operation-ID reuse with different raw input conflict
(exit 3). Exact original-byte resends return the retained receipt without another
write. Replaying an older submission after a newer configuration returns the old
receipt alongside the current revision/configuration; it does not revert it.

Inputs are limited to 16384 UTF-8 bytes at the file/stdin read boundary, before
decoding or copying the full input. Stdin refuses the first over-budget byte
without waiting for EOF. Canonical
`nbtcli.next.agent-configuration` version-1 state retains up to 1024 submissions,
each receipt, original input bytes as base64 and SHA-256. State decoding verifies
the raw inputs, consecutive revisions, identities and final configuration.
These bounds fit the existing atomic-state JSON/storage budgets. New submissions
at capacity return ResourceLimit; exact historical resends remain available.
Original caller questionnaire/answer files are never deleted or overwritten by
submit, including when validation or storage fails.

Publication uses the existing owned atomic/CAS repository. A submission I/O
failure returns nonzero `AGENT_STATE_IO` with its operation ID and
`repair=retry-same-questionnaire`. Resolve the I/O condition and resend identical
bytes: the retained receipt determines whether publication already happened. Do
not invent a new identity or assume a nonzero exit proves no write occurred.

`translate start` uses confirmed languages and preferred page size only when their
explicit flags are absent. Explicit options take precedence. Existing sessions
retain their original languages/policy when preferences change. Without a config,
start accepts explicit source/target languages and its normal page defaults. The
other fields guide the external agent; they never relax local reply validation,
grant writer authority or enable network calls.

Unlike the legacy flow, submit does not delete a questionnaire or implicitly start
translation: its machine result supplies the explicit next command. Start a new
session deliberately, with its own identity. Legacy unversioned config/questionnaire
data, including all old quality commitments, remain covered by the read-only
legacy retention/import service with raw bytes/provenance. They are not silently
adopted as confirmed new preferences; copy/reconfirm intended values in a generated
questionnaire. Public legacy import/report commands remain integration work. The
instructions now describe the protected slot protocol; promise checkboxes cannot
replace actual local validation.

World scope is the existing NTFS directory binding, not a portable world UUID.
Byte-identical other roots cannot adopt a questionnaire as that world's confirmed
state. No old executable, cache/config folder or writer is called. World translation
apply/key/pack integration, public legacy import, performance/RSS, Minecraft load
and release acceptance remain separate required work.
