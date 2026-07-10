using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IParticipanteRepository
{
    Task<int> InsertAsync(Participante participante, CancellationToken cancellationToken = default);
    Task<int?> GetIdExistenteAsync(Participante participante, CancellationToken cancellationToken = default);
    Task EnsureSimplesNacionalColumnsAsync(CancellationToken cancellationToken = default);
    Task AtualizarSimplesNacionalAsync(int empresaId, string cnpj, bool? simplesNacional, string mensagem, CancellationToken cancellationToken = default);
}

public sealed class ParticipanteRepository : RepositoryBase, IParticipanteRepository
{
    public ParticipanteRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(Participante participante, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO PARTICIPANTE (ID_EMPRESA, COD_PART, NOME, CNPJ, CPF, IE, COD_MUNICIPIO)
            OUTPUT INSERTED.ID_PARTICIPANTE
            VALUES (@EmpresaId, @Codigo, @Nome, @Cnpj, @Cpf, @InscricaoEstadual, @CodigoMunicipio);
            """;

        return InsertAsync(sql, participante, cancellationToken);
    }

    public Task<int?> GetIdExistenteAsync(Participante participante, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP 1 ID_PARTICIPANTE
            FROM PARTICIPANTE
            WHERE ID_EMPRESA = @EmpresaId
              AND (
                    (NULLIF(@Cnpj, '') IS NOT NULL AND CNPJ = @Cnpj)
                 OR (NULLIF(@Cnpj, '') IS NULL AND NULLIF(@Cpf, '') IS NOT NULL AND CPF = @Cpf)
                 OR (NULLIF(@Cnpj, '') IS NULL AND NULLIF(@Cpf, '') IS NULL AND COD_PART = @Codigo)
              )
            ORDER BY ID_PARTICIPANTE;
            """;

        return QuerySingleOrDefaultAsync<int?>(sql, participante, cancellationToken);
    }

    public Task EnsureSimplesNacionalColumnsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_NACIONAL') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_NACIONAL BIT NULL;

            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_CONSULTADO_EM') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_CONSULTADO_EM DATETIME2 NULL;

            IF COL_LENGTH('PARTICIPANTE', 'SIMPLES_MENSAGEM') IS NULL
                ALTER TABLE PARTICIPANTE ADD SIMPLES_MENSAGEM NVARCHAR(300) NULL;
            """;

        return ExecuteAsync(sql, null, cancellationToken);
    }

    public async Task AtualizarSimplesNacionalAsync(int empresaId, string cnpj, bool? simplesNacional, string mensagem, CancellationToken cancellationToken = default)
    {
        await EnsureSimplesNacionalColumnsAsync(cancellationToken);

        const string sql = """
            UPDATE PARTICIPANTE
            SET SIMPLES_NACIONAL = @SimplesNacional,
                SIMPLES_CONSULTADO_EM = @ConsultadoEm,
                SIMPLES_MENSAGEM = @Mensagem
            WHERE ID_EMPRESA = @EmpresaId
              AND CNPJ = @Cnpj;
            """;

        await ExecuteAsync(sql, new
        {
            EmpresaId = empresaId,
            Cnpj = cnpj,
            SimplesNacional = simplesNacional,
            ConsultadoEm = DateTime.Now,
            Mensagem = mensagem.Length > 300 ? mensagem[..300] : mensagem
        }, cancellationToken);
    }
}
