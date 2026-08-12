using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using N3.AnalisadorFiscal.Data;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IFolhaFortesImportService
{
    Task<FolhaFortesImportResult> ImportarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
}

public sealed record FolhaFortesImportResult(int Trabalhadores, int Eventos, int Lancamentos, decimal Proventos, decimal Descontos, decimal Liquido);

public sealed class FolhaFortesImportService : IFolhaFortesImportService
{
    private readonly IConfiguration _configuration;
    private readonly IDbConnectionFactory _sqlFactory;
    private readonly IEmpresaRepository _empresaRepository;

    public FolhaFortesImportService(IConfiguration configuration, IDbConnectionFactory sqlFactory, IEmpresaRepository empresaRepository)
    {
        _configuration = configuration; _sqlFactory = sqlFactory; _empresaRepository = empresaRepository;
    }

    public async Task<FolhaFortesImportResult> ImportarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        var empresa = await _empresaRepository.GetByIdAsync(empresaId, cancellationToken) ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigo = empresa.CodigoEmpresaFolhaFortes?.Trim();
        if (string.IsNullOrWhiteSpace(codigo)) throw new InvalidOperationException("Informe o código da empresa na Folha Fortes no cadastro da empresa.");

        var connectionString = _configuration.GetConnectionString("FortesFolha");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A conexão FortesFolha não foi configurada.");
        var senha = _configuration["FortesFolha:Password"];
        if (string.IsNullOrWhiteSpace(senha)) senha = Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        var builder = new FbConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(senha)) throw new InvalidOperationException("A senha do banco do Fortes não foi configurada.");
        builder.Password = senha;

        await using var fonte = new FbConnection(builder.ConnectionString);
        await fonte.OpenAsync(cancellationToken);
        var anoMes = ano * 100 + mes;
        var folha = await fonte.QuerySingleOrDefaultAsync<FolhaOrigem>(new CommandDefinition("""
            SELECT FIRST 1 f.SEQ AS Sequencial, f.FOLHA AS TipoFolha, f.ENCERRADA AS Encerrada,
                   p.ANOMES AS AnoMes, p.TIPO AS TipoCompetencia
            FROM FOL f INNER JOIN FPG p ON p.EMP_CODIGO=f.EMP_CODIGO AND p.FOL_SEQ=f.SEQ
            WHERE TRIM(f.EMP_CODIGO)=@Codigo AND p.ANOMES=@AnoMes AND f.ENCERRADA='S'
              AND p.TIPO='04'
            ORDER BY f.SEQ DESC
            """, new { Codigo = codigo, AnoMes = anoMes }, cancellationToken: cancellationToken));
        if (folha is null) throw new InvalidOperationException("Não foi encontrada uma folha mensal encerrada para a competência selecionada.");

        var inssPatronal = await fonte.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT SUM(COALESCE(INSSEMPRESA,0)) FROM GPS WHERE TRIM(EMP_CODIGO)=@Codigo AND ANOMES=@AnoMes",
            new { Codigo = codigo, AnoMes = anoMes }, cancellationToken: cancellationToken)) ?? 0;

        var lancamentos = (await fonte.QueryAsync<LancamentoOrigem>(new CommandDefinition("""
            SELECT p.EFO_EPG_CODIGO AS CodigoTrabalhador, t.NOME AS NomeTrabalhador,
                   p.EVE_CODIGO AS CodigoEvento, e.NOME AS NomeEvento, e.PROVDESC AS TipoEvento,
                   p.REFERENCIA AS Referencia, p.VALOR AS Valor, p.PARAMETRO AS Parametro
            FROM EFP p
            INNER JOIN EPG t ON t.EMP_CODIGO=p.EMP_CODIGO AND t.CODIGO=p.EFO_EPG_CODIGO
            LEFT JOIN EVE e ON e.CODIGO=p.EVE_CODIGO
               AND (e.EMP_CODIGO=p.EMP_CODIGO OR e.EMP_CODIGO IS NULL)
            WHERE TRIM(p.EMP_CODIGO)=@Codigo AND p.EFO_FOL_SEQ=@Sequencial
            """, new { Codigo = codigo, folha.Sequencial }, cancellationToken: cancellationToken))).AsList();

        await using var destino = (SqlConnection)_sqlFactory.CreateConnection();
        await destino.OpenAsync(cancellationToken);
        await GarantirSchemaAsync(destino, cancellationToken);
        await using var transacao = await destino.BeginTransactionAsync(cancellationToken);
        try
        {
            await destino.ExecuteAsync(new CommandDefinition("DELETE FROM FOLHA_IMPORTACAO WHERE ID_EMPRESA=@EmpresaId AND ANO=@Ano AND MES=@Mes", new { EmpresaId = empresaId, Ano = ano, Mes = mes }, transacao, cancellationToken: cancellationToken));
            var importacaoId = await destino.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO FOLHA_IMPORTACAO (ID_EMPRESA, CODIGO_EMPRESA_FORTES, ANO, MES, SEQUENCIAL_FOLHA_FORTES, TIPO_FOLHA_FORTES, TIPO_COMPETENCIA_FORTES, FOLHA_ENCERRADA, IMPORTADO_EM)
                OUTPUT INSERTED.ID_FOLHA_IMPORTACAO
                VALUES (@EmpresaId,@Codigo,@Ano,@Mes,@Sequencial,@TipoFolha,@TipoCompetencia,1,SYSUTCDATETIME())
                """, new { EmpresaId = empresaId, Codigo = codigo, Ano = ano, Mes = mes, folha.Sequencial, folha.TipoFolha, folha.TipoCompetencia }, transacao, cancellationToken: cancellationToken));

            var trabalhadores = new Dictionary<string, int>();
            foreach (var item in lancamentos.GroupBy(x => x.CodigoTrabalhador).Select(x => x.First()))
                trabalhadores[item.CodigoTrabalhador] = await destino.ExecuteScalarAsync<int>(new CommandDefinition("INSERT INTO FOLHA_TRABALHADOR (ID_FOLHA_IMPORTACAO,CODIGO_TRABALHADOR_FORTES,NOME) OUTPUT INSERTED.ID_FOLHA_TRABALHADOR VALUES (@ImportacaoId,@Codigo,@Nome)", new { ImportacaoId = importacaoId, Codigo = item.CodigoTrabalhador, Nome = item.NomeTrabalhador }, transacao, cancellationToken: cancellationToken));

            var eventos = new Dictionary<string, int>();
            foreach (var item in lancamentos.GroupBy(x => x.CodigoEvento).Select(x => x.First()))
                eventos[item.CodigoEvento] = await destino.ExecuteScalarAsync<int>(new CommandDefinition("INSERT INTO FOLHA_EVENTO (ID_FOLHA_IMPORTACAO,CODIGO_EVENTO_FORTES,NOME,TIPO_EVENTO_FORTES) OUTPUT INSERTED.ID_FOLHA_EVENTO VALUES (@ImportacaoId,@Codigo,@Nome,@Tipo)", new { ImportacaoId = importacaoId, Codigo = item.CodigoEvento, Nome = item.NomeEvento ?? $"Evento {item.CodigoEvento}", Tipo = item.TipoEvento }, transacao, cancellationToken: cancellationToken));

            foreach (var item in lancamentos)
                await destino.ExecuteAsync(new CommandDefinition("INSERT INTO FOLHA_LANCAMENTO (ID_FOLHA_IMPORTACAO,ID_FOLHA_TRABALHADOR,ID_FOLHA_EVENTO,REFERENCIA,VALOR,PARAMETRO) VALUES (@ImportacaoId,@TrabalhadorId,@EventoId,@Referencia,@Valor,@Parametro)", new { ImportacaoId = importacaoId, TrabalhadorId = trabalhadores[item.CodigoTrabalhador], EventoId = eventos[item.CodigoEvento], item.Referencia, item.Valor, item.Parametro }, transacao, cancellationToken: cancellationToken));

            var fgts = lancamentos.Where(x => x.TipoEvento == 0 && string.Equals(x.NomeEvento?.Trim(), "FGTS", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Valor);
            await destino.ExecuteAsync(new CommandDefinition(
                "INSERT INTO FOLHA_RESUMO_ENCARGOS (ID_FOLHA_IMPORTACAO,INSS_PATRONAL,FGTS) VALUES (@ImportacaoId,@InssPatronal,@Fgts)",
                new { ImportacaoId = importacaoId, InssPatronal = inssPatronal, Fgts = fgts }, transacao, cancellationToken: cancellationToken));

            await transacao.CommitAsync(cancellationToken);
            var proventos = lancamentos.Where(x => x.TipoEvento == 1).Sum(x => x.Valor);
            var descontos = lancamentos.Where(x => x.TipoEvento == -1).Sum(x => x.Valor);
            return new(trabalhadores.Count, eventos.Count, lancamentos.Count, proventos, descontos, proventos - descontos);
        }
        catch { await transacao.RollbackAsync(cancellationToken); throw; }
    }

    private static Task GarantirSchemaAsync(SqlConnection connection, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(SchemaSql, cancellationToken: cancellationToken));

    private sealed class FolhaOrigem { public int Sequencial { get; set; } public int TipoFolha { get; set; } public int AnoMes { get; set; } public string TipoCompetencia { get; set; } = ""; public string Encerrada { get; set; } = ""; }
    private sealed class LancamentoOrigem { public string CodigoTrabalhador { get; set; } = ""; public string NomeTrabalhador { get; set; } = ""; public string CodigoEvento { get; set; } = ""; public string? NomeEvento { get; set; } public int TipoEvento { get; set; } public decimal Referencia { get; set; } public decimal Valor { get; set; } public decimal Parametro { get; set; } }

    private const string SchemaSql = """
        IF OBJECT_ID('dbo.FOLHA_IMPORTACAO','U') IS NULL CREATE TABLE dbo.FOLHA_IMPORTACAO (ID_FOLHA_IMPORTACAO INT IDENTITY PRIMARY KEY, ID_EMPRESA INT NOT NULL, CODIGO_EMPRESA_FORTES VARCHAR(20) NOT NULL, ANO INT NOT NULL, MES INT NOT NULL, SEQUENCIAL_FOLHA_FORTES INT NOT NULL, TIPO_FOLHA_FORTES INT NULL, TIPO_COMPETENCIA_FORTES VARCHAR(10) NULL, FOLHA_ENCERRADA BIT NOT NULL, IMPORTADO_EM DATETIME2 NOT NULL, CONSTRAINT FK_FOLHA_IMPORTACAO_EMPRESA FOREIGN KEY(ID_EMPRESA) REFERENCES EMPRESA(ID_EMPRESA), CONSTRAINT UX_FOLHA_IMPORTACAO UNIQUE(ID_EMPRESA,ANO,MES));
        IF OBJECT_ID('dbo.FOLHA_TRABALHADOR','U') IS NULL CREATE TABLE dbo.FOLHA_TRABALHADOR (ID_FOLHA_TRABALHADOR INT IDENTITY PRIMARY KEY, ID_FOLHA_IMPORTACAO INT NOT NULL, CODIGO_TRABALHADOR_FORTES VARCHAR(30) NOT NULL, NOME VARCHAR(255) NOT NULL, CONSTRAINT FK_FOLHA_TRABALHADOR_IMPORTACAO FOREIGN KEY(ID_FOLHA_IMPORTACAO) REFERENCES FOLHA_IMPORTACAO(ID_FOLHA_IMPORTACAO) ON DELETE CASCADE, CONSTRAINT UX_FOLHA_TRABALHADOR UNIQUE(ID_FOLHA_IMPORTACAO,CODIGO_TRABALHADOR_FORTES));
        IF OBJECT_ID('dbo.FOLHA_EVENTO','U') IS NULL CREATE TABLE dbo.FOLHA_EVENTO (ID_FOLHA_EVENTO INT IDENTITY PRIMARY KEY, ID_FOLHA_IMPORTACAO INT NOT NULL, CODIGO_EVENTO_FORTES VARCHAR(20) NOT NULL, NOME VARCHAR(255) NOT NULL, TIPO_EVENTO_FORTES INT NOT NULL, CONSTRAINT FK_FOLHA_EVENTO_IMPORTACAO FOREIGN KEY(ID_FOLHA_IMPORTACAO) REFERENCES FOLHA_IMPORTACAO(ID_FOLHA_IMPORTACAO) ON DELETE CASCADE, CONSTRAINT UX_FOLHA_EVENTO UNIQUE(ID_FOLHA_IMPORTACAO,CODIGO_EVENTO_FORTES));
        IF OBJECT_ID('dbo.FOLHA_LANCAMENTO','U') IS NULL CREATE TABLE dbo.FOLHA_LANCAMENTO (ID_FOLHA_LANCAMENTO BIGINT IDENTITY PRIMARY KEY, ID_FOLHA_IMPORTACAO INT NOT NULL, ID_FOLHA_TRABALHADOR INT NOT NULL, ID_FOLHA_EVENTO INT NOT NULL, REFERENCIA DECIMAL(18,4) NOT NULL, VALOR DECIMAL(18,2) NOT NULL, PARAMETRO DECIMAL(18,4) NOT NULL, ATRIBUTO VARCHAR(30) NULL, DEMONSTRACAO VARCHAR(30) NULL, CONSTRAINT FK_FOLHA_LANCAMENTO_IMPORTACAO FOREIGN KEY(ID_FOLHA_IMPORTACAO) REFERENCES FOLHA_IMPORTACAO(ID_FOLHA_IMPORTACAO) ON DELETE CASCADE, CONSTRAINT FK_FOLHA_LANCAMENTO_TRABALHADOR FOREIGN KEY(ID_FOLHA_TRABALHADOR) REFERENCES FOLHA_TRABALHADOR(ID_FOLHA_TRABALHADOR), CONSTRAINT FK_FOLHA_LANCAMENTO_EVENTO FOREIGN KEY(ID_FOLHA_EVENTO) REFERENCES FOLHA_EVENTO(ID_FOLHA_EVENTO));
        IF OBJECT_ID('dbo.FOLHA_RESUMO_ENCARGOS','U') IS NULL CREATE TABLE dbo.FOLHA_RESUMO_ENCARGOS (ID_FOLHA_IMPORTACAO INT PRIMARY KEY, INSS_PATRONAL DECIMAL(18,2) NOT NULL, FGTS DECIMAL(18,2) NOT NULL, CONSTRAINT FK_FOLHA_RESUMO_IMPORTACAO FOREIGN KEY(ID_FOLHA_IMPORTACAO) REFERENCES FOLHA_IMPORTACAO(ID_FOLHA_IMPORTACAO) ON DELETE CASCADE);
        """;
}
