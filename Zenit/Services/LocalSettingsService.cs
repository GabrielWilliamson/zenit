using Zenit.Contracts.Services;
using Zenit.Core.Contracts.Services;
using Zenit.Core.Helpers;
using Zenit.Models;

namespace Zenit.Services;

public sealed class LocalSettingsService : ILocalSettingsService
{
    private const string DefaultApplicationDataFolder = "Zenit/ApplicationData";
    private const string DefaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly string _applicationDataFolder;
    private readonly string _localSettingsFile;
    private IDictionary<string, object> _settings = new Dictionary<string, object>();
    private bool _isInitialized;

    public LocalSettingsService(
        IFileService fileService,
        LocalSettingsOptions? options = null)
    {
        _fileService = fileService;
        options ??= new LocalSettingsOptions();

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _applicationDataFolder = Path.Combine(root, options.ApplicationDataFolder ?? DefaultApplicationDataFolder);
        _localSettingsFile = options.LocalSettingsFile ?? DefaultLocalSettingsFile;
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        await InitializeAsync();

        if (_settings.TryGetValue(key, out var obj) && obj is string rawJson)
            return await Json.ToObjectAsync<T>(rawJson);

        return default;
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
        await InitializeAsync();
        _settings[key] = await Json.StringifyAsync(value);
        await Task.Run(() => _fileService.Save(_applicationDataFolder, _localSettingsFile, _settings));
    }

    private async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _settings = await Task.Run(() =>
                        _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, _localSettingsFile))
                    ?? new Dictionary<string, object>();
        _isInitialized = true;
    }
}
