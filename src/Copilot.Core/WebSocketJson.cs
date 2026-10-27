using System.Net.WebSockets;
using System.Text.Json;
namespace Copilot.Core;
public static class WebSocketJson
{
    public static async Task<JsonDocument> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        using var ms = new MemoryStream(); var bytes = new byte[16384]; WebSocketReceiveResult r;
        do { r = await ws.ReceiveAsync(new ArraySegment<byte>(bytes), ct); if (r.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Remote closed"); ms.Write(bytes, 0, r.Count); if (ms.Length > 4 * 1024 * 1024) throw new InvalidDataException("Message too large"); } while (!r.EndOfMessage);
        return JsonDocument.Parse(ms.ToArray());
    }
    public static Task SendAsync(ClientWebSocket ws, object payload, CancellationToken ct) => ws.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(payload)), WebSocketMessageType.Text, true, ct);
}
