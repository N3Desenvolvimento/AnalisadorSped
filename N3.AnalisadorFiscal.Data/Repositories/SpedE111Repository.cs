using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface ISpedE111Repository
{
    Task<int> InsertAsync(SpedE111 spedE111, CancellationToken cancellationToken = default);
}

public sealed class SpedE111Repository : RepositoryBase, ISpedE111Repository
{
    public SpedE111Repository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(SpedE111 spedE111, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO SPED_E111 (ID_E110, COD_AJ_APUR, DESCR_COMPL_AJ, VL_AJ_APUR)
            OUTPUT INSERTED.ID_E111
            VALUES (@SpedE110Id, @CodigoAjusteApuracao, @DescricaoComplementar, @ValorAjuste);
            """;

        return InsertAsync(sql, spedE111, cancellationToken);
    }
}
