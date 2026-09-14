using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IDashboardFiscalRepository
{
    Task<IReadOnlyList<EmpresaOpcaoDto>> GetEmpresasAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpedEnvioEmpresaAnoDto>> GetEmpresasComSpedNoAnoAsync(int ano, CancellationToken cancellationToken = default);
    Task<DashboardFiscalDto> GetDashboardAsync(int empresaId, DateTime competencia, CancellationToken cancellationToken = default);
    Task<DashboardFiscalAnualDto> GetDashboardAnualAsync(int empresaId, int ano, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardEntradaParticipanteDto>> GetEntradasPorParticipanteAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasEntradaFornecedorAsync(int empresaId, int ano, int mes, string codigoParticipante, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasEntradaFornecedoresAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardNotaEntradaItemDto>> GetItensNotaEntradaAsync(int spedC100Id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetFornecedoresComIcmsZeradoMesesAnterioresAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardTopProdutoSaidaDto>> GetTopProdutosSaidaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardTopFornecedorEntradaDto>> GetTopFornecedoresEntradaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardTopClienteSaidaDto>> GetTopClientesSaidaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardProdutoOperacaoDto>> GetProdutosPorOperacaoAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task SalvarGuiaIcmsPdfAsync(DashboardGuiaIcmsPdfDto guia, CancellationToken cancellationToken = default);
    Task<DashboardGuiaIcmsPdfDto?> GetGuiaIcmsPdfAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
}

public sealed class DashboardFiscalRepository : IDashboardFiscalRepository
{
    private static readonly string[] NomesMeses = [
        "Janeiro", "Fevereiro", "Marco", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    ];

    private readonly IDbConnectionFactory _connectionFactory;

    public DashboardFiscalRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EmpresaOpcaoDto>> GetEmpresasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ID_EMPRESA AS Id, CNPJ AS Cnpj, RAZAO_SOCIAL AS RazaoSocial, UF AS Uf
            FROM EMPRESA
            ORDER BY RAZAO_SOCIAL;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        var empresas = await connection.QueryAsync<EmpresaOpcaoDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return empresas.AsList();
    }

    public async Task<IReadOnlyList<SpedEnvioEmpresaAnoDto>> GetEmpresasComSpedNoAnoAsync(
        int ano, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                e.ID_EMPRESA AS EmpresaId,
                e.RAZAO_SOCIAL AS RazaoSocial,
                e.CNPJ AS Cnpj,
                MONTH(a.DT_INI) AS Mes
            FROM EMPRESA e
            INNER JOIN SPED_ARQUIVO a ON a.ID_EMPRESA = e.ID_EMPRESA
            WHERE a.DT_INI >= @Inicio
              AND a.DT_INI < @Fim
            GROUP BY e.ID_EMPRESA, e.RAZAO_SOCIAL, e.CNPJ, MONTH(a.DT_INI)
            ORDER BY e.RAZAO_SOCIAL, e.CNPJ, MONTH(a.DT_INI);
            """;

        await using var connection = _connectionFactory.CreateConnection();
        var linhas = await connection.QueryAsync<SpedEnvioEmpresaMesLinha>(
            new CommandDefinition(sql, new
            {
                Inicio = new DateTime(ano, 1, 1),
                Fim = new DateTime(ano + 1, 1, 1)
            }, cancellationToken: cancellationToken));

        return linhas
            .GroupBy(item => new { item.EmpresaId, item.RazaoSocial, item.Cnpj })
            .Select(grupo => new SpedEnvioEmpresaAnoDto
            {
                EmpresaId = grupo.Key.EmpresaId,
                RazaoSocial = grupo.Key.RazaoSocial,
                Cnpj = grupo.Key.Cnpj,
                MesesEnviados = grupo.Select(item => item.Mes).ToHashSet()
            })
            .ToArray();
    }

    private sealed class SpedEnvioEmpresaMesLinha
    {
        public int EmpresaId { get; set; }
        public string RazaoSocial { get; set; } = string.Empty;
        public string Cnpj { get; set; } = string.Empty;
        public int Mes { get; set; }
    }

    public async Task<DashboardFiscalAnualDto> GetDashboardAnualAsync(int empresaId, int ano, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await GarantirTabelaGuiaIcmsPdfAsync(connection, cancellationToken);

        var meses = (await connection.QueryAsync<DashboardFiscalMesDto>(
            new CommandDefinition(DashboardAnualSql, new { EmpresaId = empresaId, Ano = ano }, cancellationToken: cancellationToken))).AsList();

        foreach (var mes in meses)
        {
            mes.NomeMes = NomesMeses[mes.Mes - 1];
        }

        return new DashboardFiscalAnualDto
        {
            Ano = ano,
            Meses = meses
        };
    }


    public async Task<IReadOnlyList<DashboardEntradaParticipanteDto>> GetEntradasPorParticipanteAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var entradas = await connection.QueryAsync<DashboardEntradaParticipanteDto>(
            new CommandDefinition(EntradasPorParticipanteSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken));

        return entradas.AsList();
    }

    public async Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasEntradaFornecedorAsync(int empresaId, int ano, int mes, string codigoParticipante, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await GarantirColunasSimplesNacionalAsync(connection, cancellationToken);

        var notas = await connection.QueryAsync<DashboardNotaEntradaFornecedorDto>(
            new CommandDefinition(NotasEntradaFornecedorSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes, CodigoParticipante = codigoParticipante }, commandTimeout: 30, cancellationToken: cancellationToken));

        return notas.AsList();
    }

    public async Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasEntradaFornecedoresAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await GarantirColunasSimplesNacionalAsync(connection, cancellationToken);

        var notas = await connection.QueryAsync<DashboardNotaEntradaFornecedorDto>(
            new CommandDefinition(NotasEntradaFornecedoresSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return notas.AsList();
    }

    public async Task<IReadOnlyList<DashboardNotaEntradaItemDto>> GetItensNotaEntradaAsync(
        int spedC100Id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c170.ID_C170 AS SpedC170Id,
                COALESCE(c170.NUM_ITEM, 0) AS NumeroItem,
                COALESCE(c170.COD_ITEM, '') AS CodigoItem,
                COALESCE(NULLIF(p.DESCRICAO, ''), NULLIF(c170.DESCR_COMPL, ''), c170.COD_ITEM, '(Item sem descrição)') AS Descricao,
                COALESCE(c170.CFOP, '') AS Cfop,
                COALESCE(c170.CST_ICMS, '') AS CstIcms,
                COALESCE(c170.VL_ITEM, 0) - COALESCE(c170.VL_DESC, 0) AS ValorItem,
                COALESCE(c170.VL_BC_ICMS, 0) AS ValorBaseIcms,
                COALESCE(c170.ALIQ_ICMS, 0) AS AliquotaIcms,
                COALESCE(c170.VL_ICMS, 0) AS ValorIcmsCredito
            FROM SPED_C170 c170
            LEFT JOIN PRODUTO p ON p.ID_PRODUTO = c170.ID_PRODUTO
            WHERE c170.ID_C100 = @SpedC100Id
            ORDER BY COALESCE(c170.NUM_ITEM, 0), c170.ID_C170;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DashboardNotaEntradaItemDto>(
            new CommandDefinition(sql, new { SpedC100Id = spedC100Id },
                commandTimeout: 30, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<string>> GetFornecedoresComIcmsZeradoMesesAnterioresAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var fornecedores = await connection.QueryAsync<string>(
            new CommandDefinition(FornecedoresIcmsZeradoMesesAnterioresSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return fornecedores.AsList();
    }

    public async Task<IReadOnlyList<DashboardTopProdutoSaidaDto>> GetTopProdutosSaidaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var produtos = await connection.QueryAsync<DashboardTopProdutoSaidaDto>(
            new CommandDefinition(TopProdutosSaidaSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return produtos.AsList();
    }

    public async Task SalvarGuiaIcmsPdfAsync(DashboardGuiaIcmsPdfDto guia, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await GarantirTabelaGuiaIcmsPdfAsync(connection, cancellationToken);

        const string sql = """
            MERGE GUIA_ICMS_PDF AS destino
            USING (SELECT @EmpresaId AS ID_EMPRESA, @Ano AS ANO, @Mes AS MES) AS origem
            ON destino.ID_EMPRESA = origem.ID_EMPRESA
               AND destino.ANO = origem.ANO
               AND destino.MES = origem.MES
            WHEN MATCHED THEN
                UPDATE SET
                    NOME_ARQUIVO = @NomeArquivo,
                    ARQUIVO_PDF = @ArquivoPdf,
                    VALOR_PRINCIPAL = @ValorPrincipal,
                    ICMS_A_RECOLHER = @IcmsARecolher,
                    DIFERENCA = @Diferenca,
                    SITUACAO = @Situacao,
                    CONFERIDO_EM = @ConferidoEm
            WHEN NOT MATCHED THEN
                INSERT (ID_EMPRESA, ANO, MES, NOME_ARQUIVO, ARQUIVO_PDF, VALOR_PRINCIPAL, ICMS_A_RECOLHER, DIFERENCA, SITUACAO, CONFERIDO_EM)
                VALUES (@EmpresaId, @Ano, @Mes, @NomeArquivo, @ArquivoPdf, @ValorPrincipal, @IcmsARecolher, @Diferenca, @Situacao, @ConferidoEm);
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, guia, commandTimeout: 30, cancellationToken: cancellationToken));
    }

    public async Task<DashboardGuiaIcmsPdfDto?> GetGuiaIcmsPdfAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await GarantirTabelaGuiaIcmsPdfAsync(connection, cancellationToken);

        const string sql = """
            SELECT
                ID_EMPRESA AS EmpresaId,
                ANO AS Ano,
                MES AS Mes,
                NOME_ARQUIVO AS NomeArquivo,
                ARQUIVO_PDF AS ArquivoPdf,
                VALOR_PRINCIPAL AS ValorPrincipal,
                ICMS_A_RECOLHER AS IcmsARecolher,
                DIFERENCA AS Diferenca,
                SITUACAO AS Situacao,
                CONFERIDO_EM AS ConferidoEm
            FROM GUIA_ICMS_PDF
            WHERE ID_EMPRESA = @EmpresaId
              AND ANO = @Ano
              AND MES = @Mes;
            """;

        return await connection.QuerySingleOrDefaultAsync<DashboardGuiaIcmsPdfDto>(
            new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DashboardTopFornecedorEntradaDto>> GetTopFornecedoresEntradaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var fornecedores = await connection.QueryAsync<DashboardTopFornecedorEntradaDto>(
            new CommandDefinition(TopFornecedoresEntradaSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return fornecedores.AsList();
    }

    public async Task<IReadOnlyList<DashboardTopClienteSaidaDto>> GetTopClientesSaidaAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var clientes = await connection.QueryAsync<DashboardTopClienteSaidaDto>(
            new CommandDefinition(TopClientesSaidaSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return clientes.AsList();
    }

    public async Task<IReadOnlyList<DashboardProdutoOperacaoDto>> GetProdutosPorOperacaoAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        var produtos = await connection.QueryAsync<DashboardProdutoOperacaoDto>(
            new CommandDefinition(ProdutosPorOperacaoSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, commandTimeout: 30, cancellationToken: cancellationToken));

        return produtos.AsList();
    }

    private static Task GarantirColunasSimplesNacionalAsync(System.Data.IDbConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_NACIONAL') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_NACIONAL BIT NULL;

            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_CONSULTADO_EM') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_CONSULTADO_EM DATETIME2 NULL;

            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_MENSAGEM') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_MENSAGEM NVARCHAR(300) NULL;
            """;

        return connection.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken));
    }

    private static Task GarantirTabelaGuiaIcmsPdfAsync(System.Data.IDbConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID('GUIA_ICMS_PDF', 'U') IS NULL
            BEGIN
                CREATE TABLE GUIA_ICMS_PDF (
                    ID_GUIA_ICMS_PDF INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ID_EMPRESA INT NOT NULL,
                    ANO INT NOT NULL,
                    MES INT NOT NULL,
                    NOME_ARQUIVO NVARCHAR(260) NOT NULL,
                    ARQUIVO_PDF VARBINARY(MAX) NOT NULL,
                    VALOR_PRINCIPAL DECIMAL(18, 2) NOT NULL,
                    ICMS_A_RECOLHER DECIMAL(18, 2) NOT NULL,
                    DIFERENCA DECIMAL(18, 2) NOT NULL,
                    SITUACAO NVARCHAR(30) NOT NULL,
                    CONFERIDO_EM DATETIME2 NOT NULL,
                    CONSTRAINT UX_GUIA_ICMS_PDF UNIQUE (ID_EMPRESA, ANO, MES)
                );
            END;
            """;

        return connection.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken));
    }

    private const string CfopsTransferenciaSql = "'1151','1152','1408','2151','2152','2408','2409','5151','5152','5408','5409','6151','6152','6408','6409'";

    private const string TopProdutosSaidaSql = $"""
        WITH ItensSaida AS (
            SELECT
                COALESCE(NULLIF(c170.COD_ITEM, ''), '(sem codigo)') AS CodigoProduto,
                COALESCE(NULLIF(p.DESCRICAO, ''), NULLIF(c170.DESCR_COMPL, ''), '(produto nao informado)') AS DescricaoProduto,
                COALESCE(c170.VL_ITEM, 0) AS ValorItem
            FROM SPED_ARQUIVO a
            INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
            INNER JOIN SPED_C170 c170 ON c170.ID_C100 = c100.ID_C100
            LEFT JOIN PRODUTO p ON p.ID_EMPRESA = a.ID_EMPRESA AND p.COD_ITEM = c170.COD_ITEM
            WHERE a.ID_EMPRESA = @EmpresaId
              AND YEAR(a.DT_INI) = @Ano
              AND MONTH(a.DT_INI) = @Mes
              AND c100.IND_OPER = '1'
              AND LTRIM(RTRIM(COALESCE(c170.CFOP, ''))) NOT IN ({CfopsTransferenciaSql})
        ),
        Totais AS (
            SELECT
                CodigoProduto,
                MAX(DescricaoProduto) AS DescricaoProduto,
                SUM(ValorItem) AS ValorSaida
            FROM ItensSaida
            GROUP BY CodigoProduto
        ),
        TotalGeral AS (
            SELECT SUM(ValorSaida) AS ValorTotalSaida
            FROM Totais
        )
        SELECT TOP 10
            t.CodigoProduto,
            t.DescricaoProduto,
            t.ValorSaida,
            CASE WHEN COALESCE(g.ValorTotalSaida, 0) = 0 THEN 0 ELSE t.ValorSaida / g.ValorTotalSaida * 100 END AS PercentualSaida
        FROM Totais t
        CROSS JOIN TotalGeral g
        ORDER BY t.ValorSaida DESC, t.DescricaoProduto;
        """;

    private const string TopFornecedoresEntradaSql = $"""
        WITH ItensEntrada AS (
            SELECT
                COALESCE(NULLIF(c100.COD_PART, ''), '(sem codigo)') AS CodigoParticipante,
                COALESCE(NULLIF(p.NOME, ''), '(fornecedor nao informado)') AS NomeParticipante,
                c100.ID_C100 AS NotaId,
                COALESCE(c170.VL_ITEM, 0) AS ValorItem
            FROM SPED_ARQUIVO a
            INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
            INNER JOIN SPED_C170 c170 ON c170.ID_C100 = c100.ID_C100
            LEFT JOIN PARTICIPANTE p ON p.ID_EMPRESA = a.ID_EMPRESA AND p.COD_PART = c100.COD_PART
            WHERE a.ID_EMPRESA = @EmpresaId
              AND YEAR(a.DT_INI) = @Ano
              AND MONTH(a.DT_INI) = @Mes
              AND c100.IND_OPER = '0'
              AND LTRIM(RTRIM(COALESCE(c170.CFOP, ''))) NOT IN ({CfopsTransferenciaSql})
              AND NOT EXISTS (
                  SELECT 1
                  FROM SPED_C190 c190
                  WHERE c190.ID_C100 = c100.ID_C100
                    AND LTRIM(RTRIM(COALESCE(c190.CFOP, ''))) IN ({CfopsTransferenciaSql})
              )
        )
        SELECT TOP 10
            CodigoParticipante,
            MAX(NomeParticipante) AS NomeParticipante,
            COUNT(DISTINCT NotaId) AS QuantidadeNotas,
            SUM(ValorItem) AS ValorEntrada
        FROM ItensEntrada
        GROUP BY CodigoParticipante
        ORDER BY SUM(ValorItem) DESC, MAX(NomeParticipante);
        """;

    private const string TopClientesSaidaSql = $"""
        WITH ItensSaida AS (
            SELECT
                COALESCE(NULLIF(c100.COD_PART, ''), '(sem codigo)') AS CodigoParticipante,
                COALESCE(NULLIF(p.NOME, ''), '(cliente nao informado)') AS NomeParticipante,
                c100.ID_C100 AS NotaId,
                COALESCE(c170.VL_ITEM, 0) AS ValorItem
            FROM SPED_ARQUIVO a
            INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
            INNER JOIN SPED_C170 c170 ON c170.ID_C100 = c100.ID_C100
            LEFT JOIN PARTICIPANTE p ON p.ID_EMPRESA = a.ID_EMPRESA AND p.COD_PART = c100.COD_PART
            WHERE a.ID_EMPRESA = @EmpresaId
              AND YEAR(a.DT_INI) = @Ano
              AND MONTH(a.DT_INI) = @Mes
              AND c100.IND_OPER = '1'
              AND LTRIM(RTRIM(COALESCE(c170.CFOP, ''))) NOT IN ({CfopsTransferenciaSql})
        )
        SELECT TOP 10
            CodigoParticipante,
            MAX(NomeParticipante) AS NomeParticipante,
            COUNT(DISTINCT NotaId) AS QuantidadeNotas,
            SUM(ValorItem) AS ValorSaida
        FROM ItensSaida
        GROUP BY CodigoParticipante
        ORDER BY SUM(ValorItem) DESC, MAX(NomeParticipante);
        """;

    private const string ProdutosPorOperacaoSql = """
        SELECT
            c100.IND_OPER AS IndicadorOperacao,
            c170.CFOP AS Cfop,
            COALESCE(NULLIF(c170.COD_ITEM, ''), '(sem codigo)') AS CodigoProduto,
            COALESCE(NULLIF(p.DESCRICAO, ''), NULLIF(c170.DESCR_COMPL, ''), '(produto nao informado)') AS DescricaoProduto,
            COALESCE(NULLIF(c170.UNID, ''), '-') AS Unidade,
            SUM(COALESCE(c170.QTD, 0)) AS Quantidade,
            SUM(COALESCE(c170.VL_ITEM, 0)) AS ValorOperacao
        FROM SPED_ARQUIVO a
        INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
        INNER JOIN SPED_C170 c170 ON c170.ID_C100 = c100.ID_C100
        LEFT JOIN PRODUTO p ON p.ID_EMPRESA = a.ID_EMPRESA AND p.COD_ITEM = c170.COD_ITEM
        WHERE a.ID_EMPRESA = @EmpresaId
          AND YEAR(a.DT_INI) = @Ano
          AND MONTH(a.DT_INI) = @Mes
        GROUP BY
            c100.IND_OPER,
            c170.CFOP,
            COALESCE(NULLIF(c170.COD_ITEM, ''), '(sem codigo)'),
            COALESCE(NULLIF(p.DESCRICAO, ''), NULLIF(c170.DESCR_COMPL, ''), '(produto nao informado)'),
            COALESCE(NULLIF(c170.UNID, ''), '-')
        ORDER BY SUM(COALESCE(c170.VL_ITEM, 0)) DESC, DescricaoProduto;
        """;

    public async Task<DashboardFiscalDto> GetDashboardAsync(int empresaId, DateTime competencia, CancellationToken cancellationToken = default)
    {
        var inicio = new DateTime(competencia.Year, competencia.Month, 1);
        var fim = inicio.AddMonths(1);
        var parametros = new { EmpresaId = empresaId, Inicio = inicio, Fim = fim };

        await using var connection = _connectionFactory.CreateConnection();

        var resumo = await connection.QuerySingleAsync<DashboardFiscalResumoDto>(
            new CommandDefinition(ResumoSql, parametros, cancellationToken: cancellationToken));

        var resumoPorCfopCst = await connection.QueryAsync<DashboardFiscalCfopCstDto>(
            new CommandDefinition(ResumoPorCfopCstSql, parametros, cancellationToken: cancellationToken));

        return new DashboardFiscalDto
        {
            Resumo = resumo,
            ResumoPorCfopCst = resumoPorCfopCst.AsList()
        };
    }



    private const string FornecedoresIcmsZeradoMesesAnterioresSql = """
        WITH ParticipantesComCnpj AS (
            SELECT DISTINCT COD_PART
            FROM PARTICIPANTE
            WHERE ID_EMPRESA = @EmpresaId
              AND NULLIF(CNPJ, '') IS NOT NULL
        ),
        EntradasAnteriores AS (
            SELECT
                c100.ID_C100,
                c100.COD_PART,
                COALESCE(c100.VL_DOC, 0) AS ValorDocumento
            FROM SPED_ARQUIVO a
            INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
            INNER JOIN ParticipantesComCnpj p ON p.COD_PART = c100.COD_PART
            WHERE a.ID_EMPRESA = @EmpresaId
              AND YEAR(a.DT_INI) = @Ano
              AND MONTH(a.DT_INI) < @Mes
              AND c100.IND_OPER = '0'
              AND NULLIF(c100.COD_PART, '') IS NOT NULL
        ),
        Valores AS (
            SELECT
                e.COD_PART,
                SUM(e.ValorDocumento) AS ValorEntrada,
                SUM(COALESCE(c190.VL_ICMS, 0)) AS ValorIcmsCredito
            FROM EntradasAnteriores e
            LEFT JOIN SPED_C190 c190 ON c190.ID_C100 = e.ID_C100
            GROUP BY e.COD_PART
        )
        SELECT COD_PART
        FROM Valores
        WHERE ValorEntrada > 0
          AND ValorIcmsCredito = 0
        ORDER BY COD_PART;
        """;
    private const string EntradasPorParticipanteSql = """
        WITH Entradas AS (
            SELECT
                c100.ID_C100,
                c100.COD_PART,
                c100.VL_DOC
            FROM SPED_ARQUIVO a
            INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
            WHERE a.ID_EMPRESA = @EmpresaId
              AND YEAR(a.DT_INI) = @Ano
              AND MONTH(a.DT_INI) = @Mes
              AND c100.IND_OPER = '0'
        ),
        Participantes AS (
            SELECT
                COD_PART,
                MIN(ID_PARTICIPANTE) AS ParticipanteId,
                MAX(NULLIF(NOME, '')) AS NOME,
                MAX(NULLIF(CNPJ, '')) AS CNPJ,
                CASE
                    WHEN MAX(CASE WHEN SIMPLES_NACIONAL = 1 THEN 1 ELSE 0 END) = 1 THEN CAST(1 AS bit)
                    WHEN MAX(SIMPLES_CONSULTADO_EM) IS NOT NULL THEN CAST(0 AS bit)
                    ELSE CAST(NULL AS bit)
                END AS SimplesNacional,
                MAX(SIMPLES_CONSULTADO_EM) AS SimplesNacionalConsultadoEm,
                MAX(NULLIF(SIMPLES_MENSAGEM, '')) AS SimplesNacionalMensagem
            FROM PARTICIPANTE
            WHERE ID_EMPRESA = @EmpresaId
            GROUP BY COD_PART
        ),
        ValoresDocumento AS (
            SELECT
                COD_PART,
                SUM(COALESCE(VL_DOC, 0)) AS ValorEntrada
            FROM Entradas
            GROUP BY COD_PART
        ),
        ValoresIcms AS (
            SELECT
                e.COD_PART,
                SUM(COALESCE(c190.VL_BC_ICMS, 0)) AS BaseCalculo,
                SUM(COALESCE(c190.VL_ICMS, 0)) AS ValorIcmsCredito
            FROM Entradas e
            LEFT JOIN SPED_C190 c190 ON c190.ID_C100 = e.ID_C100
            GROUP BY e.COD_PART
        )
        SELECT
            COALESCE(p.ParticipanteId, 0) AS ParticipanteId,
            COALESCE(vd.COD_PART, '') AS CodigoParticipante,
            COALESCE(p.NOME, '(Participante nao informado)') AS NomeParticipante,
            p.CNPJ AS Cnpj,
            p.SimplesNacional,
            p.SimplesNacionalConsultadoEm,
            p.SimplesNacionalMensagem,
            COALESCE(vd.ValorEntrada, 0) AS ValorEntrada,
            COALESCE(vi.BaseCalculo, 0) AS BaseCalculo,
            COALESCE(vi.ValorIcmsCredito, 0) AS ValorIcmsCredito,
            CASE WHEN COALESCE(vd.ValorEntrada, 0) = 0 THEN 0 ELSE COALESCE(vi.ValorIcmsCredito, 0) / vd.ValorEntrada * 100 END AS AliquotaEfetivaCredito,
            CASE WHEN COALESCE(vi.BaseCalculo, 0) = 0 THEN 0 ELSE COALESCE(vi.ValorIcmsCredito, 0) / vi.BaseCalculo * 100 END AS AliquotaIcms
        FROM ValoresDocumento vd
        LEFT JOIN ValoresIcms vi ON vi.COD_PART = vd.COD_PART
        LEFT JOIN Participantes p ON p.COD_PART = vd.COD_PART
        ORDER BY COALESCE(vd.ValorEntrada, 0) DESC, COALESCE(p.NOME, vd.COD_PART);
        """;

    private const string NotasEntradaFornecedorSql = """
        WITH Participantes AS (
            SELECT
                COD_PART,
                MAX(NULLIF(NOME, '')) AS NOME,
                MAX(NULLIF(CNPJ, '')) AS CNPJ
            FROM PARTICIPANTE
            WHERE ID_EMPRESA = @EmpresaId
            GROUP BY COD_PART
        ),
        ValoresIcms AS (
            SELECT
                c190.ID_C100,
                SUM(COALESCE(c190.VL_BC_ICMS, 0)) AS ValorBaseIcms,
                SUM(COALESCE(c190.VL_ICMS, 0)) AS ValorIcmsCredito
            FROM SPED_C190 c190
            GROUP BY c190.ID_C100
        )
        SELECT
            c100.ID_C100 AS SpedC100Id,
            c100.COD_PART AS CodigoParticipante,
            COALESCE(p.NOME, '(Fornecedor nao informado)') AS NomeParticipante,
            p.CNPJ AS Cnpj,
            c100.COD_MOD AS CodigoModelo,
            c100.COD_SIT AS CodigoSituacao,
            c100.SER AS Serie,
            c100.NUM_DOC AS NumeroDocumento,
            c100.CHV_NFE AS ChaveNfe,
            c100.DT_DOC AS DataDocumento,
            c100.DT_E_S AS DataEntradaSaida,
            COALESCE(c100.VL_DOC, 0) AS ValorDocumento,
            COALESCE(c100.VL_MERC, 0) AS ValorMercadoria,
            COALESCE(vi.ValorBaseIcms, 0) AS ValorBaseIcms,
            COALESCE(vi.ValorIcmsCredito, 0) AS ValorIcmsCredito,
            CASE WHEN COALESCE(c100.VL_DOC, 0) = 0 THEN 0 ELSE COALESCE(vi.ValorIcmsCredito, 0) / c100.VL_DOC * 100 END AS AliquotaEfetivaCredito
        FROM SPED_ARQUIVO a
        INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
        LEFT JOIN Participantes p ON p.COD_PART = c100.COD_PART
        LEFT JOIN ValoresIcms vi ON vi.ID_C100 = c100.ID_C100
        WHERE a.ID_EMPRESA = @EmpresaId
          AND YEAR(a.DT_INI) = @Ano
          AND MONTH(a.DT_INI) = @Mes
          AND c100.IND_OPER = '0'
          AND c100.COD_PART = @CodigoParticipante
        ORDER BY COALESCE(c100.DT_DOC, c100.DT_E_S), c100.NUM_DOC;
        """;

    private const string NotasEntradaFornecedoresSql = """
        WITH Participantes AS (
            SELECT
                COD_PART,
                MAX(NULLIF(NOME, '')) AS NOME,
                MAX(NULLIF(CNPJ, '')) AS CNPJ
            FROM PARTICIPANTE
            WHERE ID_EMPRESA = @EmpresaId
            GROUP BY COD_PART
        ),
        ValoresIcms AS (
            SELECT
                c190.ID_C100,
                SUM(COALESCE(c190.VL_BC_ICMS, 0)) AS ValorBaseIcms,
                SUM(COALESCE(c190.VL_ICMS, 0)) AS ValorIcmsCredito
            FROM SPED_C190 c190
            GROUP BY c190.ID_C100
        )
        SELECT
            c100.ID_C100 AS SpedC100Id,
            c100.COD_PART AS CodigoParticipante,
            COALESCE(p.NOME, '(Fornecedor nao informado)') AS NomeParticipante,
            p.CNPJ AS Cnpj,
            c100.COD_MOD AS CodigoModelo,
            c100.COD_SIT AS CodigoSituacao,
            c100.SER AS Serie,
            c100.NUM_DOC AS NumeroDocumento,
            c100.CHV_NFE AS ChaveNfe,
            c100.DT_DOC AS DataDocumento,
            c100.DT_E_S AS DataEntradaSaida,
            COALESCE(c100.VL_DOC, 0) AS ValorDocumento,
            COALESCE(c100.VL_MERC, 0) AS ValorMercadoria,
            COALESCE(vi.ValorBaseIcms, 0) AS ValorBaseIcms,
            COALESCE(vi.ValorIcmsCredito, 0) AS ValorIcmsCredito,
            CASE WHEN COALESCE(c100.VL_DOC, 0) = 0 THEN 0 ELSE COALESCE(vi.ValorIcmsCredito, 0) / c100.VL_DOC * 100 END AS AliquotaEfetivaCredito
        FROM SPED_ARQUIVO a
        INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
        LEFT JOIN Participantes p ON p.COD_PART = c100.COD_PART
        LEFT JOIN ValoresIcms vi ON vi.ID_C100 = c100.ID_C100
        WHERE a.ID_EMPRESA = @EmpresaId
          AND YEAR(a.DT_INI) = @Ano
          AND MONTH(a.DT_INI) = @Mes
          AND c100.IND_OPER = '0'
        ORDER BY COALESCE(p.NOME, c100.COD_PART), COALESCE(c100.DT_DOC, c100.DT_E_S), c100.NUM_DOC;
        """;
    private const string DashboardAnualSql = """
        WITH Meses AS (
            SELECT Mes
            FROM (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11), (12)) AS M(Mes)
        )
        SELECT
            m.Mes,
            CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM SPED_ARQUIVO a
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ) THEN 1 ELSE 0 END AS bit) AS ArquivoImportado,
            (
                SELECT TOP 1 a.NOME_ARQUIVO
                FROM SPED_ARQUIVO a
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
                ORDER BY a.DATA_IMPORTACAO DESC
            ) AS NomeArquivo,
            (
                SELECT TOP 1 a.DATA_IMPORTACAO
                FROM SPED_ARQUIVO a
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
                ORDER BY a.DATA_IMPORTACAO DESC
            ) AS DataImportacao,
            COALESCE((
                SELECT SUM(CASE WHEN c100.IND_OPER = '0' THEN c100.VL_DOC ELSE 0 END)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS ValorTotalEntradas,
            COALESCE((
                SELECT SUM(CASE WHEN c100.IND_OPER = '1' THEN c100.VL_DOC ELSE 0 END)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS ValorTotalSaidas,
            COALESCE((
                SELECT SUM(c190.VL_BC_ICMS)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100.ID_C100
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS BaseIcms,
            COALESCE((
                SELECT SUM(CASE WHEN c100.IND_OPER = '1' THEN c190.VL_ICMS ELSE 0 END)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100.ID_C100
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS IcmsDebitado,
            COALESCE((
                SELECT SUM(CASE WHEN c100.IND_OPER = '0' THEN c190.VL_ICMS ELSE 0 END)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100.ID_C100
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS IcmsCreditado,
            COALESCE((
                SELECT SUM(e110.VL_ICMS_RECOLHER)
                FROM SPED_ARQUIVO a
                INNER JOIN SPED_E110 e110 ON e110.ID_ARQUIVO = a.ID_ARQUIVO
                WHERE a.ID_EMPRESA = @EmpresaId
                  AND YEAR(a.DT_INI) = @Ano
                  AND MONTH(a.DT_INI) = m.Mes
            ), 0) AS IcmsARecolher,
            guia.VALOR_PRINCIPAL AS ValorPrincipalGuiaIcms,
            guia.SITUACAO AS SituacaoGuiaIcms
        FROM Meses m
        LEFT JOIN GUIA_ICMS_PDF guia ON guia.ID_EMPRESA = @EmpresaId
            AND guia.ANO = @Ano
            AND guia.MES = m.Mes
        ORDER BY m.Mes;
        """;

    private const string ResumoSql = """
        SELECT
            COALESCE(SUM(CASE WHEN c100.IND_OPER = '0' THEN c100.VL_DOC ELSE 0 END), 0) AS ValorTotalEntradas,
            COALESCE(SUM(CASE WHEN c100.IND_OPER = '1' THEN c100.VL_DOC ELSE 0 END), 0) AS ValorTotalSaidas,
            COALESCE((
                SELECT SUM(c190.VL_BC_ICMS)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_C100 c100_2 ON c100_2.ID_ARQUIVO = a2.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100_2.ID_C100
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
            ), 0) AS BaseIcms,
            COALESCE((
                SELECT SUM(CASE WHEN c100_2.IND_OPER = '1' THEN c190.VL_ICMS ELSE 0 END)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_C100 c100_2 ON c100_2.ID_ARQUIVO = a2.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100_2.ID_C100
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
            ), 0) AS IcmsDebitado,
            COALESCE((
                SELECT SUM(CASE WHEN c100_2.IND_OPER = '0' THEN c190.VL_ICMS ELSE 0 END)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_C100 c100_2 ON c100_2.ID_ARQUIVO = a2.ID_ARQUIVO
                INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100_2.ID_C100
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
            ), 0) AS IcmsCreditado,
            COALESCE((
                SELECT SUM(e110.VL_ICMS_RECOLHER)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_E110 e110 ON e110.ID_ARQUIVO = a2.ID_ARQUIVO
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
            ), 0) AS IcmsARecolher,
            COALESCE((
                SELECT SUM(e111.VL_AJ_APUR)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_E110 e110 ON e110.ID_ARQUIVO = a2.ID_ARQUIVO
                INNER JOIN SPED_E111 e111 ON e111.ID_E110 = e110.ID_E110
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
                  AND (
                    e111.COD_AJ_APUR = 'RN022009'
                    OR UPPER(e111.DESCR_COMPL_AJ) LIKE '%ICMS%ANTECIP%'
                  )
            ), 0) AS IcmsAntecipado,
            COALESCE((
                SELECT SUM(e111.VL_AJ_APUR)
                FROM SPED_ARQUIVO a2
                INNER JOIN SPED_E110 e110 ON e110.ID_ARQUIVO = a2.ID_ARQUIVO
                INNER JOIN SPED_E111 e111 ON e111.ID_E110 = e110.ID_E110
                WHERE a2.ID_EMPRESA = @EmpresaId
                  AND a2.DT_INI >= @Inicio
                  AND a2.DT_INI < @Fim
                  AND e111.COD_AJ_APUR = 'RN022037'
            ), 0) AS IcmsCreditoEstoque
        FROM SPED_ARQUIVO a
        LEFT JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
        WHERE a.ID_EMPRESA = @EmpresaId
          AND a.DT_INI >= @Inicio
          AND a.DT_INI < @Fim;
        """;

    private const string ResumoPorCfopCstSql = """
        SELECT
            c100.IND_OPER AS IndicadorOperacao,
            c190.CFOP AS Cfop,
            c190.CST_ICMS AS CstIcms,
            COALESCE(SUM(c190.VL_OPR), 0) AS ValorOperacao,
            COALESCE(SUM(c190.VL_BC_ICMS), 0) AS BaseIcms,
            COALESCE(SUM(c190.VL_ICMS), 0) AS ValorIcms
        FROM SPED_ARQUIVO a
        INNER JOIN SPED_C100 c100 ON c100.ID_ARQUIVO = a.ID_ARQUIVO
        INNER JOIN SPED_C190 c190 ON c190.ID_C100 = c100.ID_C100
        WHERE a.ID_EMPRESA = @EmpresaId
          AND a.DT_INI >= @Inicio
          AND a.DT_INI < @Fim
        GROUP BY c100.IND_OPER, c190.CFOP, c190.CST_ICMS
        ORDER BY c100.IND_OPER, c190.CFOP, c190.CST_ICMS;
        """;
}



