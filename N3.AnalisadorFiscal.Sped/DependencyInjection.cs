using Microsoft.Extensions.DependencyInjection;
using N3.AnalisadorFiscal.Sped.Parsing;
using N3.AnalisadorFiscal.Sped.Services;

namespace N3.AnalisadorFiscal.Sped;

public static class DependencyInjection
{
    public static IServiceCollection AddSpedServices(this IServiceCollection services)
    {
        services.AddScoped<EfdIcmsParser>();
        services.AddScoped<EfdContribuicoesParser>();
        services.AddScoped<IEfdIcmsImportService, EfdIcmsImportService>();
        services.AddScoped<IEfdContribuicoesImportService, EfdContribuicoesImportService>();

        return services;
    }
}
