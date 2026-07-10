using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IUnidadeMedidaRepository
{
    Task<int> InsertAsync(UnidadeMedida unidadeMedida, CancellationToken cancellationToken = default);
}

public sealed class UnidadeMedidaRepository : RepositoryBase, IUnidadeMedidaRepository
{
    public UnidadeMedidaRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(UnidadeMedida unidadeMedida, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO UNIDADE_MEDIDA (ID_EMPRESA, UNID, DESCRICAO)
            OUTPUT INSERTED.ID_UNIDADE
            VALUES (@EmpresaId, @Codigo, @Descricao);
            """;

        return InsertAsync(sql, unidadeMedida, cancellationToken);
    }
}
