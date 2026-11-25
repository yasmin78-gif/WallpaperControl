using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RemoteExperiment;

// Explicit metadata is shared by desktop checks and Android; no implicit reflection contract.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PairQr))]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PairPending))]
[JsonSerializable(typeof(ClaimRequest))]
[JsonSerializable(typeof(Credential))]
[JsonSerializable(typeof(SavedConnection))]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(Result))]
public partial class WireJsonContext : JsonSerializerContext { }

public static class WireJson
{
    public static JsonTypeInfo<T> Type<T>() => (JsonTypeInfo<T>)(WireJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new InvalidOperationException("Missing wire JSON contract"));
}
