using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedE110Repository
{
    Task<int> InsertAsync(SpedE110 spedE110, CancellationToken cancellationToken = default);
}

public sealed class SpedE110Repository : RepositoryBase, ISpedE110Repository
{
    public SpedE110Repository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(SpedE110 spedE110, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_E110 (ID_ARQUIVO, VL_TOT_DEBITOS, VL_AJ_DEBITOS, VL_TOT_AJ_DEBITOS, VL_ESTORNOS_CRED, VL_TOT_CREDITOS, VL_AJ_CREDITOS, VL_TOT_AJ_CREDITOS, VL_ESTORNOS_DEB, VL_SLD_CREDOR_ANT, VL_SLD_APURADO, VL_TOT_DED, VL_ICMS_RECOLHER, VL_SLD_CREDOR_TRANSPORTAR, DEB_ESP)
            OUTPUT INSERTED.ID_E110
            VALUES (@SpedArquivoId, @ValorTotalDebitos, @ValorAjustesDebitos, @ValorTotalAjustesDebitos, @ValorEstornosCreditos, @ValorTotalCreditos, @ValorAjustesCreditos, @ValorTotalAjustesCreditos, @ValorEstornosDebitos, @ValorSaldoCredorAnterior, @ValorSaldoDevedor, @ValorDeducoes, @ValorIcmsRecolher, @ValorSaldoCredorTransportar, @ValorExtraApuracao);
            """;

        return InsertAsync(sql, spedE110, cancellationToken);
    }
}
