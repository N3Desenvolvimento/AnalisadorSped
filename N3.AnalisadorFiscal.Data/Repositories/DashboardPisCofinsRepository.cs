using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IDashboardPisCofinsRepository
{
    Task<IReadOnlyList<DashboardPisCofinsMesDto>> GetMesesImportadosAsync(
        int empresaId, int ano, CancellationToken cancellationToken = default);
}

public sealed class DashboardPisCofinsRepository : IDashboardPisCofinsRepository
{
    private static readonly string[] NomesMeses =
    [
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    ];

    private readonly IDbConnectionFactory _connectionFactory;

    public DashboardPisCofinsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DashboardPisCofinsMesDto>> GetMesesImportadosAsync(
        int empresaId, int ano, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                MONTH(a.DT_INI) AS Mes,
                a.ID_ARQUIVO AS SpedArquivoId,
                a.NOME_ARQUIVO AS NomeArquivo,
                a.DATA_IMPORTACAO AS DataImportacao,
                COALESCE(pis.Debito, 0) AS DebitoPis,
                COALESCE(pis.Credito, 0) AS CreditoPis,
                COALESCE(pis.ValorPagar, 0) AS PisAPagar,
                COALESCE(cofins.Debito, 0) AS DebitoCofins,
                COALESCE(cofins.Credito, 0) AS CreditoCofins,
                COALESCE(cofins.ValorPagar, 0) AS CofinsAPagar
            FROM SPED_ARQUIVO a
            OUTER APPLY (
                SELECT
                    SUM(m.VL_TOT_CONT_NC_PER + m.VL_TOT_CONT_CUM_PER) AS Debito,
                    SUM(m.VL_TOT_CRED_DESC + m.VL_TOT_CRED_DESC_ANT) AS Credito,
                    SUM(m.VL_TOT_CONT_REC) AS ValorPagar
                FROM EFD_CONTRIB_M200 m
                WHERE m.ID_ARQUIVO = a.ID_ARQUIVO
            ) pis
            OUTER APPLY (
                SELECT
                    SUM(m.VL_TOT_CONT_NC_PER + m.VL_TOT_CONT_CUM_PER) AS Debito,
                    SUM(m.VL_TOT_CRED_DESC + m.VL_TOT_CRED_DESC_ANT) AS Credito,
                    SUM(m.VL_TOT_CONT_REC) AS ValorPagar
                FROM EFD_CONTRIB_M600 m
                WHERE m.ID_ARQUIVO = a.ID_ARQUIVO
            ) cofins
            WHERE a.ID_EMPRESA = @EmpresaId
              AND a.TIPO_ARQUIVO = 'EFD_CONTRIBUICOES'
              AND YEAR(a.DT_INI) = @Ano
            ORDER BY MONTH(a.DT_INI) DESC;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        var meses = (await connection.QueryAsync<DashboardPisCofinsMesDto>(
            new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano },
                commandTimeout: 30, cancellationToken: cancellationToken))).AsList();

        foreach (var mes in meses)
        {
            mes.NomeMes = mes.Mes is >= 1 and <= 12 ? NomesMeses[mes.Mes - 1] : mes.Mes.ToString();
        }

        return meses;
    }
}
