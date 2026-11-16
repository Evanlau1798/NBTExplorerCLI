using Next.Domain;
using System.Text.Json;

namespace Next.Formats;

public static class SessionCommandJson
{
    public static string Write(SessionCommand command) => ContractJson.Write("nbtcli.next.session-command", command with { ResetSlots = command.ResetSlots.IsDefault ? [] : command.ResetSlots });
    public static SessionCommand Read(string json) => ContractJson.Read(json, "nbtcli.next.session-command", data =>
    {
        ContractJson.Fields(data, "operationId", "sessionId", "revision", "kind", "page", "slotId", "text", "source", "origin", "confirmed", "resetSlots");
        var command = ContractJson.Decode<SessionCommand>(data);
        if (command.ResetSlots.IsDefault) throw new JsonException("Missing reset slot declaration");
        return command;
    });
}
