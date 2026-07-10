using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedC100Repository
{
    Task<int> InsertAsync(SpedC100 spedC100, CancellationToken cancellationToken = default);
}

public sealed class SpedC100Repository : RepositoryBase, ISpedC100Repository
{
    public SpedC100Repository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(SpedC100 spedC100, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_C100 (ID_ARQUIVO, ID_EMPRESA, IND_OPER, IND_EMIT, COD_PART, COD_MOD, COD_SIT, SER, NUM_DOC, CHV_NFE, DT_DOC, DT_E_S, VL_DOC, VL_DESC, VL_MERC, VL_FRT, VL_SEG, VL_OUT_DA, VL_ICMS, VL_ICMS_ST, VL_IPI, VL_PIS, VL_COFINS)
            OUTPUT INSERTED.ID_C100
            VALUES (@SpedArquivoId, @EmpresaId, @IndicadorOperacao, @IndicadorEmitente, @CodigoParticipante, @CodigoModelo, @CodigoSituacao, @Serie, @NumeroDocumento, @ChaveNfe, @DataDocumento, @DataEntradaSaida, @ValorDocumento, @ValorDesconto, @ValorMercadoria, @ValorFrete, @ValorSeguro, @ValorOutrasDespesas, @ValorIcms, @ValorIcmsSt, @ValorIpi, @ValorPis, @ValorCofins);
            """;

        return InsertAsync(sql, spedC100, cancellationToken);
    }
}
