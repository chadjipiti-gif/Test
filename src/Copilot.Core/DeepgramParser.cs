using System.Text.Json;
namespace Copilot.Core;
public static class DeepgramParser
{
    public static IReadOnlyList<TranscriptSegment> Parse(JsonElement r, string stream, DateTimeOffset offset)
    {
        var segments = new List<TranscriptSegment>();
        if (!r.TryGetProperty("channel", out var channel)) return []; var alt = channel.GetProperty("alternatives")[0]; var text = alt.GetProperty("transcript").GetString(); if (string.IsNullOrWhiteSpace(text)) return [];
        bool final = r.GetProperty("is_final").GetBoolean(); double start = r.GetProperty("start").GetDouble(), duration = r.GetProperty("duration").GetDouble(); double? confidence = alt.TryGetProperty("confidence", out var c) ? c.GetDouble() : null;
        var role = stream == "ME" ? SpeakerRole.Me : stream == "THEM" ? SpeakerRole.Them : SpeakerRole.Unknown;
        var baseTime = TimeSpan.FromTicks(offset.UtcTicks);
        if (stream == "MIXED" && final && alt.TryGetProperty("words", out var words))
        {
            var group = new List<JsonElement>(); string? last = null;
            void Emit() { if (group.Count == 0) return; var first = group[0]; var end = group[^1]; var t = string.Join(" ", group.Select(w => w.TryGetProperty("punctuated_word", out var p) ? p.GetString() : w.GetProperty("word").GetString())); segments.Add(new(Guid.NewGuid(), SpeakerRole.Unknown, t, true, baseTime + TimeSpan.FromSeconds(first.GetProperty("start").GetDouble()), baseTime + TimeSpan.FromSeconds(end.GetProperty("end").GetDouble()), confidence, last)); group.Clear(); }
            foreach (var w in words.EnumerateArray()) { var speaker = w.TryGetProperty("speaker", out var sp) ? sp.ToString() : null; if (speaker != last) Emit(); last = speaker; group.Add(w); }
            Emit();
        }
        else segments.Add(new(Guid.NewGuid(), role, text, final, baseTime + TimeSpan.FromSeconds(start), baseTime + TimeSpan.FromSeconds(start + duration), confidence));
        return segments;
    }
}
