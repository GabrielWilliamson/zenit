using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Zenit.Contracts.Services;
using Zenit.Data;
using Zenit.Infrastructure.Auth;
using Zenit.Infrastructure.Persistence;
using Zenit.Infrastructure.PowerBi.Datasets;
using Zenit.Infrastructure.PowerBi.Dimensions;
using Zenit.Infrastructure.PowerBi.Queries;
using Zenit.Infrastructure.PowerBi.Reports;
using Zenit.Infrastructure.PowerBi.Workspaces;
using Zenit.Infrastructure.WhatsApp;
using Zenit.Services;
using Zenit.Infrastructure.Configuration;
using Zenit.Infrastructure.Logging;
using Zenit.Models;
using Zenit.Properties;
using Zenit.Services.SalaryPlans;
using Zenit.ViewModels;

namespace Zenit.Infrastructure;

public sealed class AppBootstrapper
{
    private readonly DictionaryConfiguration _configuration;
    private readonly IFileService _fileService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly PowerBiSelectionState _selectionState;

    public AppBootstrapper()
    {
        AppSettings = Zenit.Properties.Settings.Default;
        QuestPDF.Settings.License = LicenseType.Community;

        _configuration = new DictionaryConfiguration(BuildConfiguration(AppSettings));
        _fileService = new FileService();
        _localSettingsService = new LocalSettingsService(
            _fileService,
            new LocalSettingsOptions
            {
                ApplicationDataFolder = "Zenit/ApplicationData",
                LocalSettingsFile = "LocalSettings.json"
            });
        _selectionState = new PowerBiSelectionState();

        Home = BuildHomeViewModel();
        Reports = BuildReportsViewModel();
        AdvancedReports = BuildAdvancedReportsViewModel();
        CustomReportRunner = BuildCustomReportRunnerViewModel();
        Vendedores = BuildVendedoresViewModel();
        PowerBiQuery = BuildPowerBiQueryViewModel();
        SettingsViewModel = BuildSettingsViewModel();
        SalaryPlansGeneratorViewModel = BuildSalaryPlansGeneratorViewModel();
    }

    public Zenit.Properties.Settings AppSettings { get; }
    public HomeViewModel Home { get; }
    public ReportsViewModel Reports { get; }
    public AdvancedReportsViewModel AdvancedReports { get; }
    public CustomReportRunnerViewModel CustomReportRunner { get; }
    public VendedoresViewModel Vendedores { get; }
    public PowerBiQueryViewModel PowerBiQuery { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public SalaryPlansGeneratorViewModel SalaryPlansGeneratorViewModel { get; }

    public MainWindowViewModel CreateMainWindowViewModel()
    {
        return new MainWindowViewModel(new[]
        {
            new NavigationItem
            {
                Title = "Token",
                Subtitle = "Autenticacion Power BI",
                ViewModel = Home,
                ActivateAsync = () => Home.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "Reportes",
                Subtitle = "Consulta y exportacion",
                ViewModel = Reports,
                ActivateAsync = () => Reports.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "Plantillas",
                Subtitle = "Builder de reportes",
                ViewModel = AdvancedReports,
                ActivateAsync = () => AdvancedReports.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "Runner",
                Subtitle = "Ejecucion de templates",
                ViewModel = CustomReportRunner,
                ActivateAsync = () => CustomReportRunner.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "Vendedores",
                Subtitle = "Mantenimiento",
                ViewModel = Vendedores,
                ActivateAsync = () => Vendedores.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "DAX",
                Subtitle = "Consulta manual",
                ViewModel = PowerBiQuery,
                ActivateAsync = () => Task.CompletedTask
            },
            new NavigationItem
            {
                Title = "Settings",
                Subtitle = "Secretos y defaults",
                ViewModel = SettingsViewModel,
                ActivateAsync = () => SettingsViewModel.InitializeAsync()
            },
            new NavigationItem
            {
                Title = "Planes de salario",
                Subtitle = "Generar planes de salario",
                ViewModel = SalaryPlansGeneratorViewModel,
                ActivateAsync = () => Task.CompletedTask
            }
        });
    }

    private HomeViewModel BuildHomeViewModel()
    {
        return new HomeViewModel(BuildTokenManager(), BuildDefaultSelectionService());
    }

    private ReportsViewModel BuildReportsViewModel()
    {
        return new ReportsViewModel(
            BuildPowerBiReportService(),
            BuildWhatsAppService(),
            BuildDimensionValuesService(),
            _selectionState,
            BuildDefaultSelectionService(),
            _configuration,
            new PdfExportService(),
            CreateLogger<ReportsViewModel>());
    }

    private AdvancedReportsViewModel BuildAdvancedReportsViewModel()
    {
        return new AdvancedReportsViewModel(
            BuildDynamicReportService(),
            BuildDefaultSelectionService(),
            new ReportTemplateRuleSchemaService(),
            CreateLogger<AdvancedReportsViewModel>());
    }

    private CustomReportRunnerViewModel BuildCustomReportRunnerViewModel()
    {
        return new CustomReportRunnerViewModel(
            BuildDynamicReportService(),
            _selectionState,
            BuildDefaultSelectionService(),
            CreateLogger<CustomReportRunnerViewModel>());
    }

    private VendedoresViewModel BuildVendedoresViewModel()
    {
        return new VendedoresViewModel(
            new VendedoresDataService(CreateDbContext()),
            CreateLogger<VendedoresViewModel>());
    }

    private PowerBiQueryViewModel BuildPowerBiQueryViewModel()
    {
        return new PowerBiQueryViewModel(BuildExecuteQueryService());
    }

    private SalaryPlansGeneratorViewModel BuildSalaryPlansGeneratorViewModel()
    {
        return new SalaryPlansGeneratorViewModel(new PowerBiQueryService(BuildExecuteQueryService()));
    }

    private SettingsViewModel BuildSettingsViewModel()
    {
        return new SettingsViewModel(BuildDefaultSelectionService());
    }

    private DynamicReportService BuildDynamicReportService()
    {
        var reportColumnService = new ReportColumnService();
        var metadataService = BuildReportMetadataService();
        var definitionService = new ReportDefinitionService(
            BuildPowerBiReportService(),
            reportColumnService,
            CreateLogger<ReportDefinitionService>());
        var fieldCatalogService = new ReportFieldCatalogService(
            _configuration,
            new BiModelStructureService(_configuration, CreateLogger<BiModelStructureService>()),
            definitionService,
            metadataService,
            CreateLogger<ReportFieldCatalogService>());
        var templateService = new ReportTemplateService(CreateDbContext());
        var executionService = new ReportExecutionService(
            BuildPowerBiReportService(),
            BuildDefaultSelectionService(),
            metadataService,
            reportColumnService,
            new ReportSortingService(),
            new ReportFormattingService(),
            new ReportRuleEngineService(),
            new ReportAggregationRuleService(),
            new GuidedReportRuleEngineService(),
            CreateLogger<ReportExecutionService>());

        return new DynamicReportService(
            metadataService,
            definitionService,
            fieldCatalogService,
            templateService,
            executionService,
            new ReportTemplateRuleSchemaService(),
            CreateLogger<DynamicReportService>());
    }

    private ReportMetadataService BuildReportMetadataService()
    {
        return new ReportMetadataService(
            BuildDimensionValuesService(),
            BuildDefaultSelectionService(),
            _configuration,
            CreateLogger<ReportMetadataService>());
    }

    private PowerBiDefaultSelectionService BuildDefaultSelectionService()
    {
        return new PowerBiDefaultSelectionService(
            _localSettingsService,
            _selectionState,
            BuildWorkspaceService(),
            BuildDatasetService());
    }

    private WorkspaceService BuildWorkspaceService()
    {
        return new WorkspaceService(CreateHttpClient(TimeSpan.FromSeconds(100)), BuildTokenManager());
    }

    private DatasetService BuildDatasetService()
    {
        return new DatasetService(CreateHttpClient(TimeSpan.FromSeconds(100)), BuildTokenManager());
    }

    private ExecuteQueryService BuildExecuteQueryService()
    {
        return new ExecuteQueryService(CreateHttpClient(TimeSpan.FromSeconds(180)), BuildTokenManager());
    }

    private DimensionValuesService BuildDimensionValuesService()
    {
        return new DimensionValuesService(
            BuildExecuteQueryService(),
            new ExecuteQueriesResponseParser());
    }

    private PowerBiReportService BuildPowerBiReportService()
    {
        return new PowerBiReportService(
            BuildExecuteQueryService(),
            new ExecuteQueriesResponseParser(),
            AppSettings.PowerBiCodVendColumn,
            AppSettings.PowerBiGrupoColumn);
    }

    private WhatsAppService BuildWhatsAppService()
    {
        return new WhatsAppService(
            CreateHttpClient(TimeSpan.FromSeconds(60)),
            AppSettings.WhatsAppApiBaseUrl,
            AppSettings.WhatsAppSendMessagePath,
            AppSettings.WhatsAppSendFilePath);
    }

    private TokenManager BuildTokenManager()
    {
        return new TokenManager(
            new TokenRepository(CreateDbContext()),
            new PowerBiAuthService(AppSettings.PowerBiTenantId, AppSettings.PowerBiClientId));
    }

    private AppDbContext CreateDbContext()
    {
        var connectionString = string.IsNullOrWhiteSpace(AppSettings.ConnectionString)
            ? "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder"
            : AppSettings.ConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        return new HttpClient
        {
            Timeout = timeout
        };
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return new ConsoleLogger<T>();
    }

    private static Dictionary<string, string?> BuildConfiguration(Settings settings)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PowerBi:Dimensions:CodVendColumn"] = settings.PowerBiCodVendColumn,
            ["PowerBi:Dimensions:GrupoColumn"] = settings.PowerBiGrupoColumn,
            ["PowerBi:Dimensions:NomVenColumn"] = settings.PowerBiNomVenColumn,
            ["PowerBi:Dimensions:RutaColumn"] = settings.PowerBiRutaColumn,
            ["PowerBi:Dimensions:SubgrupoColumn"] = settings.PowerBiSubgrupoColumn,
            ["PowerBi:Dimensions:SubzonaColumn"] = settings.PowerBiSubzonaColumn,
            ["PowerBi:Dimensions:ReportesColumn"] = settings.PowerBiReportesColumn
        };
    }
}
