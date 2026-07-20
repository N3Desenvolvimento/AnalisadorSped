using Dapper;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IFolhaPagamentoRepository
{
    Task<FolhaEspelhoDto?> GetEspelhoAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FolhaResumoMesDto>> GetResumoAnualAsync(int empresaId, int ano, CancellationToken cancellationToken = default);
}

public sealed class FolhaPagamentoRepository : RepositoryBase, IFolhaPagamentoRepository
{
    public FolhaPagamentoRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<FolhaEspelhoDto?> GetEspelhoAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        await using var connection = ConnectionFactory.CreateConnection();
        const string importacaoSql = """
            SELECT TOP 1 i.ID_FOLHA_IMPORTACAO AS Id, i.ID_EMPRESA AS EmpresaId,
                   e.RAZAO_SOCIAL AS RazaoSocial, i.ANO AS Ano, i.MES AS Mes, i.IMPORTADO_EM AS ImportadoEm
            FROM FOLHA_IMPORTACAO i INNER JOIN EMPRESA e ON e.ID_EMPRESA=i.ID_EMPRESA
            WHERE i.ID_EMPRESA=@EmpresaId AND i.ANO=@Ano AND i.MES=@Mes;
            """;
        var importacao = await connection.QuerySingleOrDefaultAsync<ImportacaoLinha>(new CommandDefinition(importacaoSql, new { EmpresaId = empresaId, Ano = ano, Mes = mes }, cancellationToken: cancellationToken));
        if (importacao is null) return null;

        const string trabalhadoresSql = """
            SELECT ID_FOLHA_TRABALHADOR AS Id, CODIGO_TRABALHADOR_FORTES AS CodigoFortes, NOME AS Nome
            FROM FOLHA_TRABALHADOR WHERE ID_FOLHA_IMPORTACAO=@ImportacaoId ORDER BY NOME;
            """;
        var trabalhadores = (await connection.QueryAsync<FolhaEspelhoTrabalhadorDto>(new CommandDefinition(trabalhadoresSql, new { ImportacaoId = importacao.Id }, cancellationToken: cancellationToken))).AsList();

        const string eventosSql = """
            SELECT l.ID_FOLHA_TRABALHADOR AS TrabalhadorId, e.CODIGO_EVENTO_FORTES AS CodigoFortes,
                   e.NOME AS Nome, e.TIPO_EVENTO_FORTES AS TipoEvento,
                   l.REFERENCIA AS Referencia, l.VALOR AS Valor
            FROM FOLHA_LANCAMENTO l INNER JOIN FOLHA_EVENTO e ON e.ID_FOLHA_EVENTO=l.ID_FOLHA_EVENTO
            WHERE l.ID_FOLHA_IMPORTACAO=@ImportacaoId
            ORDER BY l.ID_FOLHA_TRABALHADOR, e.TIPO_EVENTO_FORTES DESC, e.CODIGO_EVENTO_FORTES;
            """;
        var eventos = (await connection.QueryAsync<EventoLinha>(new CommandDefinition(eventosSql, new { ImportacaoId = importacao.Id }, cancellationToken: cancellationToken))).AsList();
        foreach (var trabalhador in trabalhadores)
            trabalhador.Eventos = eventos.Where(x => x.TrabalhadorId == trabalhador.Id).Select(x => new FolhaEspelhoEventoDto { CodigoFortes=x.CodigoFortes, Nome=x.Nome, TipoEvento=x.TipoEvento, Referencia=x.Referencia, Valor=x.Valor }).ToArray();

        return new FolhaEspelhoDto { EmpresaId=importacao.EmpresaId, RazaoSocial=importacao.RazaoSocial, Ano=importacao.Ano, Mes=importacao.Mes, ImportadoEm=importacao.ImportadoEm, Trabalhadores=trabalhadores };
    }

    public async Task<IReadOnlyList<FolhaResumoMesDto>> GetResumoAnualAsync(int empresaId, int ano, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT i.MES AS Mes,
                   SUM(CASE WHEN e.TIPO_EVENTO_FORTES=1 THEN l.VALOR ELSE 0 END) AS ValorFolha,
                   MAX(COALESCE(r.INSS_PATRONAL,0)) AS ValorInssPatronal,
                   MAX(COALESCE(r.FGTS,0)) AS ValorFgts
            FROM FOLHA_IMPORTACAO i
            INNER JOIN FOLHA_LANCAMENTO l ON l.ID_FOLHA_IMPORTACAO=i.ID_FOLHA_IMPORTACAO
            INNER JOIN FOLHA_EVENTO e ON e.ID_FOLHA_EVENTO=l.ID_FOLHA_EVENTO
            LEFT JOIN FOLHA_RESUMO_ENCARGOS r ON r.ID_FOLHA_IMPORTACAO=i.ID_FOLHA_IMPORTACAO
            WHERE i.ID_EMPRESA=@EmpresaId AND i.ANO=@Ano
            GROUP BY i.MES ORDER BY i.MES;
            """;
        await using var connection = ConnectionFactory.CreateConnection();
        return (await connection.QueryAsync<FolhaResumoMesDto>(new CommandDefinition(sql, new { EmpresaId = empresaId, Ano = ano }, cancellationToken: cancellationToken))).AsList();
    }

    private sealed class ImportacaoLinha { public int Id { get; set; } public int EmpresaId { get; set; } public string RazaoSocial { get; set; }=""; public int Ano { get; set; } public int Mes { get; set; } public DateTime ImportadoEm { get; set; } }
    private sealed class EventoLinha : FolhaEspelhoEventoDto { public int TrabalhadorId { get; set; } }
}
