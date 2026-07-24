using Dapper;
using FirebirdSql.Data.FirebirdClient;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IIcmsFortesApuracaoService
{
    Task<IcmsFortesApuracao> ConsultarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default);
}

public sealed class IcmsFortesApuracaoService(IConfiguration configuration, IEmpresaRepository empresas) : IIcmsFortesApuracaoService
{
    public async Task<IcmsFortesApuracao> ConsultarAsync(int empresaId, int ano, int mes, CancellationToken cancellationToken = default)
    {
        if (ano is < 2000 or > 2100 || mes is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(mes), "Informe uma competência válida.");

        var empresa = await empresas.GetByIdAsync(empresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigo = empresa.CodigoEmpresaFiscalFortes?.Trim();
        if (string.IsNullOrWhiteSpace(codigo))
            throw new InvalidOperationException("A empresa não possui código do Fiscal Fortes cadastrado.");

        var cs = configuration.GetConnectionString("FortesFolha")
            ?? throw new InvalidOperationException("A conexão com o Fortes não foi configurada.");
        var builder = new FbConnectionStringBuilder(cs);
        var senha = configuration["FortesFolha:Password"] ?? Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        if (!string.IsNullOrWhiteSpace(senha)) builder.Password = senha;

        await using var connection = new FbConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var inicio = new DateTime(ano, mes, 1);
        var fim = inicio.AddMonths(1);

        var itens = (await connection.QueryAsync<ItemFortes>(new CommandDefinition("""
            SELECT TRIM(p.CFO_CODIGO) AS Cfop,
                   COALESCE(p.VRTOTAL,0)+COALESCE(p.IPIVR,0)+COALESCE(p.ICMSSUBSTVR,0) AS ValorContabil,
                   COALESCE(p.ICMSBASECALC,0) AS BaseCalculo,
                   ROUND(COALESCE(p.ICMSBASECALC,0)*COALESCE(p.ICMSALIQ,0)/100,2) AS Imposto,
                   CASE
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=2 THEN COALESCE(p.VRTOTAL,0)
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=1 AND COALESCE(p.VRTOTAL,0)>COALESCE(p.ICMSBASECALC,0)
                       THEN COALESCE(p.VRTOTAL,0)-COALESCE(p.ICMSBASECALC,0)
                     ELSE 0
                   END AS Isentas,
                   CASE
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=3 THEN COALESCE(p.VRTOTAL,0)+COALESCE(p.IPIVR,0)+COALESCE(p.ICMSSUBSTVR,0)
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=1 THEN COALESCE(p.IPIVR,0)+COALESCE(p.ICMSSUBSTVR,0)
                     ELSE 0
                   END AS Outras
            FROM NFM n
            INNER JOIN PNM p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ
            WHERE TRIM(n.EMP_CODIGO)=@Codigo AND TRIM(n.EST_CODIGO)='0001'
              AND COALESCE(n.CANCELADO,0)=0
              AND n.DTENTRADASAIDA>=@Inicio AND n.DTENTRADASAIDA<@Fim
              AND SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3','5','6','7')
            UNION ALL
            SELECT TRIM(p.CFO_CODIGO) AS Cfop, COALESCE(p.VRTOTAL,0) AS ValorContabil,
                   COALESCE(p.ICMSBASECALC,0) AS BaseCalculo,
                   ROUND(COALESCE(p.ICMSBASECALC,0)*COALESCE(p.ICMSALIQ,0)/100,2) AS Imposto,
                   CASE WHEN COALESCE(p.ICMSTRIBUTACAO,0)=2 THEN COALESCE(p.VRTOTAL,0) ELSE 0 END AS Isentas,
                   CASE
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=3 THEN COALESCE(p.VRTOTAL,0)
                     WHEN COALESCE(p.ICMSTRIBUTACAO,0)=1 AND COALESCE(p.VRTOTAL,0)>COALESCE(p.ICMSBASECALC,0)
                       THEN COALESCE(p.VRTOTAL,0)-COALESCE(p.ICMSBASECALC,0)
                     ELSE 0
                   END AS Outras
            FROM NVC n
            INNER JOIN PNC p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NVC_SEQ=n.SEQ
            WHERE TRIM(n.EMP_CODIGO)=@Codigo AND TRIM(n.EST_CODIGO)='0001'
              AND COALESCE(n.CANCELADO,0)=0
              AND n.DTEMISSAO>=@Inicio AND n.DTEMISSAO<@Fim
              AND SUBSTRING(TRIM(n.CHAVEELET) FROM 21 FOR 2)='65'
              AND SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
            """, new { Codigo = codigo, Inicio = inicio, Fim = fim }, commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();

        var linhas = itens.GroupBy(x => x.Cfop)
            .Select(g => new IcmsFortesLinha(g.Key, "", g.Sum(x => x.ValorContabil),
                g.Sum(x => x.BaseCalculo), g.Sum(x => x.Imposto), g.Sum(x => x.Isentas), g.Sum(x => x.Outras)))
            .OrderBy(x => x.Cfop)
            .ToList();

        var ajustes = (await connection.QueryAsync<IcmsFortesAjuste>(new CommandDefinition("""
            SELECT MOVIMENTO AS Movimento, CLASSIFICACAO AS Classificacao, VALOR AS Valor,
                   CAST(DESCRICAO AS VARCHAR(1000)) AS Descricao
            FROM ICD
            WHERE TRIM(EMP_CODIGO)=@Codigo AND TRIM(EST_CODIGO)='0001'
              AND ANOMES=@AnoMes AND APURACAO=1 AND MOVIMENTO IN (1,3)
            ORDER BY MOVIMENTO, CLASSIFICACAO, SEQ
            """, new { Codigo = codigo, AnoMes = ano * 100 + mes }, commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();

        return new IcmsFortesApuracao(empresa.RazaoSocial, empresa.Cnpj, codigo, ano, mes,
            linhas.Where(x => x.Cfop[0] is '1' or '2' or '3').ToArray(),
            linhas.Where(x => x.Cfop[0] is '5' or '6' or '7').ToArray(), ajustes);
    }

    private sealed class ItemFortes
    {
        public string Cfop { get; set; } = "";
        public decimal ValorContabil { get; set; }
        public decimal BaseCalculo { get; set; }
        public decimal Imposto { get; set; }
        public decimal Isentas { get; set; }
        public decimal Outras { get; set; }
    }
}

public sealed record IcmsFortesLinha(string Cfop, string Descricao, decimal ValorContabil,
    decimal BaseCalculo, decimal Imposto, decimal Isentas, decimal Outras);

public sealed class IcmsFortesAjuste
{
    public int Movimento { get; set; }
    public int Classificacao { get; set; }
    public decimal Valor { get; set; }
    public string? Descricao { get; set; }
}

public sealed record IcmsFortesApuracao(string Empresa, string Cnpj, string CodigoFortes, int Ano, int Mes,
    IReadOnlyList<IcmsFortesLinha> Entradas, IReadOnlyList<IcmsFortesLinha> Saidas, IReadOnlyList<IcmsFortesAjuste> Ajustes)
{
    public decimal Creditos => Entradas.Sum(x => x.Imposto);
    public decimal Debitos => Saidas.Sum(x => x.Imposto);
    public decimal OutrosDebitos => Ajustes.Where(x => x.Movimento == 1).Sum(x => x.Valor);
    public decimal OutrosCreditos => Ajustes.Where(x => x.Movimento == 3).Sum(x => x.Valor);
    public decimal TotalDebitos => Debitos + OutrosDebitos;
    public decimal TotalCreditos => Creditos + OutrosCreditos;
    public decimal SaldoDevedor => Math.Max(0, TotalDebitos - TotalCreditos);
    public decimal SaldoCredor => Math.Max(0, TotalCreditos - TotalDebitos);
}
