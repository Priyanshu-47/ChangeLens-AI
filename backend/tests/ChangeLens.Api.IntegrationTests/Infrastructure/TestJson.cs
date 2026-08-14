using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChangeLens.Api.IntegrationTests.Infrastructure;

/// <summary>
/// JSON options that mirror the API's wire format: camelCase property names and
/// enums serialized as strings (the API registers JsonStringEnumConverter).
/// The default <c>ReadFromJsonAsync</c> options only accept numeric enums, so tests
/// that read enum-typed fields must use these options.
/// </summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
