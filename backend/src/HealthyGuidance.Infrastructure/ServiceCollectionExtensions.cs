using HealthyGuidance.Application.Interfaces;
using HealthyGuidance.Infrastructure.Mock;
using Microsoft.Extensions.DependencyInjection;

namespace HealthyGuidance.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHealthyGuidanceInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IScreenshotRecognizer, MockScreenshotRecognizer>();
        services.AddSingleton<IMealRecognizer, MockMealRecognizer>();
        services.AddSingleton<IAdvisor, MockAdvisor>();
        return services;
    }
}
