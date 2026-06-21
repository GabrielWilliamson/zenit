namespace Zenit.Services.SalaryPlans;

public interface IPowerBiQueryService
{
    Task<string> ExecuteAsync(string datasetId, string dax);
}
