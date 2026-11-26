using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The serializer settings every file under <c>%AppData%\PoolScoreboard</c> is written with, so the
/// settings and roster files stay readable and consistent by hand.
/// </summary>
internal static class PersistenceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
