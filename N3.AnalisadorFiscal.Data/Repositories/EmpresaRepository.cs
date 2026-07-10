using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IEmpresaRepository
{
    Task<int> InsertAsync(Empresa empresa, CancellationToken cancellationToken = default);
    Task<Empresa?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken = default);
}

public sealed class EmpresaRepository : RepositoryBase, IEmpresaRepository
{
    public EmpresaRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(Empresa empresa, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO EMPRESA (CNPJ, RAZAO_SOCIAL, UF, IE, COD_MUNICIPIO, ATIVO)
            OUTPUT INSERTED.ID_EMPRESA
            VALUES (@Cnpj, @RazaoSocial, @Uf, @InscricaoEstadual, @CodigoMunicipio, 1);
            """;

        return InsertAsync(sql, empresa, cancellationToken);
    }

    public Task<Empresa?> GetByCnpjAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP 1
                ID_EMPRESA AS Id,
                CNPJ AS Cnpj,
                RAZAO_SOCIAL AS RazaoSocial,
                IE AS InscricaoEstadual,
                UF AS Uf,
                COD_MUNICIPIO AS CodigoMunicipio
            FROM EMPRESA
            WHERE CNPJ = @Cnpj;
            """;

        return QuerySingleOrDefaultAsync<Empresa>(sql, new { Cnpj = cnpj }, cancellationToken);
    }
}
