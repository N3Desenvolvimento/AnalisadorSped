using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IProdutoRepository
{
    Task<int> InsertAsync(Produto produto, CancellationToken cancellationToken = default);
    Task<int?> GetIdExistenteAsync(Produto produto, CancellationToken cancellationToken = default);
}

public sealed class ProdutoRepository : RepositoryBase, IProdutoRepository
{
    public ProdutoRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(Produto produto, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO PRODUTO (ID_EMPRESA, COD_ITEM, DESCRICAO, COD_BARRA, UNID_INV, TIPO_ITEM, NCM, EX_IPI, COD_GEN, ALIQ_ICMS)
            OUTPUT INSERTED.ID_PRODUTO
            VALUES (@EmpresaId, @Codigo, @Descricao, @CodigoBarra, @Unidade, @TipoItem, @CodigoNcm, @ExIpi, @CodigoGenero, @AliquotaIcms);
            """;

        return InsertAsync(sql, produto, cancellationToken);
    }

    public Task<int?> GetIdExistenteAsync(Produto produto, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP 1 ID_PRODUTO
            FROM PRODUTO
            WHERE ID_EMPRESA = @EmpresaId
              AND UPPER(LTRIM(RTRIM(COALESCE(DESCRICAO, '')))) = UPPER(LTRIM(RTRIM(COALESCE(@Descricao, ''))))
              AND LTRIM(RTRIM(COALESCE(NCM, ''))) = LTRIM(RTRIM(COALESCE(@CodigoNcm, '')))
            ORDER BY ID_PRODUTO;
            """;

        return QuerySingleOrDefaultAsync<int?>(sql, produto, cancellationToken);
    }
}
