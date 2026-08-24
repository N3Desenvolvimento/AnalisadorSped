using Dapper;
using Microsoft.Data.SqlClient;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IRecebimentoRepository
{
    Task<int> SubstituirImportacaoAsync(RecebimentoImportacao importacao, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecebimentoResumoMesDto>> GetResumoMensalAsync(int empresaId, int ano, CancellationToken cancellationToken = default);
    Task<RecebimentoCompetenciaDto> GetCompetenciaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecebimentoDetalheDto>> GetRecebimentosAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecebimentoDocumentoDto>> GetDocumentosAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecebimentoImportacaoResumoDto>> GetImportacoesAsync(int empresaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecebimentoFortesOrigemDto>> GetPreparacaoFortesAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
}

public sealed class RecebimentoRepository(IDbConnectionFactory connectionFactory) : RepositoryBase(connectionFactory), IRecebimentoRepository
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool schemaGarantido;

    public async Task<int> SubstituirImportacaoAsync(RecebimentoImportacao importacao, CancellationToken cancellationToken = default)
    {
        await using var connection = (SqlConnection)ConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await GarantirSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM RECEBIMENTO_IMPORTACAO
                WHERE ID_EMPRESA=@EmpresaId AND COMPETENCIA_ARQUIVO=@CompetenciaArquivo;
                """, new { importacao.EmpresaId, importacao.CompetenciaArquivo }, transaction, cancellationToken: cancellationToken));

            var importacaoId = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO RECEBIMENTO_IMPORTACAO
                    (ID_EMPRESA,COMPETENCIA_ARQUIVO,NOME_ARQUIVO,HASH_ARQUIVO,NOME_ABA,LINHAS_LIDAS,
                     TOTAL_FATURADO_ARQUIVO,TOTAL_RECEBIDO_ARQUIVO,TOTAL_RETENCOES_ARQUIVO,
                     QUANTIDADE_ALERTAS,IMPORTADO_EM)
                OUTPUT INSERTED.ID_RECEBIMENTO_IMPORTACAO
                VALUES
                    (@EmpresaId,@CompetenciaArquivo,@NomeArquivo,@HashArquivo,@NomeAba,@LinhasLidas,
                     @TotalFaturadoArquivo,@TotalRecebidoArquivo,@TotalRetencoesArquivo,
                     @QuantidadeAlertas,@ImportadoEm);
                """, importacao, transaction, cancellationToken: cancellationToken));

            const string documentoSql = """
                INSERT INTO RECEBIMENTO_DOCUMENTO
                    (ID_RECEBIMENTO_IMPORTACAO,CHAVE_NATURAL,CLIENTE,DATA_EMISSAO,VALOR_FATURADO,
                     DESCONTO_INFORMADO,VALOR_ISS,VALOR_PIS,VALOR_COFINS,VALOR_IR,VALOR_CSLL,
                     VALOR_INSS,VALOR_OUTROS,CODIGO_SERVICO,NUMERO_NOTA,INDICADOR_RETENCOES_FEDERAIS,
                     INDICADOR_ISS,LINHA_ORIGEM)
                VALUES
                    (@ImportacaoId,@ChaveNatural,@Cliente,@DataEmissao,@ValorFaturado,
                     @DescontoInformado,@ValorIss,@ValorPis,@ValorCofins,@ValorIr,@ValorCsll,
                     @ValorInss,@ValorOutros,@CodigoServico,@NumeroNota,@IndicadorRetencoesFederais,
                     @IndicadorIss,@LinhaOrigem);
                """;
            foreach (var documento in importacao.Documentos)
                await connection.ExecuteAsync(new CommandDefinition(documentoSql, new
                {
                    ImportacaoId = importacaoId,
                    documento.ChaveNatural,
                    documento.Cliente,
                    documento.DataEmissao,
                    documento.ValorFaturado,
                    documento.DescontoInformado,
                    documento.ValorIss,
                    documento.ValorPis,
                    documento.ValorCofins,
                    documento.ValorIr,
                    documento.ValorCsll,
                    documento.ValorInss,
                    documento.ValorOutros,
                    documento.CodigoServico,
                    documento.NumeroNota,
                    documento.IndicadorRetencoesFederais,
                    documento.IndicadorIss,
                    documento.LinhaOrigem
                }, transaction, cancellationToken: cancellationToken));

            const string movimentoSql = """
                INSERT INTO RECEBIMENTO_MOVIMENTO
                    (ID_RECEBIMENTO_IMPORTACAO,CHAVE_NATURAL,CLIENTE,DATA_RECEBIMENTO,VALOR_RECEBIDO,
                     PARCELA,NOTAS_VINCULADAS,DATA_EMISSAO_REFERENCIA,RECEBIDO_ANTES_EMISSAO,LINHA_ORIGEM)
                VALUES
                    (@ImportacaoId,@ChaveNatural,@Cliente,@DataRecebimento,@ValorRecebido,
                     @Parcela,@NotasVinculadas,@DataEmissaoReferencia,@RecebidoAntesEmissao,@LinhaOrigem);
                """;
            foreach (var movimento in importacao.Movimentos)
                await connection.ExecuteAsync(new CommandDefinition(movimentoSql, new
                {
                    ImportacaoId = importacaoId,
                    movimento.ChaveNatural,
                    movimento.Cliente,
                    movimento.DataRecebimento,
                    movimento.ValorRecebido,
                    movimento.Parcela,
                    movimento.NotasVinculadas,
                    movimento.DataEmissaoReferencia,
                    movimento.RecebidoAntesEmissao,
                    movimento.LinhaOrigem
                }, transaction, cancellationToken: cancellationToken));

            const string linhaSql = """
                INSERT INTO RECEBIMENTO_IMPORTACAO_LINHA
                    (ID_RECEBIMENTO_IMPORTACAO,NUMERO_LINHA,CONTEUDO_JSON,ALERTA)
                VALUES (@ImportacaoId,@NumeroLinha,@ConteudoJson,@Alerta);
                """;
            foreach (var linha in importacao.LinhasOrigem)
                await connection.ExecuteAsync(new CommandDefinition(linhaSql, new
                {
                    ImportacaoId = importacaoId,
                    linha.NumeroLinha,
                    linha.ConteudoJson,
                    linha.Alerta
                }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return importacaoId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<RecebimentoResumoMesDto>> GetResumoMensalAsync(int empresaId, int ano, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        const string sql = MovimentoUnicoCte + """
            SELECT MONTH(DATA_RECEBIMENTO) AS Mes,
                   SUM(VALOR_RECEBIDO) AS ValorRecebido,
                   COUNT(*) AS QuantidadeRecebimentos,
                   COUNT(DISTINCT CLIENTE) AS QuantidadeClientes
            FROM MovimentosUnicos
            WHERE RN=1 AND YEAR(DATA_RECEBIMENTO)=@Ano
            GROUP BY MONTH(DATA_RECEBIMENTO)
            ORDER BY Mes;
            """;
        return (await connection.QueryAsync<RecebimentoResumoMesDto>(new CommandDefinition(
            sql, new { EmpresaId = empresaId, Ano = ano }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<RecebimentoCompetenciaDto> GetCompetenciaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        var movimento = await connection.QuerySingleAsync<RecebimentoCompetenciaDto>(new CommandDefinition(
            MovimentoUnicoCte + """
                SELECT COALESCE(SUM(VALOR_RECEBIDO),0) AS ValorRecebido,
                       COUNT(*) AS QuantidadeRecebimentos,
                       COUNT(DISTINCT CLIENTE) AS QuantidadeClientes
                FROM MovimentosUnicos
                WHERE RN=1 AND YEAR(DATA_RECEBIMENTO)=@Ano AND MONTH(DATA_RECEBIMENTO)=@Mes;
                """, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken));

        var documento = await connection.QuerySingleAsync<RecebimentoCompetenciaDto>(new CommandDefinition(
            DocumentoUnicoCte + """
                SELECT COALESCE(SUM(VALOR_FATURADO),0) AS ValorFaturadoEmitido,
                       COALESCE(SUM(VALOR_ISS+VALOR_PIS+VALOR_COFINS+VALOR_IR+VALOR_CSLL+VALOR_INSS+VALOR_OUTROS),0) AS ValorRetencoesInformadas,
                       COUNT(*) AS QuantidadeDocumentos
                FROM DocumentosUnicos
                WHERE RN=1 AND YEAR(DATA_EMISSAO)=@Ano AND MONTH(DATA_EMISSAO)=@Mes;
                """, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken));

        movimento.ValorFaturadoEmitido = documento.ValorFaturadoEmitido;
        movimento.ValorRetencoesInformadas = documento.ValorRetencoesInformadas;
        movimento.QuantidadeDocumentos = documento.QuantidadeDocumentos;
        return movimento;
    }

    public async Task<IReadOnlyList<RecebimentoDetalheDto>> GetRecebimentosAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        const string sql = MovimentoUnicoCte + """
            SELECT DATA_RECEBIMENTO AS DataRecebimento, CLIENTE AS Cliente,
                   NOTAS_VINCULADAS AS NotasVinculadas, VALOR_RECEBIDO AS ValorRecebido,
                   PARCELA AS Parcela, DATA_EMISSAO_REFERENCIA AS DataEmissaoReferencia,
                   RECEBIDO_ANTES_EMISSAO AS RecebidoAntesEmissao, NOME_ARQUIVO AS NomeArquivo,
                   COMPETENCIA_ARQUIVO AS CompetenciaArquivo
            FROM MovimentosUnicos
            WHERE RN=1 AND YEAR(DATA_RECEBIMENTO)=@Ano AND MONTH(DATA_RECEBIMENTO)=@Mes
            ORDER BY DATA_RECEBIMENTO, CLIENTE, VALOR_RECEBIDO;
            """;
        return (await connection.QueryAsync<RecebimentoDetalheDto>(new CommandDefinition(
            sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<RecebimentoDocumentoDto>> GetDocumentosAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        const string sql = DocumentoUnicoCte + """
            SELECT DATA_EMISSAO AS DataEmissao, CLIENTE AS Cliente, NUMERO_NOTA AS NumeroNota,
                   CODIGO_SERVICO AS CodigoServico, VALOR_FATURADO AS ValorFaturado,
                   (VALOR_ISS+VALOR_PIS+VALOR_COFINS+VALOR_IR+VALOR_CSLL+VALOR_INSS+VALOR_OUTROS) AS ValorRetencoes,
                   INDICADOR_RETENCOES_FEDERAIS AS IndicadorRetencoesFederais,
                   INDICADOR_ISS AS IndicadorIss, NOME_ARQUIVO AS NomeArquivo
            FROM DocumentosUnicos
            WHERE RN=1 AND YEAR(DATA_EMISSAO)=@Ano AND MONTH(DATA_EMISSAO)=@Mes
            ORDER BY DATA_EMISSAO, CLIENTE, NUMERO_NOTA;
            """;
        return (await connection.QueryAsync<RecebimentoDocumentoDto>(new CommandDefinition(
            sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<RecebimentoImportacaoResumoDto>> GetImportacoesAsync(int empresaId, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        const string sql = """
            SELECT TOP 36 i.ID_RECEBIMENTO_IMPORTACAO AS Id,
                   i.COMPETENCIA_ARQUIVO AS CompetenciaArquivo, i.NOME_ARQUIVO AS NomeArquivo,
                   i.NOME_ABA AS NomeAba, i.LINHAS_LIDAS AS LinhasLidas,
                   (SELECT COUNT(*) FROM RECEBIMENTO_DOCUMENTO d WHERE d.ID_RECEBIMENTO_IMPORTACAO=i.ID_RECEBIMENTO_IMPORTACAO) AS QuantidadeDocumentos,
                   (SELECT COUNT(*) FROM RECEBIMENTO_MOVIMENTO m WHERE m.ID_RECEBIMENTO_IMPORTACAO=i.ID_RECEBIMENTO_IMPORTACAO) AS QuantidadeRecebimentos,
                   i.TOTAL_FATURADO_ARQUIVO AS TotalFaturadoArquivo,
                   i.TOTAL_RECEBIDO_ARQUIVO AS TotalRecebidoArquivo,
                   i.TOTAL_RETENCOES_ARQUIVO AS TotalRetencoesArquivo,
                   i.QUANTIDADE_ALERTAS AS QuantidadeAlertas, i.IMPORTADO_EM AS ImportadoEm
            FROM RECEBIMENTO_IMPORTACAO i
            WHERE i.ID_EMPRESA=@EmpresaId
            ORDER BY i.COMPETENCIA_ARQUIVO DESC, i.IMPORTADO_EM DESC;
            """;
        return (await connection.QueryAsync<RecebimentoImportacaoResumoDto>(new CommandDefinition(
            sql, new { EmpresaId = empresaId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<RecebimentoFortesOrigemDto>> GetPreparacaoFortesAsync(
        int empresaId,
        int ano,
        int mes,
        CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        await GarantirSchemaAsync(connection, cancellationToken);
        const string sql = """
            WITH MovimentosUnicos AS (
                SELECT m.*, i.ID_EMPRESA, i.COMPETENCIA_ARQUIVO,
                       ROW_NUMBER() OVER (
                           PARTITION BY i.ID_EMPRESA, m.CHAVE_NATURAL
                           ORDER BY i.COMPETENCIA_ARQUIVO DESC, i.IMPORTADO_EM DESC, m.ID_RECEBIMENTO_MOVIMENTO DESC
                       ) AS RN
                FROM RECEBIMENTO_MOVIMENTO m
                INNER JOIN RECEBIMENTO_IMPORTACAO i ON i.ID_RECEBIMENTO_IMPORTACAO=m.ID_RECEBIMENTO_IMPORTACAO
                WHERE i.ID_EMPRESA=@EmpresaId
            ),
            DocumentosUnicos AS (
                SELECT d.*, i.ID_EMPRESA,
                       ROW_NUMBER() OVER (
                           PARTITION BY i.ID_EMPRESA, d.CHAVE_NATURAL
                           ORDER BY i.COMPETENCIA_ARQUIVO DESC, i.IMPORTADO_EM DESC, d.ID_RECEBIMENTO_DOCUMENTO DESC
                       ) AS RN
                FROM RECEBIMENTO_DOCUMENTO d
                INNER JOIN RECEBIMENTO_IMPORTACAO i ON i.ID_RECEBIMENTO_IMPORTACAO=d.ID_RECEBIMENTO_IMPORTACAO
                WHERE i.ID_EMPRESA=@EmpresaId
            )
            SELECT m.CHAVE_NATURAL AS MovimentoChaveNatural,
                   m.DATA_RECEBIMENTO AS DataRecebimento,
                   m.VALOR_RECEBIDO AS ValorRecebido,
                   m.CLIENTE AS Cliente,
                   m.NOTAS_VINCULADAS AS NotasVinculadas,
                   d.NUMERO_NOTA AS NumeroNota,
                   d.DATA_EMISSAO AS DataEmissao,
                   d.VALOR_FATURADO AS ValorFaturado,
                   d.DESCONTO_INFORMADO AS DescontoInformado,
                   COALESCE((
                       SELECT SUM(total.VALOR_RECEBIDO)
                       FROM MovimentosUnicos total
                       WHERE total.RN=1
                         AND total.CLIENTE=m.CLIENTE
                         AND COALESCE(total.NOTAS_VINCULADAS,'')=COALESCE(m.NOTAS_VINCULADAS,'')
                         AND COALESCE(total.DATA_EMISSAO_REFERENCIA,CONVERT(date,'19000101'))=
                             COALESCE(m.DATA_EMISSAO_REFERENCIA,CONVERT(date,'19000101'))
                   ),0) AS ValorRecebidoTotalConhecido
            FROM MovimentosUnicos m
            LEFT JOIN DocumentosUnicos d
              ON d.RN=1
             AND d.CLIENTE=m.CLIENTE
             AND (
                    d.NUMERO_NOTA=m.NOTAS_VINCULADAS
                    OR CHARINDEX(
                        ';'+REPLACE(LTRIM(RTRIM(d.NUMERO_NOTA)),' ','')+';',
                        ';'+REPLACE(COALESCE(m.NOTAS_VINCULADAS,''),' ','')+';'
                    ) > 0
                 )
             AND COALESCE(d.DATA_EMISSAO,CONVERT(date,'19000101'))=
                 COALESCE(m.DATA_EMISSAO_REFERENCIA,CONVERT(date,'19000101'))
            WHERE m.RN=1
              AND YEAR(m.DATA_RECEBIMENTO)=@Ano
              AND MONTH(m.DATA_RECEBIMENTO)=@Mes
            ORDER BY m.DATA_RECEBIMENTO, m.CLIENTE, m.NOTAS_VINCULADAS, m.VALOR_RECEBIDO;
            """;

        return (await connection.QueryAsync<RecebimentoFortesOrigemDto>(new CommandDefinition(
            sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken))).AsList();
    }

    private static async Task GarantirSchemaAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref schemaGarantido))
            return;
        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (schemaGarantido)
                return;
            await connection.ExecuteAsync(new CommandDefinition(
                SchemaSql, commandTimeout: 120, cancellationToken: cancellationToken));
            Volatile.Write(ref schemaGarantido, true);
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private const string MovimentoUnicoCte = """
        WITH MovimentosUnicos AS (
            SELECT m.*, i.ID_EMPRESA, i.NOME_ARQUIVO, i.COMPETENCIA_ARQUIVO,
                   ROW_NUMBER() OVER (
                       PARTITION BY i.ID_EMPRESA, m.CHAVE_NATURAL
                       ORDER BY i.COMPETENCIA_ARQUIVO DESC, i.IMPORTADO_EM DESC, m.ID_RECEBIMENTO_MOVIMENTO DESC
                   ) AS RN
            FROM RECEBIMENTO_MOVIMENTO m
            INNER JOIN RECEBIMENTO_IMPORTACAO i ON i.ID_RECEBIMENTO_IMPORTACAO=m.ID_RECEBIMENTO_IMPORTACAO
            WHERE i.ID_EMPRESA=@EmpresaId
        )
        """;

    private const string DocumentoUnicoCte = """
        WITH DocumentosUnicos AS (
            SELECT d.*, i.ID_EMPRESA, i.NOME_ARQUIVO, i.COMPETENCIA_ARQUIVO,
                   ROW_NUMBER() OVER (
                       PARTITION BY i.ID_EMPRESA, d.CHAVE_NATURAL
                       ORDER BY i.COMPETENCIA_ARQUIVO DESC, i.IMPORTADO_EM DESC, d.ID_RECEBIMENTO_DOCUMENTO DESC
                   ) AS RN
            FROM RECEBIMENTO_DOCUMENTO d
            INNER JOIN RECEBIMENTO_IMPORTACAO i ON i.ID_RECEBIMENTO_IMPORTACAO=d.ID_RECEBIMENTO_IMPORTACAO
            WHERE i.ID_EMPRESA=@EmpresaId
        )
        """;

    private const string SchemaSql = """
        IF OBJECT_ID('dbo.RECEBIMENTO_IMPORTACAO','U') IS NULL
        CREATE TABLE dbo.RECEBIMENTO_IMPORTACAO (
            ID_RECEBIMENTO_IMPORTACAO INT IDENTITY PRIMARY KEY,
            ID_EMPRESA INT NOT NULL,
            COMPETENCIA_ARQUIVO DATE NOT NULL,
            NOME_ARQUIVO NVARCHAR(255) NOT NULL,
            HASH_ARQUIVO VARCHAR(64) NOT NULL,
            NOME_ABA NVARCHAR(128) NOT NULL,
            LINHAS_LIDAS INT NOT NULL,
            TOTAL_FATURADO_ARQUIVO DECIMAL(18,2) NOT NULL,
            TOTAL_RECEBIDO_ARQUIVO DECIMAL(18,2) NOT NULL,
            TOTAL_RETENCOES_ARQUIVO DECIMAL(18,4) NOT NULL,
            QUANTIDADE_ALERTAS INT NOT NULL,
            IMPORTADO_EM DATETIME2 NOT NULL,
            CONSTRAINT FK_RECEBIMENTO_IMPORTACAO_EMPRESA FOREIGN KEY(ID_EMPRESA) REFERENCES EMPRESA(ID_EMPRESA),
            CONSTRAINT UX_RECEBIMENTO_IMPORTACAO_COMPETENCIA UNIQUE(ID_EMPRESA,COMPETENCIA_ARQUIVO)
        );

        IF OBJECT_ID('dbo.RECEBIMENTO_DOCUMENTO','U') IS NULL
        CREATE TABLE dbo.RECEBIMENTO_DOCUMENTO (
            ID_RECEBIMENTO_DOCUMENTO BIGINT IDENTITY PRIMARY KEY,
            ID_RECEBIMENTO_IMPORTACAO INT NOT NULL,
            CHAVE_NATURAL VARCHAR(64) NOT NULL,
            CLIENTE NVARCHAR(255) NOT NULL,
            DATA_EMISSAO DATE NULL,
            VALOR_FATURADO DECIMAL(18,2) NOT NULL,
            DESCONTO_INFORMADO DECIMAL(18,2) NULL,
            VALOR_ISS DECIMAL(18,4) NOT NULL,
            VALOR_PIS DECIMAL(18,4) NOT NULL,
            VALOR_COFINS DECIMAL(18,4) NOT NULL,
            VALOR_IR DECIMAL(18,4) NOT NULL,
            VALOR_CSLL DECIMAL(18,4) NOT NULL,
            VALOR_INSS DECIMAL(18,4) NOT NULL,
            VALOR_OUTROS DECIMAL(18,4) NOT NULL,
            CODIGO_SERVICO VARCHAR(50) NULL,
            NUMERO_NOTA VARCHAR(100) NULL,
            INDICADOR_RETENCOES_FEDERAIS NVARCHAR(255) NULL,
            INDICADOR_ISS NVARCHAR(255) NULL,
            LINHA_ORIGEM INT NOT NULL,
            CONSTRAINT FK_RECEBIMENTO_DOCUMENTO_IMPORTACAO FOREIGN KEY(ID_RECEBIMENTO_IMPORTACAO)
                REFERENCES RECEBIMENTO_IMPORTACAO(ID_RECEBIMENTO_IMPORTACAO) ON DELETE CASCADE
        );

        IF OBJECT_ID('dbo.RECEBIMENTO_MOVIMENTO','U') IS NULL
        CREATE TABLE dbo.RECEBIMENTO_MOVIMENTO (
            ID_RECEBIMENTO_MOVIMENTO BIGINT IDENTITY PRIMARY KEY,
            ID_RECEBIMENTO_IMPORTACAO INT NOT NULL,
            CHAVE_NATURAL VARCHAR(64) NOT NULL,
            CLIENTE NVARCHAR(255) NOT NULL,
            DATA_RECEBIMENTO DATE NOT NULL,
            VALOR_RECEBIDO DECIMAL(18,2) NOT NULL,
            PARCELA VARCHAR(50) NULL,
            NOTAS_VINCULADAS NVARCHAR(1000) NULL,
            DATA_EMISSAO_REFERENCIA DATE NULL,
            RECEBIDO_ANTES_EMISSAO BIT NOT NULL,
            LINHA_ORIGEM INT NOT NULL,
            CONSTRAINT FK_RECEBIMENTO_MOVIMENTO_IMPORTACAO FOREIGN KEY(ID_RECEBIMENTO_IMPORTACAO)
                REFERENCES RECEBIMENTO_IMPORTACAO(ID_RECEBIMENTO_IMPORTACAO) ON DELETE CASCADE
        );

        IF OBJECT_ID('dbo.RECEBIMENTO_IMPORTACAO_LINHA','U') IS NULL
        CREATE TABLE dbo.RECEBIMENTO_IMPORTACAO_LINHA (
            ID_RECEBIMENTO_IMPORTACAO_LINHA BIGINT IDENTITY PRIMARY KEY,
            ID_RECEBIMENTO_IMPORTACAO INT NOT NULL,
            NUMERO_LINHA INT NOT NULL,
            CONTEUDO_JSON NVARCHAR(MAX) NOT NULL,
            ALERTA NVARCHAR(1000) NULL,
            CONSTRAINT FK_RECEBIMENTO_LINHA_IMPORTACAO FOREIGN KEY(ID_RECEBIMENTO_IMPORTACAO)
                REFERENCES RECEBIMENTO_IMPORTACAO(ID_RECEBIMENTO_IMPORTACAO) ON DELETE CASCADE,
            CONSTRAINT UX_RECEBIMENTO_LINHA UNIQUE(ID_RECEBIMENTO_IMPORTACAO,NUMERO_LINHA)
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RECEBIMENTO_MOVIMENTO_DATA' AND object_id=OBJECT_ID('dbo.RECEBIMENTO_MOVIMENTO'))
            CREATE INDEX IX_RECEBIMENTO_MOVIMENTO_DATA ON dbo.RECEBIMENTO_MOVIMENTO(DATA_RECEBIMENTO,CHAVE_NATURAL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RECEBIMENTO_DOCUMENTO_EMISSAO' AND object_id=OBJECT_ID('dbo.RECEBIMENTO_DOCUMENTO'))
            CREATE INDEX IX_RECEBIMENTO_DOCUMENTO_EMISSAO ON dbo.RECEBIMENTO_DOCUMENTO(DATA_EMISSAO,CHAVE_NATURAL);
        """;
}
