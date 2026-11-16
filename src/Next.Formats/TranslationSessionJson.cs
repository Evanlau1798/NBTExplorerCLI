using Next.Domain;
using System.Text.Json;
using System.Collections.Immutable;

namespace Next.Formats;

public static class TranslationSessionJson
{
    public const string Family = "nbtcli.next.translation-session";
    public const int Version = 1;
    public static string Write(TranslationSession session)
    {
        SessionStateValidation.State(session); var json = ContractJson.Write(Family, session); _ = Read(json); return json;
    }
    public static TranslationSession Read(string json) => ContractJson.Read(json, Family, data =>
    {
        ContractJson.Fields(data, "sessionId", "revision", "source", "sourceLocale", "targetLocale", "policy", "slots", "pages", "operations");
        var session = ContractJson.Decode<TranslationSession>(data); SessionStateValidation.State(session);
        if (ContractJson.Write(Family, session) != json) throw new JsonException("Session state must be canonical");
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
