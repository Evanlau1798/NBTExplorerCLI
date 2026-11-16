using Next.Domain;
using System.Text.Json;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Next.Formats;

public static class TranslationSessionJson
{
    public const string Family = "nbtcli.next.translation-session";
    public const int Version = 1;
    private sealed record SessionDto(Guid SessionId, long Revision, SessionSourceSnapshot Source, string SourceLocale, string TargetLocale, SessionPolicy Policy,
        ImmutableArray<SessionSlot> Slots, ImmutableArray<ImmutableArray<Hash256>> Pages, ImmutableArray<SessionOperationReceipt> Operations,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionWorkspace? Workspace = null);
    private static string Encode(TranslationSession state) => ContractJson.Write(Family, new SessionDto(state.SessionId, state.Revision, state.Source, state.SourceLocale, state.TargetLocale, state.Policy, state.Slots, state.Pages, state.Operations, state.Workspace));
    public static string Write(TranslationSession session)
    {
        SessionStateValidation.State(session); var json = Encode(session); _ = Read(json); return json;
    }
    public static TranslationSession Read(string json) => ContractJson.Read(json, Family, data =>
    {
        var fields = new[] { "sessionId", "revision", "source", "sourceLocale", "targetLocale", "policy", "slots", "pages", "operations" };
        ContractJson.Fields(data, data.TryGetProperty("workspace", out _) ? fields.Append("workspace").ToArray() : fields);
        var session = ContractJson.Decode<TranslationSession>(data); SessionStateValidation.State(session);
        if (Encode(session) != json) throw new JsonException("Session state must be canonical");
        return session;
    });
    public static string WriteStart(SessionStartRequest request)
    {
        SessionStateValidation.Start(request);
        request = request with { Formats = (request.Formats.IsDefault ? [] : request.Formats).Where(item => item.Format != PlaceholderFormat.Literal).OrderBy(item => item.SlotId.Hex, StringComparer.Ordinal).ToImmutableArray() };
        var json = ContractJson.Write("nbtcli.next.session-start", request);
        _ = ContractJson.Read(json, "nbtcli.next.session-start", data => { var decoded = ContractJson.Decode<SessionStartRequest>(data); SessionStateValidation.Start(decoded); return decoded; });
        return json;
    }
}
