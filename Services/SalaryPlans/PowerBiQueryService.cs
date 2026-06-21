using Zenit.Infrastructure.PowerBi.Queries;

namespace Zenit.Services.SalaryPlans;

public sealed class PowerBiQueryService : IPowerBiQueryService
{
    private readonly ExecuteQueryService _executeQueryService;

    public PowerBiQueryService(ExecuteQueryService executeQueryService)
    {
        _executeQueryService = executeQueryService;
    }

    public Task<string> ExecuteAsync(string datasetId, string dax)
        => _executeQueryService.ExecuteAsync(datasetId, dax);
}
