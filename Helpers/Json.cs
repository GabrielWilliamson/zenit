using System.Text.Json;

namespace Zenit.Helpers;

public static class Json
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T?> ToObjectAsync<T>(string value)
    {
        return await Task.Run<T?>(() =>
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        });
    }

    public static async Task<string> StringifyAsync(object? value)
    {
        return await Task.Run<string>(() =>
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        });
    }
}
