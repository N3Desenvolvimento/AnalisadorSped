using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface INfseRepository
{
    Task<long> GetUltimoNsuAsync(int empresaId, CancellationToken cancellationToken = default);
    Task AtualizarUltimoNsuAsync(int empresaId, long ultimoNsu, CancellationToken cancellationToken = default);
    Task ReiniciarNsuAsync(int empresaId, CancellationToken cancellationToken = default);
    Task SalvarAsync(NfseDocumento documento, CancellationToken cancellationToken = default);
    Task MarcarCanceladaAsync(int empresaId, string chaveAcesso, CancellationToken cancellationToken = default);
    Task<IssApuracaoDto> GetApuracaoAsync(int empresaId, string cnpjEmpresa, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateTime>> GetCompetenciasAsync(int empresaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NfseXmlDownloadDto>> GetXmlsAsync(int empresaId, string cnpjEmpresa, int ano, int mes, string tipo, CancellationToken cancellationToken = default);
}

public sealed class NfseXmlDownloadDto
{
    public string ChaveAcesso { get; set; } = string.Empty;
    public bool Cancelada { get; set; }
    public string Xml { get; set; } = string.Empty;
}

public sealed class NfseRepository : INfseRepository
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaReady;
    private readonly IDbConnectionFactory _connectionFactory;

    public NfseRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<long> GetUltimoNsuAsync(int empresaId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE((SELECT ULTIMO_NSU FROM NFSE_SINCRONIZACAO WHERE ID_EMPRESA=@EmpresaId), 0)",
            new { EmpresaId = empresaId }, cancellationToken: cancellationToken));
    }

    public async Task AtualizarUltimoNsuAsync(int empresaId, long ultimoNsu, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            MERGE NFSE_SINCRONIZACAO AS destino
            USING (SELECT @EmpresaId AS ID_EMPRESA) AS origem ON destino.ID_EMPRESA=origem.ID_EMPRESA
            WHEN MATCHED THEN UPDATE SET ULTIMO_NSU=@UltimoNsu, ATUALIZADO_EM=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (ID_EMPRESA, ULTIMO_NSU) VALUES (@EmpresaId, @UltimoNsu);
            """;
        await using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { EmpresaId = empresaId, UltimoNsu = ultimoNsu }, cancellationToken: cancellationToken));
    }

    public async Task ReiniciarNsuAsync(int empresaId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = "DELETE FROM NFSE_SINCRONIZACAO WHERE ID_EMPRESA=@EmpresaId";
        await using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { EmpresaId = empresaId }, cancellationToken: cancellationToken));
    }

    public async Task SalvarAsync(NfseDocumento d, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            MERGE NFSE_DOCUMENTO AS destino
            USING (SELECT @EmpresaId AS ID_EMPRESA, @ChaveAcesso AS CHAVE_ACESSO) AS origem
               ON destino.ID_EMPRESA = origem.ID_EMPRESA AND destino.CHAVE_ACESSO = origem.CHAVE_ACESSO
            WHEN MATCHED THEN UPDATE SET
                NSU=@Nsu, NUMERO_NOTA=@NumeroNota, DATA_EMISSAO=@DataEmissao, COMPETENCIA=@Competencia,
                CNPJ_PRESTADOR=@CnpjPrestador, NOME_PRESTADOR=@NomePrestador, CNPJ_TOMADOR=@CnpjTomador,
                NOME_TOMADOR=@NomeTomador,
                CODIGO_SERVICO=@CodigoServico, MUNICIPIO_INCIDENCIA=@MunicipioIncidencia,
                VALOR_SERVICOS=@ValorServicos, BASE_CALCULO=@BaseCalculo, ALIQUOTA=@Aliquota,
                VALOR_ISS=@ValorIss, ISS_RETIDO=@IssRetido, CANCELADA=@Cancelada,
                VALOR_PIS_RETIDO=@ValorPisRetido, VALOR_COFINS_RETIDO=@ValorCofinsRetido,
                VALOR_IRRF_RETIDO=@ValorIrrfRetido, VALOR_CSLL_RETIDO=@ValorCsllRetido,
                VALOR_INSS_RETIDO=@ValorInssRetido,
                XML_DOCUMENTO=@Xml, ATUALIZADO_EM=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (ID_EMPRESA, NSU, CHAVE_ACESSO, NUMERO_NOTA, DATA_EMISSAO, COMPETENCIA, CNPJ_PRESTADOR, NOME_PRESTADOR,
                 CNPJ_TOMADOR, NOME_TOMADOR, CODIGO_SERVICO, MUNICIPIO_INCIDENCIA, VALOR_SERVICOS,
                 BASE_CALCULO, ALIQUOTA, VALOR_ISS, ISS_RETIDO, VALOR_PIS_RETIDO,
                 VALOR_COFINS_RETIDO, VALOR_IRRF_RETIDO, VALOR_CSLL_RETIDO, VALOR_INSS_RETIDO,
                 CANCELADA, XML_DOCUMENTO)
            VALUES
                (@EmpresaId, @Nsu, @ChaveAcesso, @NumeroNota, @DataEmissao, @Competencia, @CnpjPrestador, @NomePrestador,
                 @CnpjTomador, @NomeTomador, @CodigoServico, @MunicipioIncidencia, @ValorServicos,
                 @BaseCalculo, @Aliquota, @ValorIss, @IssRetido, @ValorPisRetido,
                 @ValorCofinsRetido, @ValorIrrfRetido, @ValorCsllRetido, @ValorInssRetido,
                 @Cancelada, @Xml);
            """;
        await using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, d, cancellationToken: cancellationToken));
    }

    public async Task MarcarCanceladaAsync(int empresaId, string chaveAcesso, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = "UPDATE NFSE_DOCUMENTO SET CANCELADA=1, ATUALIZADO_EM=SYSUTCDATETIME() WHERE ID_EMPRESA=@EmpresaId AND CHAVE_ACESSO=@ChaveAcesso";
        await using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { EmpresaId = empresaId, ChaveAcesso = chaveAcesso }, cancellationToken: cancellationToken));
    }

    public async Task<IssApuracaoDto> GetApuracaoAsync(int empresaId, string cnpjEmpresa, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT CHAVE_ACESSO AS ChaveAcesso,
                COALESCE(NUMERO_NOTA, CASE WHEN LEN(CHAVE_ACESSO)=50
                    THEN CONVERT(VARCHAR(30), TRY_CONVERT(BIGINT, SUBSTRING(CHAVE_ACESSO, 23, 13))) END) AS NumeroNota,
                DATA_EMISSAO AS DataEmissao,
                CASE WHEN
                    (LEN(CHAVE_ACESSO)=50 AND SUBSTRING(CHAVE_ACESSO, 9, 14)=@Cnpj)
                    OR REPLACE(REPLACE(REPLACE(CNPJ_PRESTADOR, '.', ''), '/', ''), '-', '')=@Cnpj
                    THEN 'Prestada' ELSE 'Tomada' END AS Tipo,
                CNPJ_PRESTADOR AS CnpjPrestador, NOME_PRESTADOR AS NomePrestador,
                CNPJ_TOMADOR AS CnpjTomador, NOME_TOMADOR AS NomeTomador,
                CODIGO_SERVICO AS CodigoServico, MUNICIPIO_INCIDENCIA AS MunicipioIncidencia, VALOR_SERVICOS AS ValorServicos,
                BASE_CALCULO AS BaseCalculo, ALIQUOTA AS Aliquota, VALOR_ISS AS ValorIss,
                ISS_RETIDO AS IssRetido, VALOR_PIS_RETIDO AS ValorPisRetido,
                VALOR_COFINS_RETIDO AS ValorCofinsRetido, VALOR_IRRF_RETIDO AS ValorIrrfRetido,
                VALOR_CSLL_RETIDO AS ValorCsllRetido, VALOR_INSS_RETIDO AS ValorInssRetido
            FROM NFSE_DOCUMENTO
            WHERE ID_EMPRESA=@EmpresaId AND COMPETENCIA>=@Inicio AND COMPETENCIA<@Fim AND CANCELADA=0
            ORDER BY DATA_EMISSAO DESC;
            """;
        var cnpj = new string(cnpjEmpresa.Where(char.IsDigit).ToArray());
        var inicio = new DateTime(ano, mes, 1);
        await using var connection = _connectionFactory.CreateConnection();
        var notas = (await connection.QueryAsync<IssNotaDto>(new CommandDefinition(sql,
            new { EmpresaId = empresaId, Cnpj = cnpj, Inicio = inicio, Fim = inicio.AddMonths(1) },
            cancellationToken: cancellationToken))).AsList();
        var prestadas = notas.Where(n => n.Tipo == "Prestada").ToArray();
        var tomadas = notas.Where(n => n.Tipo == "Tomada").ToArray();
        return new IssApuracaoDto
        {
            EmpresaId = empresaId, Ano = ano, Mes = mes, Notas = notas,
            QuantidadePrestadas = prestadas.Length, QuantidadeTomadas = tomadas.Length,
            ValorServicosPrestados = prestadas.Sum(n => n.ValorServicos),
            ValorServicosTomados = tomadas.Sum(n => n.ValorServicos),
            IssDevido = prestadas.Where(n => !n.IssRetido).Sum(n => n.ValorIss),
            IssRetidoPorTomadores = prestadas.Where(n => n.IssRetido).Sum(n => n.ValorIss),
            IssRetidoComoTomador = tomadas.Where(n => n.IssRetido).Sum(n => n.ValorIss)
        };
    }

    public async Task<IReadOnlyList<DateTime>> GetCompetenciasAsync(int empresaId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT DISTINCT DATEFROMPARTS(YEAR(COMPETENCIA), MONTH(COMPETENCIA), 1)
            FROM NFSE_DOCUMENTO
            WHERE ID_EMPRESA=@EmpresaId AND CANCELADA=0
            ORDER BY 1 DESC;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DateTime>(new CommandDefinition(sql,
            new { EmpresaId = empresaId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<NfseXmlDownloadDto>> GetXmlsAsync(int empresaId, string cnpjEmpresa, int ano, int mes, string tipo, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT CHAVE_ACESSO AS ChaveAcesso, CANCELADA AS Cancelada,
                CONVERT(NVARCHAR(MAX), XML_DOCUMENTO) AS Xml
            FROM NFSE_DOCUMENTO
            WHERE ID_EMPRESA=@EmpresaId AND COMPETENCIA>=@Inicio AND COMPETENCIA<@Fim
              AND ((@Tipo='prestadas' AND (
                    (LEN(CHAVE_ACESSO)=50 AND SUBSTRING(CHAVE_ACESSO, 9, 14)=@Cnpj)
                    OR REPLACE(REPLACE(REPLACE(CNPJ_PRESTADOR, '.', ''), '/', ''), '-', '')=@Cnpj))
                OR (@Tipo='tomadas' AND NOT (
                    (LEN(CHAVE_ACESSO)=50 AND SUBSTRING(CHAVE_ACESSO, 9, 14)=@Cnpj)
                    OR REPLACE(REPLACE(REPLACE(CNPJ_PRESTADOR, '.', ''), '/', ''), '-', '')=@Cnpj)))
            ORDER BY DATA_EMISSAO, CHAVE_ACESSO;
            """;
        var inicio = new DateTime(ano, mes, 1);
        var cnpj = new string(cnpjEmpresa.Where(char.IsDigit).ToArray());
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<NfseXmlDownloadDto>(new CommandDefinition(sql,
            new { EmpresaId = empresaId, Cnpj = cnpj, Tipo = tipo, Inicio = inicio, Fim = inicio.AddMonths(1) },
            cancellationToken: cancellationToken))).AsList();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;
        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            const string sql = """
                IF OBJECT_ID('dbo.NFSE_DOCUMENTO', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.NFSE_DOCUMENTO
                    (
                        ID_NFSE BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NFSE_DOCUMENTO PRIMARY KEY,
                        ID_EMPRESA INT NOT NULL,
                        NSU BIGINT NOT NULL,
                        CHAVE_ACESSO VARCHAR(60) NOT NULL,
                        NUMERO_NOTA VARCHAR(30) NULL,
                        DATA_EMISSAO DATETIME2 NOT NULL,
                        COMPETENCIA DATE NOT NULL,
                        CNPJ_PRESTADOR VARCHAR(14) NOT NULL,
                        NOME_PRESTADOR VARCHAR(255) NULL,
                        CNPJ_TOMADOR VARCHAR(14) NULL,
                        NOME_TOMADOR VARCHAR(255) NULL,
                        CODIGO_SERVICO VARCHAR(20) NULL,
                        MUNICIPIO_INCIDENCIA VARCHAR(7) NULL,
                        VALOR_SERVICOS DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_VALOR_SERVICOS DEFAULT 0,
                        BASE_CALCULO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_BASE_CALCULO DEFAULT 0,
                        ALIQUOTA DECIMAL(9,4) NOT NULL CONSTRAINT DF_NFSE_ALIQUOTA DEFAULT 0,
                        VALOR_ISS DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_VALOR_ISS DEFAULT 0,
                        VALOR_PIS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_PIS_RET DEFAULT 0,
                        VALOR_COFINS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_COFINS_RET DEFAULT 0,
                        VALOR_IRRF_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_IRRF_RET DEFAULT 0,
                        VALOR_CSLL_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_CSLL_RET DEFAULT 0,
                        VALOR_INSS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_INSS_RET DEFAULT 0,
                        ISS_RETIDO BIT NOT NULL CONSTRAINT DF_NFSE_ISS_RETIDO DEFAULT 0,
                        CANCELADA BIT NOT NULL CONSTRAINT DF_NFSE_CANCELADA DEFAULT 0,
                        XML_DOCUMENTO XML NOT NULL,
                        CRIADO_EM DATETIME2 NOT NULL CONSTRAINT DF_NFSE_CRIADO_EM DEFAULT SYSUTCDATETIME(),
                        ATUALIZADO_EM DATETIME2 NULL,
                        CONSTRAINT FK_NFSE_EMPRESA FOREIGN KEY (ID_EMPRESA) REFERENCES dbo.EMPRESA(ID_EMPRESA),
                        CONSTRAINT UQ_NFSE_EMPRESA_CHAVE UNIQUE (ID_EMPRESA, CHAVE_ACESSO),
                        CONSTRAINT UQ_NFSE_EMPRESA_NSU UNIQUE (ID_EMPRESA, NSU)
                    );

                    CREATE INDEX IX_NFSE_APURACAO ON dbo.NFSE_DOCUMENTO
                        (ID_EMPRESA, COMPETENCIA, CANCELADA)
                        INCLUDE (CNPJ_PRESTADOR, CNPJ_TOMADOR, VALOR_SERVICOS, VALOR_ISS, ISS_RETIDO);
                END;

                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'NUMERO_NOTA') IS NULL
                    ALTER TABLE dbo.NFSE_DOCUMENTO ADD NUMERO_NOTA VARCHAR(30) NULL;

                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'NOME_PRESTADOR') IS NULL
                    ALTER TABLE dbo.NFSE_DOCUMENTO ADD NOME_PRESTADOR VARCHAR(255) NULL;

                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'VALOR_PIS_RETIDO') IS NULL ALTER TABLE dbo.NFSE_DOCUMENTO ADD VALOR_PIS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_PIS_RET DEFAULT 0 WITH VALUES;
                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'VALOR_COFINS_RETIDO') IS NULL ALTER TABLE dbo.NFSE_DOCUMENTO ADD VALOR_COFINS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_COFINS_RET DEFAULT 0 WITH VALUES;
                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'VALOR_IRRF_RETIDO') IS NULL ALTER TABLE dbo.NFSE_DOCUMENTO ADD VALOR_IRRF_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_IRRF_RET DEFAULT 0 WITH VALUES;
                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'VALOR_CSLL_RETIDO') IS NULL ALTER TABLE dbo.NFSE_DOCUMENTO ADD VALOR_CSLL_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_CSLL_RET DEFAULT 0 WITH VALUES;
                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'VALOR_INSS_RETIDO') IS NULL ALTER TABLE dbo.NFSE_DOCUMENTO ADD VALOR_INSS_RETIDO DECIMAL(18,2) NOT NULL CONSTRAINT DF_NFSE_INSS_RET DEFAULT 0 WITH VALUES;

                IF COL_LENGTH('dbo.NFSE_DOCUMENTO', 'NOME_TOMADOR') IS NULL
                    ALTER TABLE dbo.NFSE_DOCUMENTO ADD NOME_TOMADOR VARCHAR(255) NULL;

                IF OBJECT_ID('dbo.NFSE_SINCRONIZACAO', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.NFSE_SINCRONIZACAO
                    (
                        ID_EMPRESA INT NOT NULL CONSTRAINT PK_NFSE_SINCRONIZACAO PRIMARY KEY,
                        ULTIMO_NSU BIGINT NOT NULL CONSTRAINT DF_NFSE_SINC_NSU DEFAULT 0,
                        ATUALIZADO_EM DATETIME2 NOT NULL CONSTRAINT DF_NFSE_SINC_DATA DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_NFSE_SINC_EMPRESA FOREIGN KEY (ID_EMPRESA) REFERENCES dbo.EMPRESA(ID_EMPRESA)
                    );
                END;
                """;
            await using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
            _schemaReady = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
