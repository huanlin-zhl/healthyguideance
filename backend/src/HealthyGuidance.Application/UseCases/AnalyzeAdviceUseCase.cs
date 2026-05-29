using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Contracts.Requests;
using HealthyGuidance.Contracts.Responses;
using Microsoft.Extensions.Logging;

namespace HealthyGuidance.Application.UseCases;

public sealed class AnalyzeAdviceUseCase(
    IAdvisor advisor,
    ILogger<AnalyzeAdviceUseCase> logger)
{
    public async Task<AnalyzeAdviceResponse> ExecuteAsync(AnalyzeAdviceRequest request, CancellationToken cancellationToken)
    {
        if (request.BodyMetrics.Count == 0)
        {
            throw new ArgumentException("bodyMetrics must contain at least one sample", nameof(request));
        }

        logger.LogInformation(
            "Analyzing advice bodyMetrics={Bm} workouts={W} meals={M}",
            request.BodyMetrics.Count, request.Workouts.Count, request.Meals.Count);

        var report = await advisor.AnalyzeAsync(
            request.BodyMetrics, request.Workouts, request.Meals, cancellationToken);

        return new AnalyzeAdviceResponse(
            report.Summary,
            new TrendAnalysisDto(report.TrendAnalysis.WeightChangeKg, report.TrendAnalysis.BodyFatChangePercent, report.TrendAnalysis.Verdict),
            report.Validation,
            report.Suggestions,
            report.Warnings,
            new LlmModelInfo(advisor.ModelName));
    }
}
