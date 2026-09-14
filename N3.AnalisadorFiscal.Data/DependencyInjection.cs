using Microsoft.Extensions.DependencyInjection;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IEmpresaRepository, EmpresaRepository>();
        services.AddScoped<ISpedArquivoRepository, SpedArquivoRepository>();
        services.AddScoped<IParticipanteRepository, ParticipanteRepository>();
        services.AddScoped<IUnidadeMedidaRepository, UnidadeMedidaRepository>();
        services.AddScoped<IProdutoRepository, ProdutoRepository>();
        services.AddScoped<ISpedC100Repository, SpedC100Repository>();
        services.AddScoped<ISpedC170Repository, SpedC170Repository>();
        services.AddScoped<ISpedC190Repository, SpedC190Repository>();
        services.AddScoped<ISpedE110Repository, SpedE110Repository>();
        services.AddScoped<ISpedE111Repository, SpedE111Repository>();
        services.AddScoped<IDashboardFiscalRepository, DashboardFiscalRepository>();
        services.AddScoped<IEfdContribuicoesRepository, EfdContribuicoesRepository>();
        services.AddScoped<IDashboardPisCofinsRepository, DashboardPisCofinsRepository>();
        services.AddScoped<IPreAnaliseSpedRepository, PreAnaliseSpedRepository>();
        services.AddScoped<INfseRepository, NfseRepository>();
        services.AddScoped<IFolhaPagamentoRepository, FolhaPagamentoRepository>();
        services.AddScoped<IRecebimentoRepository, RecebimentoRepository>();
        services.AddScoped<IFaqRepository, FaqRepository>();

        return services;
    }
}

