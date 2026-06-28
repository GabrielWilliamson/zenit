using System.Text.Json;

namespace Zenit.Properties;

public sealed class Settings
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string SettingsFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zenit");

    private static readonly string SettingsFilePath = Path.Combine(
        SettingsFolderPath,
        "settings.default.json");

    public static Settings Default { get; } = Load();

    public string ConnectionString { get; set; } = string.Empty;
    public string PowerBiTenantId { get; set; } = string.Empty;
    public string PowerBiClientId { get; set; } = string.Empty;
    public string PowerBiCodVendColumn { get; set; } = "VENDEDORES[COD_VEND]";
    public string PowerBiGrupoColumn { get; set; } = "VENDEDORES[GRUPO]";
    public string PowerBiNomVenColumn { get; set; } = "VENDEDORES[NOMVEN]";
    public string PowerBiRutaColumn { get; set; } = "VENDEDORES[COD_RUTA]";
    public string PowerBiSubgrupoColumn { get; set; } = "VENDEDORES[SUBGRUPO]";
    public string PowerBiSubzonaColumn { get; set; } = "VENDEDORES[SUB_GRUPO2]";
    public string PowerBiReportesColumn { get; set; } = "REPORTES[REPORTE]";
    public string WhatsAppApiBaseUrl { get; set; } = string.Empty;
    public string WhatsAppSendMessagePath { get; set; } = "/api/whatsapp/messages/send";
    public string WhatsAppSendFilePath { get; set; } = "/api/whatsapp/files/send";
    public string AppUpdateFeedUrl { get; set; } = string.Empty;
    public string AppUpdateAccessToken { get; set; } = string.Empty;

    public bool HasRequiredSecrets => TryValidateRequiredSecrets(out _);

    public void Save()
    {
        lock (Sync)
        {
            ApplyDefaults();
            Directory.CreateDirectory(SettingsFolderPath);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
    }

    public bool TryValidateRequiredSecrets(out string errorMessage)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ConnectionString))
            missing.Add("Connection String");

        if (string.IsNullOrWhiteSpace(PowerBiTenantId))
            missing.Add("Power BI TenantId");

        if (string.IsNullOrWhiteSpace(PowerBiClientId))
            missing.Add("Power BI ClientId");

        if (string.IsNullOrWhiteSpace(WhatsAppApiBaseUrl))
            missing.Add("WhatsApp ApiBaseUrl");

        if (missing.Count == 0)
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Completa los campos requeridos: {string.Join(", ", missing)}.";
        return false;
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.ApplyDefaults();
                    return loaded;
                }
            }
        }
        catch
        {
            // Si el archivo no se puede leer/parsing falla, se regresa a valores por defecto.
        }

        var defaults = new Settings();
        defaults.ApplyDefaults();
        return defaults;
    }

    private void ApplyDefaults()
    {
        PowerBiCodVendColumn = NormalizeOrDefault(PowerBiCodVendColumn, "VENDEDORES[COD_VEND]");
        PowerBiGrupoColumn = NormalizeOrDefault(PowerBiGrupoColumn, "VENDEDORES[GRUPO]");
        PowerBiNomVenColumn = NormalizeOrDefault(PowerBiNomVenColumn, "VENDEDORES[NOMVEN]");
        PowerBiRutaColumn = NormalizeOrDefault(PowerBiRutaColumn, "VENDEDORES[COD_RUTA]");
        PowerBiSubgrupoColumn = NormalizeOrDefault(PowerBiSubgrupoColumn, "VENDEDORES[SUBGRUPO]");
        PowerBiSubzonaColumn = NormalizeOrDefault(PowerBiSubzonaColumn, "VENDEDORES[SUB_GRUPO2]");
        PowerBiReportesColumn = NormalizeOrDefault(PowerBiReportesColumn, "REPORTES[REPORTE]");
        WhatsAppSendMessagePath = NormalizeOrDefault(WhatsAppSendMessagePath, "/api/whatsapp/messages/send");
        WhatsAppSendFilePath = NormalizeOrDefault(WhatsAppSendFilePath, "/api/whatsapp/files/send");

        ConnectionString = ConnectionString.Trim();
        PowerBiTenantId = PowerBiTenantId.Trim();
        PowerBiClientId = PowerBiClientId.Trim();
        WhatsAppApiBaseUrl = WhatsAppApiBaseUrl.Trim();
        AppUpdateFeedUrl = AppUpdateFeedUrl.Trim();
        AppUpdateAccessToken = AppUpdateAccessToken.Trim();
    }

    private static string NormalizeOrDefault(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
