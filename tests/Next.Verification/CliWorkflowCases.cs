namespace Next.Verification;

internal static class CliWorkflowCases
{
    internal static readonly VerificationCase[] Cases =
    [
        new("legacy-projection-untranslated-borrowed-id", "legacy", _ => LegacyImportRecoveryTests.AnUntranslatedPageCannotBorrowAnImportOperationIdentity()),
        new("cli-legacy-list-zero-report-reads", "cli-legacy", _ => LegacyImportRecoveryTests.RecoveryListingNeverReadsReportBodiesBeforeOrAfterPagination()),
        new("legacy-projection-multipage-recovery", "legacy", _ => LegacyImportRecoveryTests.MultiPageProjectionResumesExactRepliesAcrossAnUntranslatedPage()),
        new("legacy-projection-operation-id-content", "legacy", _ => LegacyImportRecoveryTests.ProjectionRejectsCollidingOperationIdsWithDifferentContent()),
        new("cli-legacy-persistence-identity-retry", "cli-legacy", _ => LegacyImportRecoveryTests.PersistenceFailuresRetainAnImportIdentityAndExactRetry()),
        new("cli-legacy-lost-handle-discovery", "cli-legacy", _ => LegacyImportRecoveryTests.LostReportHandlesRemainDiscoverableAfterWorldChanges()),
        new("cli-legacy-candidate-resource-limit", "cli-legacy", _ => CliLegacyImportTests.CandidateFanoutLimitsAreResourcesAndPublishNoPartialReport()),
        new("cli-legacy-input-and-mode-boundaries", "cli-legacy", _ => CliLegacyImportTests.InvalidModesAndOversizedRawInputsRejectBeforeRepositories()),
        new("cli-legacy-mapped-session-canonical-export", "cli-legacy", _ => CliLegacyImportTests.MappedReportsExportCanonicalBytesAndCreateOneReenterableSession()),
        new("cli-legacy-unresolved-and-conflict", "cli-legacy", _ => CliLegacyImportTests.MissingEvidenceAndConflictsKeepTheirReportsAndNeverCreateSessions()),
        new("cli-legacy-metadata-and-raw-encoding", "cli-legacy", _ => CliLegacyImportTests.UserMetadataAndInvalidUtf8AreRetainedWithoutAutomaticAdoption()),
        new("cli-legacy-projection-reentry-and-stale", "cli-legacy", _ => CliLegacyImportTests.StaleProjectionAndPublicationFailuresRemainRecoverableByImportId()),
        new("cli-agent-stdin-early-byte-limit", "cli-agent", _ => CliAgentInputTests.OversizedStdinIsRefusedBeforeEof()),
        new("cli-agent-history-capacity-and-integrity", "cli-agent", _ => AgentConfigurationTests.BoundedHistoryRetainsExactReplayAndRejectsCorruption()),
        new("cli-agent-atomic-update-retry", "cli-agent", _ => CliAgentTests.ABlockedAtomicUpdateOffersAnExactRetryAndPreservesConfiguration()),
        new("cli-agent-request-byte-budget", "cli-agent", _ => CliAgentTests.OversizedQuestionnairesAreRejectedBeforePublication()),
        new("cli-agent-questionnaire-replay-defaults", "cli-agent", _ => CliAgentTests.QuestionnaireSubmissionPersistsRawInputAndDefaultsWithoutStartingASession()),
        new("cli-agent-invalid-and-cas", "cli-agent", _ => CliAgentTests.InvalidAnswersAndRevisionConflictsPreserveTheAcceptedConfiguration()),
        new("cli-agent-state-and-world-ownership", "cli-agent", _ => CliAgentTests.ExistingExportsUnownedStateAndForeignWorldAnswersAreProtected()),
        new("cli-agent-help-and-missing", "cli-agent", _ => CliAgentTests.HelpAndMissingConfigurationAreExplicitAndReadOnlyForTheWorld()),
        new("cli-session-selection-before-slot-budget", "cli-session", _ => SessionSelectionBudgetTests.SelectionPrecedesTheSessionSlotLimit()),
        new("cli-session-preferences-failure-preserves-work", "cli-session", _ => CliSessionTests.PreferenceFailuresRetainAcceptedWorkAndDoNotConsumeReplies()),
        new("cli-session-start-presentation-replay", "cli-session", _ => CliSessionTests.StartReplayBindsTheRetainedGroupPresentationPreference()),
        new("cli-session-invalid-start-no-state", "cli-session", _ => CliSessionTests.InvalidStartArgumentsDoNotCreateToolRepositories()),
        new("cli-session-limits-and-read-errors", "cli-session", _ => CliSessionTests.LimitsAndUnreadableSourcesKeepTheirActualClassification()),
        new("cli-session-local-memory", "cli-session", _ => CliSessionTests.MemoryCandidatesUseOnlyValidatedLocalSessionReplies()),
        new("cli-session-file-exchange-replay", "cli-session", _ => CliSessionTests.FileExchangePersistsRepliesBeforeReturningTheNextPage()),
        new("cli-session-reply-and-source-freshness", "cli-session", _ => CliSessionTests.InvalidAndStaleRepliesPreserveWorkEvenWhenTheCacheIsFresh()),
        new("cli-session-management-cas-reset", "cli-session", _ => CliSessionTests.ManagementCommandsUseRevisionCasAndRetainResetHistory()),
        new("cli-session-selection-help-export", "cli-session", _ => CliSessionTests.SelectionHelpAndRejectedExportsNeverWriteTheWorld()),
    ];
}
