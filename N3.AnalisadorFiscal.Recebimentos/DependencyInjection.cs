using Microsoft.Extensions.DependencyInjection;
using N3.AnalisadorFiscal.Recebimentos.Services;

namespace N3.AnalisadorFiscal.Recebimentos;

public static class DependencyInjection
{
    public static IServiceCollection AddRecebimentosServices(this IServiceCollection services)
    {
        services.AddScoped<RecebimentosExcelParser>();
        services.AddScoped<IRecebimentosExcelImportService, RecebimentosExcelImportService>();
        return services;
    }
}
