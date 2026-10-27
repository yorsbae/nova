using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Core.Configuration;

/// <summary>
/// Reads the eye style tolerantly. A settings file written by an older version may name a style that no longer exists (AnimeA, AnimeB,
/// AnimeC) or one this version does not know; either way the file must still load, so the old names become AnimeAura and anything else
/// unknown becomes Pet, instead of the whole settings file being thrown away. Numbers are passed through for the validator to judge.
/// </summary>
public sealed class IslandFaceStyleConverter : JsonConverter<IslandFaceStyle>
{
    public override IslandFaceStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.TryGetInt32(out var n) ? (IslandFaceStyle)n : IslandFaceStyle.Pet;
        if (reader.TokenType != JsonTokenType.String) return IslandFaceStyle.Pet;
        var name = reader.GetString() ?? "";
        if (name is "AnimeA" or "AnimeB" or "AnimeC") return IslandFaceStyle.AnimeAura;
        return Enum.TryParse<IslandFaceStyle>(name, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : IslandFaceStyle.Pet;
    }

    public override void Write(Utf8JsonWriter writer, IslandFaceStyle value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
