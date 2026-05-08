using System.Text.Json.Serialization;
using KomorebiWindows.Models;

namespace KomorebiWindows;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(KomorebiState))]
internal sealed partial class KomorebiJsonContext : JsonSerializerContext;
