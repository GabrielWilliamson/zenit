using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Core.Infrastructure.PowerBi.Dimensions;
using Zenit.Core.Infrastructure.PowerBi.Models;
using Zenit.Core.Infrastructure.PowerBi.Reports;
using Zenit.Core.Infrastructure.WhatsApp;
using Zenit.Helpers;
using Zenit.Infrastructure.Configuration;
using Zenit.Infrastructure.Logging;
using Zenit.Models;
using Zenit.Services;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.IO;

namespace Zenit.ViewModels;

/// <summary>
/// ViewModel de la pantalla "Reports".
///
/// Responsabilidades:
/// - Cargar filtros reales del dataset (COD_VEND / GRUPO) -> dropdowns.
/// - Ejecutar reportes (async + cancelación).
/// - Paginar resultados para que el DataGrid NO congele la UI.
/// </summary>
public partial class ReportsViewModel : ObservableRecipient
{
    private readonly PowerBiReportService _reportService;
    private readonly WhatsAppService _whatsAppService;
    private readonly DimensionValuesService _dimensionValues;
    private readonly PowerBiSelectionState _selectionState;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportsViewModel> _logger;

    // Cancelación: si el usuario vuelve a ejecutar un reporte mientras otro está en curso,
    // cancelamos el anterior para que la UI no se sienta "pegada".
    private CancellationTokenSource? _runCts;

    // Último resultado crudo (sin convertir a DataTable completo) para poder paginar.
    private PowerBiQueryTable? _lastTable;

    // Para no recargar filtros si ya cargamos para el mismo dataset.
    private string? _filtersLoadedForDatasetId;

    // Columnas configurables (vienen de la configuracion de la app).
    // Importante: aquí se usan para cargar los valores (dropdowns).
    private readonly string _codVendColumnRef;
    private readonly string _nomVenColumnRef;
    private readonly string _grupoColumnRef;

    // Mapa COD_VEND -> NOMVEN (para encabezado PDF y display en UI)
    private Dictionary<string, string> _vendorNameByCode = new(StringComparer.OrdinalIgnoreCase);

    // =============================
    // Opciones UI
    // =============================
    public ObservableCollection<ReportOption> ReportOptions { get; } = new();
    public ObservableCollection<MonthOption> Months { get; } = new();
    public ObservableCollection<int> Years { get; } = new();

    // Filtros (cargados desde Power BI)
    public ObservableCollection<FilterOption> CodVendOptions { get; } = new();
    public ObservableCollection<FilterOption> GrupoOptions { get; } = new();

    // Paginación
    public ObservableCollection<int> PageSizes { get; } = new() { 100, 250, 500, 1000, 2000, 5000 };

    [ObservableProperty] private ReportOption? selectedReport;
    [ObservableProperty] private MonthOption? selectedMonth;
    [ObservableProperty] private int selectedYear;

    // Multi-selección de vendedores (0 = Todos)
    public ObservableCollection<FilterOption> SelectedCodVends { get; } = new();

    [ObservableProperty]
    private string codVendSelectionText = "Seleccionar vendedores";

    public void SetSelectedCodVends(List<FilterOption> selected)
    {
        SelectedCodVends.Clear();
        foreach (var it in selected)
            SelectedCodVends.Add(it);

        CodVendSelectionText = SelectedCodVends.Count switch
        {
            0 => "Seleccionar vendedores",
            1 => SelectedCodVends[0].DisplayName,
            _ => $"{SelectedCodVends.Count} vendedores seleccionados"
        };

        OnPropertyChanged(nameof(CanExportPdf));
    }
    [ObservableProperty] private FilterOption? selectedGrupo;

    [ObservableProperty] private bool isGrupoAvailable = true;
    [ObservableProperty] private bool isFiltersLoading;

    [ObservableProperty] private string datasetName = string.Empty;
    [ObservableProperty] private string datasetId = string.Empty;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private IEnumerable? results;
    [ObservableProperty] private DataTable? resultsTable;

    // Paginación (estado)
    [ObservableProperty] private int selectedPageSize = 500;
    [ObservableProperty] private int currentPage = 1; // 1-based para UI
    [ObservableProperty] private int totalPages;
    [ObservableProperty] private int totalRows;

    // Estado UI (InfoBar)
    [ObservableProperty] private bool isStatusOpen;
    [ObservableProperty] private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string statusTitle = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string whatsAppPhoneNumber = string.Empty;
    [ObservableProperty] private string whatsAppMessageText = string.Empty;
    [ObservableProperty] private string whatsAppFilePhoneNumber = string.Empty;
    [ObservableProperty] private string whatsAppFilePath = string.Empty;
    [ObservableProperty] private string whatsAppFileCaption = string.Empty;
    [ObservableProperty] private bool isWhatsAppBusy;
    [ObservableProperty] private string selectedCodVendsCsv = string.Empty;
    [ObservableProperty] private string resultsPreview = "Todavia no hay resultados.";

    private readonly PdfExportService _pdfExport;

    public bool CanRun => !IsBusy
                          && !string.IsNullOrWhiteSpace(DatasetId)
                          && SelectedReport != null
                          && SelectedMonth != null
                          && SelectedYear > 0;

    public bool CanExportPdf => !IsBusy
                               && !string.IsNullOrWhiteSpace(DatasetId)
                               && SelectedMonth != null
                               && SelectedYear > 0
                               && SelectedCodVends.Count > 0;

    public bool CanSendWhatsAppMessage => !IsBusy
                                          && !IsWhatsAppBusy
                                          && !string.IsNullOrWhiteSpace(WhatsAppPhoneNumber)
                                          && !string.IsNullOrWhiteSpace(WhatsAppMessageText);

    public bool CanSendWhatsAppFile => !IsBusy
                                       && !IsWhatsAppBusy
                                       && !string.IsNullOrWhiteSpace(WhatsAppFilePhoneNumber)
                                       && !string.IsNullOrWhiteSpace(WhatsAppFilePath);

    public bool CanGoPrev => _lastTable != null && CurrentPage > 1 && !IsBusy;
    public bool CanGoNext => _lastTable != null && CurrentPage < TotalPages && !IsBusy;

    public string PagingSummary
    {
        get
        {
            if (_lastTable == null || TotalRows == 0)
                return "Sin resultados";

            var start = ((CurrentPage - 1) * SelectedPageSize) + 1;
            var end = Math.Min(CurrentPage * SelectedPageSize, TotalRows);

            return $"Filas: {TotalRows:N0} | Mostrando {start:N0}-{end:N0} | Página {CurrentPage}/{TotalPages}";
        }
    }

    public ReportsViewModel(
        PowerBiReportService reportService,
        WhatsAppService whatsAppService,
        DimensionValuesService dimensionValues,
        PowerBiSelectionState selectionState,
        PowerBiDefaultSelectionService defaultSelectionService,
        IConfiguration configuration,
        PdfExportService pdfExport,
        ILogger<ReportsViewModel> logger)
    {
        _reportService = reportService;
        _whatsAppService = whatsAppService;
        _dimensionValues = dimensionValues;
        _selectionState = selectionState;
        _defaultSelectionService = defaultSelectionService;
        _configuration = configuration;
        _pdfExport = pdfExport;
        _logger = logger;

        _codVendColumnRef = _configuration["PowerBi:Dimensions:CodVendColumn"] ?? "VENDEDORES[COD_VEND]";
        _nomVenColumnRef = _configuration["PowerBi:Dimensions:NomVenColumn"] ?? "VENDEDORES[NOMVEN]";
        _grupoColumnRef = _configuration["PowerBi:Dimensions:GrupoColumn"] ?? "VENDEDORES[GRUPO]";

        // Reportes solicitados
        ReportOptions.Add(new ReportOption { Key = "PLAN_INCENTIVO_KIMBERLY", DisplayName = "Plan Incentivo Kimberly (KC / Mayoristas)" });
        ReportOptions.Add(new ReportOption { Key = "TA_KIMBERLY", DisplayName = "TA Kimberly" });
        ReportOptions.Add(new ReportOption { Key = "FOCOS", DisplayName = "Focos" });

        // Reportes adicionales
        ReportOptions.Add(new ReportOption { Key = "BIC_CATEGORIAS", DisplayName = "BIC Categorías" });
        ReportOptions.Add(new ReportOption { Key = "SEG_BAYER", DisplayName = "Bayer" });
        ReportOptions.Add(new ReportOption { Key = "SOL_CATEGORIAS", DisplayName = "Sol Maya Categorías" });

        // Meses
        Months.Add(new MonthOption { Value = 1, Name = "Enero" });
        Months.Add(new MonthOption { Value = 2, Name = "Febrero" });
        Months.Add(new MonthOption { Value = 3, Name = "Marzo" });
        Months.Add(new MonthOption { Value = 4, Name = "Abril" });
        Months.Add(new MonthOption { Value = 5, Name = "Mayo" });
        Months.Add(new MonthOption { Value = 6, Name = "Junio" });
        Months.Add(new MonthOption { Value = 7, Name = "Julio" });
        Months.Add(new MonthOption { Value = 8, Name = "Agosto" });
        Months.Add(new MonthOption { Value = 9, Name = "Septiembre" });
        Months.Add(new MonthOption { Value = 10, Name = "Octubre" });
        Months.Add(new MonthOption { Value = 11, Name = "Noviembre" });
        Months.Add(new MonthOption { Value = 12, Name = "Diciembre" });

        var now = DateTime.Now;
        SelectedYear = now.Year;

        // Años razonables para selección (ajusta si lo deseas)
        for (int y = now.Year - 3; y <= now.Year + 1; y++)
            Years.Add(y);

        SelectedMonth = Months[Math.Max(0, now.Month - 1)];
        SelectedReport = ReportOptions.Count > 0 ? ReportOptions[0] : null;

        // Defaults paginación
        SelectedPageSize = 500;

        SelectedCodVends.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanExportPdf));
            RunReportCommand.NotifyCanExecuteChanged();
            ExportPdfCommand.NotifyCanExecuteChanged();
        };

        NotifyWhatsAppCanExecuteChanged();
    }

    /// <summary>
    /// Llamar cuando la página cargue (OnLoaded).
    /// Se asegura de: dataset seleccionado + filtros cargados.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: false);
        RefreshDatasetSelection();

        if (!string.IsNullOrWhiteSpace(DatasetId))
        {
            await LoadFiltersIfNeededAsync();
        }
    }

    public void RefreshDatasetSelection()
    {
        if (_selectionState.SelectedDataset == null)
        {
            DatasetName = string.Empty;
            DatasetId = string.Empty;

            ShowInfo(
                "Dataset no seleccionado",
                "Configura DefaultWorkspaceId y DefaultDatasetId en Settings antes de ejecutar reportes.",
                InfoBarSeverity.Warning);

            return;
        }

        DatasetName = _selectionState.SelectedDataset.Name;
        DatasetId = _selectionState.SelectedDataset.Id;

        // Limpia mensaje si estaba en warning por dataset
        if (StatusSeverity == InfoBarSeverity.Warning && StatusTitle.Contains("Dataset", StringComparison.OrdinalIgnoreCase))
            IsStatusOpen = false;
    }

    // =============================
    // Carga de filtros (COD_VEND / GRUPO) desde Power BI
    // =============================

    [RelayCommand]
    private async Task ReloadFiltersAsync()
    {
        _filtersLoadedForDatasetId = null;
        await LoadFiltersIfNeededAsync();
    }

    private async Task LoadFiltersIfNeededAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        if (_filtersLoadedForDatasetId == DatasetId && CodVendOptions.Count > 0)
            return;

        IsFiltersLoading = true;
        try
        {
            CodVendOptions.Clear();
            GrupoOptions.Clear();

            // GRUPO mantiene "Todos" (single select). COD_VEND es multi-select: 0 seleccionados = Todos.
            GrupoOptions.Add(new FilterOption { DisplayName = "Todos", Value = null });

            // COD_VEND
            var vendors = await _dimensionValues.GetDistinctValuesAsync(DatasetId, _codVendColumnRef);

            // Intentamos mapear COD_VEND -> NOMVEN para mejorar UX.
            // Si falla (columna no existe), hacemos fallback a solo código.
            _vendorNameByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var map = await _dimensionValues.GetVendorNameMapAsync(DatasetId, _codVendColumnRef, _nomVenColumnRef);
                _vendorNameByCode = map.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo cargar NOMVEN para COD_VEND. Se usará solo código.");
            }

            foreach (var v in vendors)
            {
                var code = v.Display;
                if (_vendorNameByCode.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name))
                    CodVendOptions.Add(new FilterOption { DisplayName = $"{code} - {name}", Value = v });
                else
                    CodVendOptions.Add(new FilterOption { DisplayName = code, Value = v });
            }

            // Deja la selección de vendedores vacía (0 = Todos)
            SelectedCodVends.Clear();
            if (!string.IsNullOrWhiteSpace(SelectedCodVendsCsv))
                ApplySelectedVendorsFromCsv(SelectedCodVendsCsv);

            // GRUPO (si existe en el modelo)
            try
            {
                IsGrupoAvailable = true;
                var grupos = await _dimensionValues.GetDistinctValuesAsync(DatasetId, _grupoColumnRef);
                foreach (var g in grupos)
                    GrupoOptions.Add(new FilterOption { DisplayName = g.Display, Value = g });

                SelectedGrupo = GrupoOptions[0]; // Todos
            }
            catch (Exception ex)
            {
                // Si el modelo no tiene la columna o da error, deshabilitamos el filtro sin romper la app.
                IsGrupoAvailable = false;
                SelectedGrupo = GrupoOptions[0];
                _logger.LogWarning(ex, "No se pudo cargar GRUPO desde columna {GrupoColumn}", _grupoColumnRef);
            }

            _filtersLoadedForDatasetId = DatasetId;

            ShowInfo("Filtros cargados", "COD_VEND y GRUPO se cargaron desde Power BI (sin inventar tipos).", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando filtros (COD_VEND / GRUPO)");
            ShowInfo("Error cargando filtros", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsFiltersLoading = false;
        }
    }

    // =============================
    // Ejecutar reportes
    // =============================

    public async Task RunSelectedReportAsync()
    {
        RefreshDatasetSelection();

        if (string.IsNullOrWhiteSpace(DatasetId) || SelectedReport == null || SelectedMonth == null)
            return;

        // Cancela cualquier ejecución anterior (por seguridad).
        CancelInFlightRun();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));

            // Si por alguna razón los filtros no están cargados, los cargamos.
            await LoadFiltersIfNeededAsync();

            var year = SelectedYear;
            var month = SelectedMonth.Value;

            // Si el usuario seleccionó 1 vendedor, filtramos por ese. Si seleccionó 0 o varios,
            // ejecutamos "Todos" (la API de reportes actual filtra por 1 valor o null).
            var vendor = SelectedCodVends.Count == 1 ? SelectedCodVends[0].Value : null;
            var grupo = (IsGrupoAvailable ? SelectedGrupo?.Value : null);

            // Kimberly: si es ruta mayorista, usamos el query de mayoristas (MD_COR/CORDOBAS)
            var mayoristas = IsMayoristaRoute(vendor);

            // Ejecuta según el reporte seleccionado
            _lastTable = await GetReportTableAsync(SelectedReport.Key, DatasetId, year, month, vendor, grupo, mayoristas, ct);

            // Set paginación
            TotalRows = _lastTable.Rows.Count;
            TotalPages = TotalRows == 0 ? 0 : (int)Math.Ceiling(TotalRows / (double)SelectedPageSize);
            CurrentPage = TotalRows == 0 ? 0 : 1;

            await LoadCurrentPageAsync(ct);

            var extra = mayoristas ? " (Mayoristas detectado)" : string.Empty;
            ShowInfo("Reporte generado", $"Filtrado por {SelectedMonth.Name} {SelectedYear}.{extra}", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Cancelado", "La ejecución del reporte fue cancelada.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando reporte {ReportKey}", SelectedReport?.Key);
            ShowInfo("Error", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));

            CancelInFlightRun();
        }
    }

    public async Task ExportSelectedVendorsToPdfAsync()
    {
        RefreshDatasetSelection();

        if (string.IsNullOrWhiteSpace(DatasetId) || SelectedMonth == null)
            return;

        if (SelectedCodVends.Count == 0)
        {
            ShowInfo(
                "Selecciona vendedores",
                "Para exportar a PDF, selecciona 1 o más COD_VEND. (Se generará 1 PDF por vendedor).",
                InfoBarSeverity.Warning);
            return;
        }

        CancelInFlightRun();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanExportPdf));

            await LoadFiltersIfNeededAsync();

            var year = SelectedYear;
            var month = SelectedMonth.Value;
            var grupo = (IsGrupoAvailable ? SelectedGrupo?.Value : null);

            var outputs = new List<string>();

            foreach (var vendOpt in SelectedCodVends)
            {
                ct.ThrowIfCancellationRequested();

                var vendor = vendOpt.Value;
                var mayoristas = IsMayoristaRoute(vendor);

                var vendorCodeDisplay = vendor?.Display ?? vendOpt.DisplayName;
                var vendorName = ResolveVendorName(vendorCodeDisplay, vendOpt.DisplayName);

                // Genera TODOS los reportes en UN SOLO PDF por vendedor
                var sections = new List<(string ReportTitle, PowerBiQueryTable Table)>();
                foreach (var rep in ReportOptions)
                {
                    ct.ThrowIfCancellationRequested();
                    var table = await GetReportTableAsync(rep.Key, DatasetId, year, month, vendor, grupo, mayoristas, ct);
                    sections.Add((rep.DisplayName, table));
                }

                var filePath = _pdfExport.ExportMultiplePowerBiTablesToDownloads(
                    sections,
                    combinedTitle: "Reportes",
                    datasetName: DatasetName,
                    monthName: SelectedMonth.Name,
                    year: year,
                    vendorCode: vendorCodeDisplay,
                    vendorName: vendorName,
                    grupoDisplay: SelectedGrupo?.DisplayName);

                outputs.Add(filePath);
            }

            ShowInfo(
                "PDF generado",
                $"Se generaron {outputs.Count} PDF en Descargas.\n" + string.Join("\n", outputs.Select(Path.GetFileName)),
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Cancelado", "La exportación a PDF fue cancelada.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exportando PDF");
            ShowInfo("Error", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanExportPdf));

            CancelInFlightRun();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunReportAsync() => RunSelectedReportAsync();

    [RelayCommand(CanExecute = nameof(CanExportPdf))]
    private Task ExportPdfAsync() => ExportSelectedVendorsToPdfAsync();

    [RelayCommand(CanExecute = nameof(CanSendWhatsAppMessage))]
    private async Task SendWhatsAppMessageAsync()
    {
        try
        {
            IsWhatsAppBusy = true;
            await _whatsAppService.SendMessageAsync(WhatsAppPhoneNumber, WhatsAppMessageText);

            ShowInfo(
                "Mensaje enviado",
                $"WhatsApp enviado a {WhatsAppPhoneNumber.Trim()}.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando mensaje WhatsApp");
            ShowInfo("Error WhatsApp", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsWhatsAppBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendWhatsAppFile))]
    private async Task SendWhatsAppFileAsync()
    {
        try
        {
            IsWhatsAppBusy = true;
            await _whatsAppService.SendFileAsync(WhatsAppFilePhoneNumber, WhatsAppFilePath, WhatsAppFileCaption);

            ShowInfo(
                "Archivo enviado",
                $"Archivo enviado por WhatsApp a {WhatsAppFilePhoneNumber.Trim()}.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando archivo WhatsApp");
            ShowInfo("Error WhatsApp", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsWhatsAppBusy = false;
        }
    }

    private string? ResolveVendorName(string vendorCodeDisplay, string displayName)
    {
        // 1) Mapa desde dataset
        if (_vendorNameByCode.TryGetValue(vendorCodeDisplay, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        // 2) Si DisplayName viene como "021 - JOSE ..."
        var idx = displayName.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0 && idx + 3 < displayName.Length)
            return displayName[(idx + 3)..].Trim();

        return null;
    }

    private async Task<PowerBiQueryTable> GetReportTableAsync(
        string reportKey,
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend,
        DaxFilterValue? grupo,
        bool mayoristas,
        CancellationToken cancellationToken)
    {
        return reportKey switch
        {
            "PLAN_INCENTIVO_KIMBERLY" => await _reportService.GetPlanIncentivoKimberlyAsync(
                datasetId,
                year,
                month,
                codVend,
                grupo,
                mayoristas: mayoristas,
                cancellationToken: cancellationToken
            ),

            "TA_KIMBERLY" => await _reportService.GetTaKimberlyAsync(datasetId, year, month, codVend, grupo, cancellationToken),

            "FOCOS" => await _reportService.GetFocosAsync(datasetId, year, month, codVend, grupo, cancellationToken),

            "BIC_CATEGORIAS" => await _reportService.GetBicCategoriasAsync(datasetId, year, month, codVend, grupo, cancellationToken),
            "SEG_BAYER" => await _reportService.GetSegBayerAsync(datasetId, year, month, codVend, grupo, cancellationToken),
            "SOL_CATEGORIAS" => await _reportService.GetSolCategoriasAsync(datasetId, year, month, codVend, grupo, cancellationToken),

            _ => throw new InvalidOperationException("Reporte no soportado")
        };
    }

    /// <summary>
    /// Detecta si el COD_VEND corresponde a rutas mayoristas.
    ///
    /// Importante: el valor puede venir como:
    /// - string "018"
    /// - número 18
    /// y ambos deben funcionar igual.
    /// </summary>
    private static bool IsMayoristaRoute(DaxFilterValue? codVend)
    {
        if (codVend is null) return false;

        var normalized = (codVend.Display ?? string.Empty).Trim().TrimStart('0');
        return normalized is "18" or "21" or "31";
    }

    // =============================
    // Paginación (evita congelamiento del DataGrid)
    // =============================

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task PrevPageAsync()
    {
        if (_lastTable == null || CurrentPage <= 1) return;
        CurrentPage--;
        await LoadCurrentPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        if (_lastTable == null || CurrentPage >= TotalPages) return;
        CurrentPage++;
        await LoadCurrentPageAsync();
    }

    [RelayCommand]
    private async Task FirstPageAsync()
    {
        if (_lastTable == null || TotalPages <= 0) return;
        CurrentPage = 1;
        await LoadCurrentPageAsync();
    }

    [RelayCommand]
    private async Task LastPageAsync()
    {
        if (_lastTable == null || TotalPages <= 0) return;
        CurrentPage = TotalPages;
        await LoadCurrentPageAsync();
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        // Cuando cambia el tamaño de página, recalculamos paginación y volvemos a la página 1.
        if (_lastTable == null) return;

        TotalPages = TotalRows == 0 ? 0 : (int)Math.Ceiling(TotalRows / (double)SelectedPageSize);
        CurrentPage = TotalPages > 0 ? 1 : 0;

        _ = LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync(CancellationToken cancellationToken = default)
    {
        if (_lastTable == null || TotalRows == 0 || CurrentPage <= 0)
        {
            ResultsTable = null;
            Results = null;
            ResultsPreview = "Todavia no hay resultados.";
            OnPropertyChanged(nameof(PagingSummary));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            return;
        }

        var skip = (CurrentPage - 1) * SelectedPageSize;
        var take = SelectedPageSize;

        // Construimos SOLO la página actual (DataTable pequeño), así no se pega.
        var dt = await Task.Run(() => PowerBiDataTableMapper.ToDataTable(_lastTable, skip, take), cancellationToken);
        ResultsTable = dt;
        Results = dt.DefaultView;
        ResultsPreview = TabularPreviewFormatter.FromDataTable(dt);

        OnPropertyChanged(nameof(PagingSummary));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    // =============================
    // Helpers UI
    // =============================

    partial void OnSelectedReportChanged(ReportOption? value) => OnPropertyChanged(nameof(CanRun));
    partial void OnSelectedMonthChanged(MonthOption? value) => OnPropertyChanged(nameof(CanRun));
    partial void OnSelectedYearChanged(int value) => OnPropertyChanged(nameof(CanRun));
    partial void OnDatasetIdChanged(string value) => OnPropertyChanged(nameof(CanRun));
    partial void OnWhatsAppPhoneNumberChanged(string value) => NotifyWhatsAppCanExecuteChanged();
    partial void OnWhatsAppMessageTextChanged(string value) => NotifyWhatsAppCanExecuteChanged();
    partial void OnWhatsAppFilePhoneNumberChanged(string value) => NotifyWhatsAppCanExecuteChanged();
    partial void OnWhatsAppFilePathChanged(string value) => NotifyWhatsAppCanExecuteChanged();
    partial void OnIsWhatsAppBusyChanged(bool value) => NotifyWhatsAppCanExecuteChanged();
    partial void OnSelectedCodVendsCsvChanged(string value) => ApplySelectedVendorsFromCsv(value);

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanExportPdf));
        OnPropertyChanged(nameof(CanSendWhatsAppMessage));
        OnPropertyChanged(nameof(CanSendWhatsAppFile));

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        RunReportCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
        NotifyWhatsAppCanExecuteChanged();
    }

    private void NotifyWhatsAppCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanSendWhatsAppMessage));
        OnPropertyChanged(nameof(CanSendWhatsAppFile));
        SendWhatsAppMessageCommand.NotifyCanExecuteChanged();
        SendWhatsAppFileCommand.NotifyCanExecuteChanged();
    }

    private void ApplySelectedVendorsFromCsv(string? value)
    {
        if (CodVendOptions.Count == 0)
            return;

        var tokens = (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Split(" - ", 2, StringSplitOptions.TrimEntries)[0])
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = CodVendOptions
            .Where(option => option.Value != null && tokens.Contains(option.Value.Display, StringComparer.OrdinalIgnoreCase))
            .ToList();

        SetSelectedCodVends(selected);
    }

    private void CancelInFlightRun()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void ShowInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
