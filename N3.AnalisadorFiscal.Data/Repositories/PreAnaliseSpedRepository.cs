using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IPreAnaliseSpedRepository
{
    Task<int> SalvarAsync(PreAnaliseSpedDados dados, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreAnaliseRegraItemDto>> GetRegrasAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreAnaliseRegraItemDto>> GetRegrasAtivasAsync(string? ufDestino, DateTime competencia, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreAnaliseSpedResumoDto>> GetPreAnalisesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasAsync(int preAnaliseSpedId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardNotaEntradaItemDto>> GetItensAsync(int preAnaliseSpedNotaId, CancellationToken cancellationToken = default);
    Task<bool> ExcluirAsync(int preAnaliseSpedId, CancellationToken cancellationToken = default);
}

public sealed class PreAnaliseSpedRepository : IPreAnaliseSpedRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PreAnaliseSpedRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> SalvarAsync(PreAnaliseSpedDados dados, CancellationToken cancellationToken = default)
    {
        const string arquivoSql = """
            INSERT INTO PRE_ANALISE_SPED
                (NOME_ARQUIVO,HASH_ARQUIVO,CNPJ,RAZAO_SOCIAL,UF,DT_INI,DT_FIN,STATUS,IMPORTADO_EM,
                 TOTAL_NOTAS_ENTRADA,VALOR_TOTAL_NOTAS,BASE_ICMS_TOTAL,ICMS_CREDITO_TOTAL)
            OUTPUT INSERTED.ID_PRE_ANALISE_SPED
            VALUES (@NomeArquivo,@HashArquivo,@Cnpj,@RazaoSocial,@Uf,@DataInicial,@DataFinal,'IMPORTADO',SYSUTCDATETIME(),
                    @TotalNotas,@ValorTotal,@BaseIcms,@IcmsCredito);
            """;
        const string notaSql = """
            INSERT INTO PRE_ANALISE_SPED_NOTA
                (ID_PRE_ANALISE_SPED,COD_PART,NOME_PARTICIPANTE,CNPJ_PARTICIPANTE,COD_MOD,COD_SIT,SER,
                 NUM_DOC,CHV_NFE,DT_DOC,DT_ENTRADA,VL_DOC,VL_MERC,VL_BC_ICMS,VL_ICMS,STATUS_ANALISE)
            OUTPUT INSERTED.ID_PRE_ANALISE_SPED_NOTA
            VALUES (@PreAnaliseId,@CodigoParticipante,@NomeParticipante,@CnpjParticipante,@CodigoModelo,
                    @CodigoSituacao,@Serie,@NumeroDocumento,@ChaveNfe,@DataDocumento,@DataEntrada,
                    @ValorDocumento,@ValorMercadoria,@ValorBaseIcms,@ValorIcms,'PENDENTE');
            """;
        const string itemSql = """
            INSERT INTO PRE_ANALISE_SPED_ITEM
                (ID_PRE_ANALISE_SPED_NOTA,NUM_ITEM,COD_ITEM,DESCRICAO,CFOP,CST_ICMS,VL_ITEM,
                 VL_BC_ICMS,ALIQ_ICMS,VL_ICMS,NCM,CEST,ID_PRE_ANALISE_REGRA_ITEM,
                 CLASSIFICACAO,NIVEL_CONFIANCA,JUSTIFICATIVA,CREDITO_PERMITIDO,DIFERENCA_CREDITO)
            VALUES (@NotaId,@NumeroItem,@CodigoItem,@Descricao,@Cfop,@CstIcms,@ValorItem,
                    @ValorBaseIcms,@AliquotaIcms,@ValorIcms,@Ncm,@Cest,@PreAnaliseRegraItemId,
                    @Classificacao,@NivelConfianca,@Justificativa,@CreditoPermitido,@DiferencaCredito);
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var preAnaliseId = await connection.QuerySingleAsync<int>(new CommandDefinition(arquivoSql, new
            {
                dados.NomeArquivo, dados.HashArquivo, dados.Empresa.Cnpj,
                dados.Empresa.RazaoSocial, dados.Empresa.Uf,
                DataInicial = dados.Arquivo.PeriodoInicial,
                DataFinal = dados.Arquivo.PeriodoFinal,
                TotalNotas = dados.Notas.Count,
                ValorTotal = dados.Notas.Sum(x => x.Nota.ValorDocumento),
                BaseIcms = dados.Notas.Sum(x => x.ValorBaseIcms),
                IcmsCredito = dados.Notas.Sum(x => x.ValorIcmsCredito)
            }, transaction, commandTimeout: 60, cancellationToken: cancellationToken));

            foreach (var registro in dados.Notas)
            {
                var nota = registro.Nota;
                var notaId = await connection.QuerySingleAsync<int>(new CommandDefinition(notaSql, new
                {
                    PreAnaliseId = preAnaliseId,
                    nota.CodigoParticipante, registro.NomeParticipante,
                    registro.CnpjParticipante, nota.CodigoModelo, nota.CodigoSituacao,
                    nota.Serie, nota.NumeroDocumento, nota.ChaveNfe,
                    nota.DataDocumento, DataEntrada = nota.DataEntradaSaida,
                    nota.ValorDocumento, nota.ValorMercadoria,
                    registro.ValorBaseIcms, ValorIcms = registro.ValorIcmsCredito
                }, transaction, commandTimeout: 60, cancellationToken: cancellationToken));

                foreach (var registroItem in registro.Itens)
                {
                    var item = registroItem.Item;
                    await connection.ExecuteAsync(new CommandDefinition(itemSql, new
                    {
                        NotaId = notaId,
                        NumeroItem = int.TryParse(item.NumeroItem, out var numeroItem) ? numeroItem : (int?)null,
                        item.CodigoItem, registroItem.Descricao, item.Cfop, item.CstIcms,
                        registroItem.Ncm, registroItem.Cest, registroItem.PreAnaliseRegraItemId,
                        registroItem.Classificacao, registroItem.NivelConfianca,
                        registroItem.Justificativa, registroItem.CreditoPermitido,
                        registroItem.DiferencaCredito,
                        ValorItem = item.ValorItem - item.ValorDesconto,
                        ValorBaseIcms = item.ValorBcIcms,
                        AliquotaIcms = item.AliquotaIcms,
                        ValorIcms = item.ValorIcms
                    }, transaction, commandTimeout: 60, cancellationToken: cancellationToken));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return preAnaliseId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PreAnaliseRegraItemDto>> GetRegrasAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ID_PRE_ANALISE_REGRA_ITEM AS PreAnaliseRegraItemId,
                   CODIGO AS Codigo, NOME AS Nome, UF_DESTINO AS UfDestino,
                   CNPJ_FORNECEDOR AS CnpjFornecedor,
                   NCM_PREFIXO AS NcmPrefixo, NCM_EXCECOES AS NcmExcecoes,
                   TERMOS_DESCRICAO AS TermosDescricao, CSTS_APLICAVEIS AS CstsAplicaveis,
                   CFOPS_APLICAVEIS AS CfopsAplicaveis, RESULTADO AS Resultado,
                   PERCENTUAL_CREDITO AS PercentualCredito, PRIORIDADE AS Prioridade,
                   FUNDAMENTO_LEGAL AS FundamentoLegal, VIGENCIA_INICIAL AS VigenciaInicial,
                   VIGENCIA_FINAL AS VigenciaFinal, ATIVA AS Ativa
            FROM PRE_ANALISE_REGRA_ITEM
            ORDER BY ATIVA DESC, PRIORIDADE, CODIGO;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<PreAnaliseRegraItemDto>(new CommandDefinition(
            sql, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<PreAnaliseRegraItemDto>> GetRegrasAtivasAsync(
        string? ufDestino, DateTime competencia, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ID_PRE_ANALISE_REGRA_ITEM AS PreAnaliseRegraItemId,
                   CODIGO AS Codigo, NOME AS Nome, UF_DESTINO AS UfDestino,
                   CNPJ_FORNECEDOR AS CnpjFornecedor,
                   NCM_PREFIXO AS NcmPrefixo, NCM_EXCECOES AS NcmExcecoes,
                   TERMOS_DESCRICAO AS TermosDescricao, CSTS_APLICAVEIS AS CstsAplicaveis,
                   CFOPS_APLICAVEIS AS CfopsAplicaveis, RESULTADO AS Resultado,
                   PERCENTUAL_CREDITO AS PercentualCredito, PRIORIDADE AS Prioridade,
                   FUNDAMENTO_LEGAL AS FundamentoLegal, VIGENCIA_INICIAL AS VigenciaInicial,
                   VIGENCIA_FINAL AS VigenciaFinal
            FROM PRE_ANALISE_REGRA_ITEM
            WHERE ATIVA = 1
              AND (UF_DESTINO IS NULL OR UF_DESTINO = @UfDestino)
              AND VIGENCIA_INICIAL <= @Competencia
              AND (VIGENCIA_FINAL IS NULL OR VIGENCIA_FINAL >= @Competencia)
            ORDER BY PRIORIDADE, ID_PRE_ANALISE_REGRA_ITEM;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<PreAnaliseRegraItemDto>(new CommandDefinition(
            sql, new { UfDestino = ufDestino, Competencia = competencia.Date },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<PreAnaliseSpedResumoDto>> GetPreAnalisesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT P.ID_PRE_ANALISE_SPED AS PreAnaliseSpedId,
                   P.NOME_ARQUIVO AS NomeArquivo,
                   COALESCE(P.CNPJ, '') AS Cnpj,
                   COALESCE(P.RAZAO_SOCIAL, '') AS RazaoSocial,
                   P.DT_INI AS PeriodoInicial,
                   P.DT_FIN AS PeriodoFinal,
                   P.IMPORTADO_EM AS ImportadoEm,
                   P.TOTAL_NOTAS_ENTRADA AS TotalNotas,
                   (SELECT COUNT(*)
                    FROM PRE_ANALISE_SPED_ITEM I
                    INNER JOIN PRE_ANALISE_SPED_NOTA N
                        ON N.ID_PRE_ANALISE_SPED_NOTA = I.ID_PRE_ANALISE_SPED_NOTA
                    WHERE N.ID_PRE_ANALISE_SPED = P.ID_PRE_ANALISE_SPED) AS TotalItens,
                   P.VALOR_TOTAL_NOTAS AS ValorTotalNotas,
                   P.BASE_ICMS_TOTAL AS BaseIcmsTotal,
                   P.ICMS_CREDITO_TOTAL AS IcmsCreditoTotal
            FROM PRE_ANALISE_SPED P
            ORDER BY P.IMPORTADO_EM DESC, P.ID_PRE_ANALISE_SPED DESC;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<PreAnaliseSpedResumoDto>(new CommandDefinition(
            sql, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<DashboardNotaEntradaFornecedorDto>> GetNotasAsync(
        int preAnaliseSpedId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT N.ID_PRE_ANALISE_SPED_NOTA AS SpedC100Id, N.COD_PART AS CodigoParticipante,
                   N.NOME_PARTICIPANTE AS NomeParticipante, N.CNPJ_PARTICIPANTE AS Cnpj,
                   N.COD_MOD AS CodigoModelo, N.COD_SIT AS CodigoSituacao, N.SER AS Serie,
                   N.NUM_DOC AS NumeroDocumento, N.CHV_NFE AS ChaveNfe, N.DT_DOC AS DataDocumento,
                   N.DT_ENTRADA AS DataEntradaSaida, N.VL_DOC AS ValorDocumento,
                   N.VL_MERC AS ValorMercadoria, N.VL_BC_ICMS AS ValorBaseIcms,
                   N.VL_ICMS AS ValorIcmsCredito,
                   CASE WHEN N.VL_ICMS - A.DiferencaCredito < 0 THEN 0
                        ELSE N.VL_ICMS - A.DiferencaCredito END AS ValorIcmsCreditoPermitido,
                   A.DiferencaCredito AS DiferencaIcmsCredito,
                   A.ItensComDivergencia, A.ItensParaRevisao,
                   CASE WHEN N.VL_DOC = 0 THEN 0 ELSE N.VL_ICMS / N.VL_DOC * 100 END AS AliquotaEfetivaCredito
            FROM PRE_ANALISE_SPED_NOTA N
            OUTER APPLY (
                SELECT COALESCE(SUM(CASE WHEN I.DIFERENCA_CREDITO > 0 THEN I.DIFERENCA_CREDITO ELSE 0 END),0) AS DiferencaCredito,
                       COALESCE(SUM(CASE WHEN I.DIFERENCA_CREDITO > 0 THEN 1 ELSE 0 END),0) AS ItensComDivergencia,
                       COALESCE(SUM(CASE WHEN I.CLASSIFICACAO = 'REVISAR' THEN 1 ELSE 0 END),0) AS ItensParaRevisao
                FROM PRE_ANALISE_SPED_ITEM I
                WHERE I.ID_PRE_ANALISE_SPED_NOTA = N.ID_PRE_ANALISE_SPED_NOTA
            ) A
            WHERE N.ID_PRE_ANALISE_SPED = @PreAnaliseSpedId
            ORDER BY COALESCE(N.DT_ENTRADA, N.DT_DOC), N.NUM_DOC;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DashboardNotaEntradaFornecedorDto>(new CommandDefinition(
            sql, new { PreAnaliseSpedId = preAnaliseSpedId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<DashboardNotaEntradaItemDto>> GetItensAsync(
        int preAnaliseSpedNotaId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT I.ID_PRE_ANALISE_SPED_ITEM AS SpedC170Id, COALESCE(I.NUM_ITEM,0) AS NumeroItem,
                   COALESCE(I.COD_ITEM,'') AS CodigoItem, COALESCE(I.DESCRICAO,'') AS Descricao,
                   COALESCE(I.NCM,'') AS Ncm, COALESCE(I.CEST,'') AS Cest,
                   COALESCE(I.CFOP,'') AS Cfop, COALESCE(I.CST_ICMS,'') AS CstIcms,
                   I.VL_ITEM AS ValorItem, I.VL_BC_ICMS AS ValorBaseIcms,
                   I.ALIQ_ICMS AS AliquotaIcms, I.VL_ICMS AS ValorIcmsCredito,
                   COALESCE(I.CLASSIFICACAO,'PENDENTE') AS Classificacao,
                   COALESCE(I.NIVEL_CONFIANCA,'') AS NivelConfianca,
                   COALESCE(I.JUSTIFICATIVA,'') AS Justificativa,
                   I.CREDITO_PERMITIDO AS CreditoPermitido,
                   I.DIFERENCA_CREDITO AS DiferencaCredito,
                   COALESCE(R.CODIGO,'') AS RegraCodigo,
                   COALESCE(R.NOME,'') AS RegraNome
            FROM PRE_ANALISE_SPED_ITEM I
            LEFT JOIN PRE_ANALISE_REGRA_ITEM R
                ON R.ID_PRE_ANALISE_REGRA_ITEM = I.ID_PRE_ANALISE_REGRA_ITEM
            WHERE I.ID_PRE_ANALISE_SPED_NOTA = @PreAnaliseSpedNotaId
            ORDER BY COALESCE(I.NUM_ITEM,0), I.ID_PRE_ANALISE_SPED_ITEM;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DashboardNotaEntradaItemDto>(new CommandDefinition(
            sql, new { PreAnaliseSpedNotaId = preAnaliseSpedNotaId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> ExcluirAsync(int preAnaliseSpedId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM PRE_ANALISE_SPED
            WHERE ID_PRE_ANALISE_SPED = @PreAnaliseSpedId;
            """;
        await using var connection = _connectionFactory.CreateConnection();
        var registrosExcluidos = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { PreAnaliseSpedId = preAnaliseSpedId }, cancellationToken: cancellationToken));
        return registrosExcluidos > 0;
    }
}
