using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedC190Repository
{
    Task<int> InsertAsync(SpedC190 spedC190, CancellationToken cancellationToken = default);
}

public sealed class SpedC190Repository : RepositoryBase, ISpedC190Repository
{
    public SpedC190Repository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(SpedC190 spedC190, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_C190 (ID_C100, CST_ICMS, CFOP, ALIQ_ICMS, VL_OPR, VL_BC_ICMS, VL_ICMS, VL_BC_ICMS_ST, VL_ICMS_ST, VL_RED_BC, VL_IPI, COD_OBS)
            OUTPUT INSERTED.ID_C190
            VALUES (@SpedC100Id, @CstIcms, @Cfop, @AliquotaIcms, @ValorOperacao, @ValorBcIcms, @ValorIcms, @ValorBcIcmsSt, @ValorIcmsSt, @ValorReducaoBcIcms, @ValorIpi, @CodigoObservacao);
            """;

        return InsertAsync(sql, spedC190, cancellationToken);
    }
}
