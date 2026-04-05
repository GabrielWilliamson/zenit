using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class CustomReportService
{
    private readonly ReportTemplateService _templateService;
    private readonly ReportColumnService _columnService;
    private readonly ReportExecutionService _executionService;

    public CustomReportService(
        ReportTemplateService templateService,
        ReportColumnService columnService,
        ReportExecutionService executionService)
    {
        _templateService = templateService;
        _columnService = columnService;
        _executionService = executionService;
    }

    public IReadOnlyList<ReportTypeDefinition> GetReportTypes()
        => _columnService.GetReportTypes();

    public IReadOnlyList<ReportColumnDefinition> GetColumnCatalog(string reportTypeKey)
        => _columnService.GetColumnCatalog(reportTypeKey);

    public Task<IReadOnlyList<ReportTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        => _templateService.GetAllAsync(cancellationToken);

    public Task<ReportTemplate> SaveTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default)
        => _templateService.SaveAsync(template, cancellationToken);

    public Task<ReportTemplate> DuplicateTemplateAsync(Guid templateId, string? newName = null, CancellationToken cancellationToken = default)
        => _templateService.DuplicateAsync(templateId, newName, cancellationToken);

    public Task DeleteTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
        => _templateService.DeleteAsync(templateId, cancellationToken);

    public Task<ReportExecutionResult> ExecuteAsync(ReportExecutionRequest request, CancellationToken cancellationToken = default)
        => _executionService.ExecuteAsync(request, cancellationToken);
}
