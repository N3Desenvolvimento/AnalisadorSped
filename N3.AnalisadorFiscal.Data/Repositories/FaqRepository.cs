using Dapper;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IFaqRepository
{
    Task<IReadOnlyList<FaqArtigo>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> InsertAsync(FaqArtigo artigo, CancellationToken cancellationToken = default);
    Task UpdateAsync(FaqArtigo artigo, CancellationToken cancellationToken = default);
    Task RegistrarAvaliacaoAsync(int artigoId, bool foiUtil, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria ou atualiza um item vindo de e-mail/chamado. Novos itens externos sempre entram como rascunho.
    /// A combinação Origem + ReferenciaExterna evita importar a mesma conversa mais de uma vez.
    /// </summary>
    Task<int> UpsertImportadoAsync(FaqArtigo artigo, CancellationToken cancellationToken = default);
}

public sealed class FaqRepository : RepositoryBase, IFaqRepository
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaPronto;

    public FaqRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<IReadOnlyList<FaqArtigo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT ID_FAQ_ARTIGO AS Id,
                   PERGUNTA AS Pergunta,
                   RESPOSTA AS Resposta,
                   CATEGORIA AS Categoria,
                   PALAVRAS_CHAVE AS PalavrasChave,
                   PUBLICADO AS Publicado,
                   ATIVO AS Ativo,
                   ORIGEM AS Origem,
                   REFERENCIA_EXTERNA AS ReferenciaExterna,
                   URL_ORIGEM AS UrlOrigem,
                   AUTOR AS Autor,
                   CRIADO_EM AS CriadoEm,
                   ATUALIZADO_EM AS AtualizadoEm,
                   TOTAL_UTIL AS TotalUtil,
                   TOTAL_NAO_UTIL AS TotalNaoUtil
            FROM dbo.FAQ_ARTIGO
            WHERE ATIVO = 1
            ORDER BY PUBLICADO DESC, ATUALIZADO_EM DESC, PERGUNTA;
            """;

        await using var connection = ConnectionFactory.CreateConnection();
        var itens = await connection.QueryAsync<FaqArtigo>(
            new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
        return itens.ToList();
    }

    public async Task<int> InsertAsync(FaqArtigo artigo, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        Validar(artigo);
        const string sql = """
            INSERT INTO dbo.FAQ_ARTIGO
                (PERGUNTA, RESPOSTA, CATEGORIA, PALAVRAS_CHAVE, PUBLICADO, ATIVO,
                 ORIGEM, REFERENCIA_EXTERNA, URL_ORIGEM, AUTOR, CRIADO_EM, ATUALIZADO_EM)
            OUTPUT INSERTED.ID_FAQ_ARTIGO
            VALUES
                (@Pergunta, @Resposta, @Categoria, @PalavrasChave, @Publicado, 1,
                 @Origem, @ReferenciaExterna, @UrlOrigem, @Autor, SYSUTCDATETIME(), SYSUTCDATETIME());
            """;

        return await InsertAsync(sql, artigo, cancellationToken);
    }

    public async Task UpdateAsync(FaqArtigo artigo, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        Validar(artigo);
        const string sql = """
            UPDATE dbo.FAQ_ARTIGO
               SET PERGUNTA = @Pergunta,
                   RESPOSTA = @Resposta,
                   CATEGORIA = @Categoria,
                   PALAVRAS_CHAVE = @PalavrasChave,
                   PUBLICADO = @Publicado,
                   ORIGEM = @Origem,
                   REFERENCIA_EXTERNA = @ReferenciaExterna,
                   URL_ORIGEM = @UrlOrigem,
                   AUTOR = @Autor,
                   ATUALIZADO_EM = SYSUTCDATETIME()
             WHERE ID_FAQ_ARTIGO = @Id AND ATIVO = 1;
            """;

        await ExecuteAsync(sql, artigo, cancellationToken);
    }

    public async Task RegistrarAvaliacaoAsync(
        int artigoId,
        bool foiUtil,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.FAQ_ARTIGO
               SET TOTAL_UTIL = TOTAL_UTIL + CASE WHEN @FoiUtil = 1 THEN 1 ELSE 0 END,
                   TOTAL_NAO_UTIL = TOTAL_NAO_UTIL + CASE WHEN @FoiUtil = 0 THEN 1 ELSE 0 END
             WHERE ID_FAQ_ARTIGO = @ArtigoId AND ATIVO = 1;
            """;

        await ExecuteAsync(sql, new { ArtigoId = artigoId, FoiUtil = foiUtil }, cancellationToken);
    }

    public async Task<int> UpsertImportadoAsync(FaqArtigo artigo, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        Validar(artigo);
        if (string.IsNullOrWhiteSpace(artigo.ReferenciaExterna))
            throw new ArgumentException("A referência externa é obrigatória para itens importados.", nameof(artigo));
        if (string.Equals(artigo.Origem, "Manual", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Informe o sistema de origem do item importado.", nameof(artigo));

        const string sql = """
            DECLARE @Id INT;

            SELECT @Id = ID_FAQ_ARTIGO
              FROM dbo.FAQ_ARTIGO
             WHERE ORIGEM = @Origem AND REFERENCIA_EXTERNA = @ReferenciaExterna;

            IF @Id IS NULL
            BEGIN
                INSERT INTO dbo.FAQ_ARTIGO
                    (PERGUNTA, RESPOSTA, CATEGORIA, PALAVRAS_CHAVE, PUBLICADO, ATIVO,
                     ORIGEM, REFERENCIA_EXTERNA, URL_ORIGEM, AUTOR, CRIADO_EM, ATUALIZADO_EM)
                VALUES
                    (@Pergunta, @Resposta, @Categoria, @PalavrasChave, 0, 1,
                     @Origem, @ReferenciaExterna, @UrlOrigem, @Autor, SYSUTCDATETIME(), SYSUTCDATETIME());
                SET @Id = CONVERT(INT, SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
                UPDATE dbo.FAQ_ARTIGO
                   SET PERGUNTA = @Pergunta,
                       RESPOSTA = @Resposta,
                       CATEGORIA = @Categoria,
                       PALAVRAS_CHAVE = @PalavrasChave,
                       URL_ORIGEM = @UrlOrigem,
                       AUTOR = @Autor,
                       ATUALIZADO_EM = SYSUTCDATETIME()
                 WHERE ID_FAQ_ARTIGO = @Id AND PUBLICADO = 0;
            END;

            SELECT @Id;
            """;

        await using var connection = ConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, artigo, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaPronto) return;

        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaPronto) return;

            const string sql = """
                IF OBJECT_ID('dbo.FAQ_ARTIGO', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.FAQ_ARTIGO
                    (
                        ID_FAQ_ARTIGO INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FAQ_ARTIGO PRIMARY KEY,
                        PERGUNTA NVARCHAR(500) NOT NULL,
                        RESPOSTA NVARCHAR(MAX) NOT NULL,
                        CATEGORIA NVARCHAR(100) NOT NULL CONSTRAINT DF_FAQ_ARTIGO_CATEGORIA DEFAULT N'Geral',
                        PALAVRAS_CHAVE NVARCHAR(500) NULL,
                        PUBLICADO BIT NOT NULL CONSTRAINT DF_FAQ_ARTIGO_PUBLICADO DEFAULT 1,
                        ATIVO BIT NOT NULL CONSTRAINT DF_FAQ_ARTIGO_ATIVO DEFAULT 1,
                        ORIGEM NVARCHAR(30) NOT NULL CONSTRAINT DF_FAQ_ARTIGO_ORIGEM DEFAULT N'Manual',
                        REFERENCIA_EXTERNA NVARCHAR(300) NULL,
                        URL_ORIGEM NVARCHAR(1000) NULL,
                        AUTOR NVARCHAR(150) NULL,
                        CRIADO_EM DATETIME2 NOT NULL CONSTRAINT DF_FAQ_ARTIGO_CRIADO_EM DEFAULT SYSUTCDATETIME(),
                        ATUALIZADO_EM DATETIME2 NOT NULL CONSTRAINT DF_FAQ_ARTIGO_ATUALIZADO_EM DEFAULT SYSUTCDATETIME(),
                        TOTAL_UTIL INT NOT NULL CONSTRAINT DF_FAQ_ARTIGO_TOTAL_UTIL DEFAULT 0,
                        TOTAL_NAO_UTIL INT NOT NULL CONSTRAINT DF_FAQ_ARTIGO_TOTAL_NAO_UTIL DEFAULT 0
                    );

                    CREATE INDEX IX_FAQ_ARTIGO_CONSULTA
                        ON dbo.FAQ_ARTIGO (ATIVO, PUBLICADO, CATEGORIA, ATUALIZADO_EM);

                    CREATE UNIQUE INDEX UX_FAQ_ARTIGO_ORIGEM_REFERENCIA
                        ON dbo.FAQ_ARTIGO (ORIGEM, REFERENCIA_EXTERNA)
                        WHERE REFERENCIA_EXTERNA IS NOT NULL;
                END;
                """;

            await ExecuteAsync(sql, null, cancellationToken);
            _schemaPronto = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static void Validar(FaqArtigo artigo)
    {
        if (string.IsNullOrWhiteSpace(artigo.Pergunta))
            throw new ArgumentException("A pergunta é obrigatória.", nameof(artigo));
        if (string.IsNullOrWhiteSpace(artigo.Resposta))
            throw new ArgumentException("A resposta é obrigatória.", nameof(artigo));
        if (string.IsNullOrWhiteSpace(artigo.Categoria))
            throw new ArgumentException("A categoria é obrigatória.", nameof(artigo));
        if (string.IsNullOrWhiteSpace(artigo.Origem))
            throw new ArgumentException("A origem é obrigatória.", nameof(artigo));
    }
}
