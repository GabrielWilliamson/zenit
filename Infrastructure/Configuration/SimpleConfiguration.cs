namespace Zenit.Infrastructure.Configuration;

public interface IConfiguration
{
    string? this[string key] { get; set; }
    IConfigurationSection GetSection(string key);
}

public interface IConfigurationSection : IConfiguration
{
    string Key { get; }
    string Path { get; }
    string? Value { get; set; }
    IEnumerable<IConfigurationSection> GetChildren();
    bool Exists();
}

public sealed class DictionaryConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public DictionaryConfiguration(IDictionary<string, string?> values)
    {
        _values = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IConfigurationSection GetSection(string key)
    {
        return new DictionaryConfigurationSection(_values, key);
    }
}

internal sealed class DictionaryConfigurationSection : IConfigurationSection
{
    private readonly Dictionary<string, string?> _values;

    public DictionaryConfigurationSection(Dictionary<string, string?> values, string path)
    {
        _values = values;
        Path = path;
        Key = path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? path;
    }

    public string Key { get; }
    public string Path { get; }

    public string? Value
    {
        get => this[Path];
        set => this[Path] = value;
    }

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IConfigurationSection GetSection(string key)
    {
        var childPath = string.IsNullOrWhiteSpace(Path) ? key : $"{Path}:{key}";
        return new DictionaryConfigurationSection(_values, childPath);
    }

    public IEnumerable<IConfigurationSection> GetChildren()
    {
        var prefix = string.IsNullOrWhiteSpace(Path) ? string.Empty : $"{Path}:";
        var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _values.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = key[prefix.Length..];
            var firstSegment = remainder.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstSegment))
                continue;

            children.Add(firstSegment);
        }

        return children
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(child => GetSection(child))
            .ToList();
    }

    public bool Exists()
    {
        if (_values.ContainsKey(Path))
            return true;

        var prefix = $"{Path}:";
        return _values.Keys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
