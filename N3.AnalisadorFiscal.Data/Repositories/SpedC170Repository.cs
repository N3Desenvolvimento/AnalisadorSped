using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedC170Repository
{
    Task<int> InsertAsync(SpedC170 spedC170, CancellationToken cancellationToken = default);
}

public sealed class SpedC170Repository : RepositoryBase, ISpedC170Repository
{
    public SpedC170Repository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(SpedC170 spedC170, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_C170 (ID_C100, NUM_ITEM, COD_ITEM, DESCR_COMPL, QTD, UNID, VL_ITEM, VL_DESC, CST_ICMS, CFOP, NAT_BC_CRED, VL_BC_ICMS, ALIQ_ICMS, VL_ICMS, VL_BC_ICMS_ST, ALIQ_ST, VL_ICMS_ST, CST_IPI, VL_BC_IPI, ALIQ_IPI, VL_IPI)
            OUTPUT INSERTED.ID_C170
            VALUES (@SpedC100Id, TRY_CONVERT(int, @NumeroItem), @CodigoItem, @DescricaoComplementar, @Quantidade, @Unidade, @ValorItem, @ValorDesconto, @CstIcms, @Cfop, @NaturezaBcIcms, @ValorBcIcms, @AliquotaIcms, @ValorIcms, @ValorBcIcmsSt, @AliquotaIcmsSt, @ValorIcmsSt, @CstIpi, @ValorBcIpi, @AliquotaIpi, @ValorIpi);
            """;

        return InsertAsync(sql, spedC170, cancellationToken);
    }
}
