using Dapper;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedArquivoRepository
{
    Task<int> InsertAsync(SpedArquivo spedArquivo, CancellationToken cancellationToken = default);
    Task<int> InsertAsync(SpedArquivo spedArquivo, string tipoArquivo, CancellationToken cancellationToken = default);
    Task<bool> ExistsByHashAsync(string hashArquivo, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCnpjCompetenciaAsync(string cnpj, int ano, int mes, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCnpjCompetenciaTipoAsync(string cnpj, int ano, int mes, string tipoArquivo, CancellationToken cancellationToken = default);
    Task<int> DeleteByEmpresaCompetenciaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<int> DeleteByEmpresaCompetenciaTipoAsync(int empresaId, int ano, int mes, string tipoArquivo,
        CancellationToken cancellationToken = default);
}

public sealed class SpedArquivoRepository : RepositoryBase, ISpedArquivoRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SpedArquivoRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<int> InsertAsync(SpedArquivo spedArquivo, CancellationToken cancellationToken = default)
    {
        return InsertAsync(spedArquivo, "ICMS_IPI", cancellationToken);
    }

    public Task<int> InsertAsync(SpedArquivo spedArquivo, string tipoArquivo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_ARQUIVO (ID_EMPRESA, TIPO_ARQUIVO, DT_INI, DT_FIN, NOME_ARQUIVO, HASH_ARQUIVO, DATA_IMPORTACAO, STATUS_IMPORTACAO)
            OUTPUT INSERTED.ID_ARQUIVO
            VALUES (@EmpresaId, @TipoArquivo, @PeriodoInicial, @PeriodoFinal, @NomeArquivo, @HashArquivo, @ImportadoEm, 'IMPORTADO');
            """;

        return InsertAsync(sql, new
        {
            spedArquivo.EmpresaId,
            spedArquivo.PeriodoInicial,
            spedArquivo.PeriodoFinal,
            spedArquivo.NomeArquivo,
            spedArquivo.HashArquivo,
            spedArquivo.ImportadoEm,
            TipoArquivo = tipoArquivo
        }, cancellationToken);
    }

    public Task<bool> ExistsByHashAsync(string hashArquivo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM SPED_ARQUIVO WITH (NOLOCK)
                WHERE HASH_ARQUIVO = @HashArquivo
            ) THEN 1 ELSE 0 END AS bit);
            """;

        return ExistsAsync(sql, new { HashArquivo = hashArquivo }, cancellationToken);
    }

    public Task<bool> ExistsByCnpjCompetenciaAsync(string cnpj, int ano, int mes, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM SPED_ARQUIVO a WITH (NOLOCK)
                INNER JOIN EMPRESA e WITH (NOLOCK) ON e.ID_EMPRESA = a.ID_EMPRESA
                WHERE e.CNPJ = @Cnpj
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = @Mes
            ) THEN 1 ELSE 0 END AS bit);
            """;

        return ExistsAsync(sql, new { Cnpj = cnpj, Ano = ano, Mes = mes }, cancellationToken);
    }

    public Task<bool> ExistsByCnpjCompetenciaTipoAsync(string cnpj, int ano, int mes, string tipoArquivo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM SPED_ARQUIVO a WITH (NOLOCK)
                INNER JOIN EMPRESA e WITH (NOLOCK) ON e.ID_EMPRESA = a.ID_EMPRESA
                WHERE e.CNPJ = @Cnpj
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = @Mes
                  AND a.TIPO_ARQUIVO = @TipoArquivo
            ) THEN 1 ELSE 0 END AS bit);
            """;

        return ExistsAsync(sql, new { Cnpj = cnpj, Ano = ano, Mes = mes, TipoArquivo = tipoArquivo }, cancellationToken);
    }

    public async Task<int> DeleteByEmpresaCompetenciaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
        => await DeleteByEmpresaCompetenciaTipoInternoAsync(empresaId, ano, mes, null, cancellationToken);

    public async Task<int> DeleteByEmpresaCompetenciaTipoAsync(int empresaId, int ano, int mes, string tipoArquivo,
        CancellationToken cancellationToken = default)
        => await DeleteByEmpresaCompetenciaTipoInternoAsync(empresaId, ano, mes, tipoArquivo, cancellationToken);

    private async Task<int> DeleteByEmpresaCompetenciaTipoInternoAsync(int empresaId, int ano, int mes,
        string? tipoArquivo, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Arquivos TABLE (ID_ARQUIVO INT PRIMARY KEY);
            DECLARE @C100 TABLE (ID_C100 INT PRIMARY KEY);
            DECLARE @E110 TABLE (ID_E110 INT PRIMARY KEY);
            DECLARE @ContribC100 TABLE (ID_CONTRIB_C100 INT PRIMARY KEY);
            DECLARE @ContribA100 TABLE (ID_CONTRIB_A100 INT PRIMARY KEY);

            INSERT INTO @Arquivos (ID_ARQUIVO)
            SELECT ID_ARQUIVO
            FROM SPED_ARQUIVO
            WHERE ID_EMPRESA = @EmpresaId
              AND YEAR(DT_INI) = @Ano
              AND MONTH(DT_INI) = @Mes
              AND (@TipoArquivo IS NULL OR TIPO_ARQUIVO = @TipoArquivo);

            INSERT INTO @C100 (ID_C100)
            SELECT ID_C100
            FROM SPED_C100
            WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);

            INSERT INTO @E110 (ID_E110)
            SELECT ID_E110
            FROM SPED_E110
            WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);

            INSERT INTO @ContribC100 SELECT ID_CONTRIB_C100 FROM EFD_CONTRIB_C100
            WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            INSERT INTO @ContribA100 SELECT ID_CONTRIB_A100 FROM EFD_CONTRIB_A100
            WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);

            DELETE FROM EFD_CONTRIB_C170 WHERE ID_CONTRIB_C100 IN (SELECT ID_CONTRIB_C100 FROM @ContribC100);
            DELETE FROM EFD_CONTRIB_C175 WHERE ID_CONTRIB_C100 IN (SELECT ID_CONTRIB_C100 FROM @ContribC100);
            DELETE FROM EFD_CONTRIB_A170 WHERE ID_CONTRIB_A100 IN (SELECT ID_CONTRIB_A100 FROM @ContribA100);
            DELETE FROM EFD_CONTRIB_RESUMO_CST WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            DELETE FROM EFD_CONTRIB_M600 WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            DELETE FROM EFD_CONTRIB_M500 WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            DELETE FROM EFD_CONTRIB_M200 WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            DELETE FROM EFD_CONTRIB_M100 WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            DELETE FROM EFD_CONTRIB_A100 WHERE ID_CONTRIB_A100 IN (SELECT ID_CONTRIB_A100 FROM @ContribA100);
            DELETE FROM EFD_CONTRIB_C100 WHERE ID_CONTRIB_C100 IN (SELECT ID_CONTRIB_C100 FROM @ContribC100);

            DELETE FROM SPED_E111 WHERE ID_E110 IN (SELECT ID_E110 FROM @E110);
            DELETE FROM SPED_E110 WHERE ID_E110 IN (SELECT ID_E110 FROM @E110);
            DELETE FROM SPED_C190 WHERE ID_C100 IN (SELECT ID_C100 FROM @C100);
            DELETE FROM SPED_C170 WHERE ID_C100 IN (SELECT ID_C100 FROM @C100);
            DELETE FROM SPED_C100 WHERE ID_C100 IN (SELECT ID_C100 FROM @C100);

            DELETE FROM SPED_ARQUIVO
            OUTPUT DELETED.ID_ARQUIVO
            WHERE ID_ARQUIVO IN (SELECT ID_ARQUIVO FROM @Arquivos);
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var ids = await connection.QueryAsync<int>(
                new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes, TipoArquivo = tipoArquivo }, transaction, commandTimeout: 30, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return ids.Count();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
