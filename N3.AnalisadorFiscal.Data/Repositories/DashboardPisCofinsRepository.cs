using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IDashboardPisCofinsRepository
{
    Task<IReadOnlyList<DashboardPisCofinsMesDto>> GetMesesImportadosAsync(
        int empresaId, int ano, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardPisCofinsCstDto>> GetResumoCstAsync(
        int empresaId, int ano, int mes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardPisCofinsCfopCstDto>> GetResumoCfopCstAsync(
        int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
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

    public async Task<IReadOnlyList<DashboardPisCofinsCstDto>> GetResumoCstAsync(
        int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH Itens AS (
                SELECT COALESCE(doc.IND_OPER, '') AS IndicadorOperacao,
                       COALESCE(item.CST_PIS, '') AS CstPis,
                       COALESCE(item.CST_COFINS, '') AS CstCofins,
                       item.VL_ITEM - item.VL_DESC AS ValorOperacao,
                       item.VL_BC_PIS AS BasePis, item.VL_PIS AS ValorPis,
                       item.VL_BC_COFINS AS BaseCofins, item.VL_COFINS AS ValorCofins
                FROM EFD_CONTRIB_C170 item
                INNER JOIN EFD_CONTRIB_C100 doc ON doc.ID_CONTRIB_C100 = item.ID_CONTRIB_C100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
                UNION ALL
                SELECT COALESCE(doc.IND_OPER, ''), COALESCE(item.CST_PIS, ''), COALESCE(item.CST_COFINS, ''),
                       item.VL_OPR - item.VL_DESC, item.VL_BC_PIS, item.VL_PIS, item.VL_BC_COFINS, item.VL_COFINS
                FROM EFD_CONTRIB_C175 item
                INNER JOIN EFD_CONTRIB_C100 doc ON doc.ID_CONTRIB_C100 = item.ID_CONTRIB_C100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
                UNION ALL
                SELECT COALESCE(doc.IND_OPER, ''), COALESCE(item.CST_PIS, ''), COALESCE(item.CST_COFINS, ''),
                       item.VL_ITEM - item.VL_DESC, item.VL_BC_PIS, item.VL_PIS, item.VL_BC_COFINS, item.VL_COFINS
                FROM EFD_CONTRIB_A170 item
                INNER JOIN EFD_CONTRIB_A100 doc ON doc.ID_CONTRIB_A100 = item.ID_CONTRIB_A100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
            )
            SELECT IndicadorOperacao, CstPis, CstCofins,
                   SUM(ValorOperacao) AS ValorOperacao, SUM(BasePis) AS BasePis,
                   SUM(ValorPis) AS ValorPis, SUM(BaseCofins) AS BaseCofins,
                   SUM(ValorCofins) AS ValorCofins
            FROM Itens
            GROUP BY IndicadorOperacao, CstPis, CstCofins
            ORDER BY IndicadorOperacao, CstPis, CstCofins;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DashboardPisCofinsCstDto>(
            new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes },
                commandTimeout: 30, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<DashboardPisCofinsCfopCstDto>> GetResumoCfopCstAsync(
        int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH Itens AS (
                SELECT COALESCE(doc.IND_OPER, '') AS IndicadorOperacao,
                       COALESCE(item.CFOP, '') AS Cfop,
                       COALESCE(item.CST_PIS, '') AS CstPis,
                       COALESCE(item.CST_COFINS, '') AS CstCofins,
                       item.VL_ITEM - item.VL_DESC AS ValorOperacao,
                       item.VL_BC_PIS AS BasePis, item.VL_PIS AS ValorPis,
                       item.VL_BC_COFINS AS BaseCofins, item.VL_COFINS AS ValorCofins
                FROM EFD_CONTRIB_C170 item
                INNER JOIN EFD_CONTRIB_C100 doc ON doc.ID_CONTRIB_C100 = item.ID_CONTRIB_C100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
                UNION ALL
                SELECT COALESCE(doc.IND_OPER, ''), COALESCE(item.CFOP, ''), COALESCE(item.CST_PIS, ''), COALESCE(item.CST_COFINS, ''),
                       item.VL_OPR - item.VL_DESC, item.VL_BC_PIS, item.VL_PIS, item.VL_BC_COFINS, item.VL_COFINS
                FROM EFD_CONTRIB_C175 item
                INNER JOIN EFD_CONTRIB_C100 doc ON doc.ID_CONTRIB_C100 = item.ID_CONTRIB_C100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
                UNION ALL
                SELECT COALESCE(doc.IND_OPER, ''), '', COALESCE(item.CST_PIS, ''), COALESCE(item.CST_COFINS, ''),
                       item.VL_ITEM - item.VL_DESC, item.VL_BC_PIS, item.VL_PIS, item.VL_BC_COFINS, item.VL_COFINS
                FROM EFD_CONTRIB_A170 item
                INNER JOIN EFD_CONTRIB_A100 doc ON doc.ID_CONTRIB_A100 = item.ID_CONTRIB_A100
                INNER JOIN SPED_ARQUIVO arquivo ON arquivo.ID_ARQUIVO = doc.ID_ARQUIVO
                WHERE arquivo.ID_EMPRESA = @EmpresaId AND YEAR(arquivo.DT_INI) = @Ano AND MONTH(arquivo.DT_INI) = @Mes
            )
            SELECT IndicadorOperacao, Cfop, CstPis, CstCofins,
                   SUM(ValorOperacao) AS ValorOperacao, SUM(BasePis) AS BasePis,
                   SUM(ValorPis) AS ValorPis, SUM(BaseCofins) AS BaseCofins,
                   SUM(ValorCofins) AS ValorCofins
            FROM Itens
            GROUP BY IndicadorOperacao, Cfop, CstPis, CstCofins
            ORDER BY IndicadorOperacao, Cfop, CstPis, CstCofins;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DashboardPisCofinsCfopCstDto>(
            new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes },
                commandTimeout: 30, cancellationToken: cancellationToken))).AsList();
    }
}
