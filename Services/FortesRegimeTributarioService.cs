using Dapper;
using FirebirdSql.Data.FirebirdClient;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IFortesRegimeTributarioService
{
    Task<IReadOnlyList<FortesEmpresaOpcao>> ListarEmpresasAsync(CancellationToken cancellationToken = default);
    Task<FortesDadosRegimeTributario> ConsultarAsync(
        string codigoEmpresa,
        int ano,
        CancellationToken cancellationToken = default);
}

public sealed record FortesEmpresaOpcao(
    string Codigo,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    bool OptanteSimples);

public sealed record FortesDadosRegimeTributario(
    FortesEmpresaOpcao Empresa,
    int Ano,
    decimal ReceitaMercadorias,
    decimal ReceitaServicos,
    decimal ComprasMercadoriasInsumos,
    decimal DebitoIcms,
    decimal CreditoIcms,
    decimal EntradasIcmsInternas,
    decimal EntradasIcmsNordeste,
    decimal EntradasIcmsSulSudeste,
    decimal EntradasIcmsOutrasInterestaduais,
    decimal EntradasIcmsSemRegra,
    decimal AjustesCreditoIcms,
    decimal Iss,
    decimal BaseCreditoPisCofins,
    decimal BaseReceitaPisCofins,
    decimal FolhaBruta,
    decimal InssPatronal,
    decimal Fgts,
    int CompetenciasFolha,
    int DocumentosMercadorias,
    int DocumentosServicos);

public sealed class FortesRegimeTributarioService(IConfiguration configuration)
    : IFortesRegimeTributarioService
{
    public async Task<IReadOnlyList<FortesEmpresaOpcao>> ListarEmpresasAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexaoAsync(cancellationToken);
        var empresas = (await connection.QueryAsync<EmpresaFortesLinha>(new CommandDefinition("""
            SELECT TRIM(e.CODIGO) AS Codigo,
                   COALESCE(NULLIF(TRIM(e.RAZAOSOCIAL),''), TRIM(e.NOME)) AS RazaoSocial,
                   NULLIF(TRIM(e.NMFANTASIA),'') AS NomeFantasia,
                   TRIM(e.CNPJBASE) AS CnpjBase,
                   COALESCE(TRIM(s.SEQCNPJ),'0001') AS SequencialCnpj,
                   COALESCE(e.OPTANTESIMPLES,'N') AS OptanteSimples
            FROM EMP e
            LEFT JOIN EST s ON s.EMP_CODIGO=e.CODIGO AND s.MATRIZ=1
            WHERE COALESCE(e.DESATIVADA,0)=0
              AND COALESCE(e.BLOQUEADA,0)=0
            ORDER BY COALESCE(NULLIF(TRIM(e.RAZAOSOCIAL),''), TRIM(e.NOME))
            """, commandTimeout: 120, cancellationToken: cancellationToken))).AsList();

        return empresas
            .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Select(x => new FortesEmpresaOpcao(
                x.Codigo,
                x.RazaoSocial,
                x.NomeFantasia,
                MontarCnpj(x.CnpjBase, x.SequencialCnpj),
                string.Equals(x.OptanteSimples?.Trim(), "S", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.RazaoSocial)
            .ToArray();
    }

    public async Task<FortesDadosRegimeTributario> ConsultarAsync(
        string codigoEmpresa,
        int ano,
        CancellationToken cancellationToken = default)
    {
        var codigo = codigoEmpresa?.Trim();
        if (string.IsNullOrWhiteSpace(codigo))
            throw new InvalidOperationException("Selecione uma empresa do cadastro do Fortes.");
        if (ano is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(ano), "Informe um ano válido.");

        await using var connection = await AbrirConexaoAsync(cancellationToken);
        var empresaLinha = await connection.QuerySingleOrDefaultAsync<EmpresaFortesLinha>(new CommandDefinition("""
            SELECT FIRST 1 TRIM(e.CODIGO) AS Codigo,
                   COALESCE(NULLIF(TRIM(e.RAZAOSOCIAL),''), TRIM(e.NOME)) AS RazaoSocial,
                   NULLIF(TRIM(e.NMFANTASIA),'') AS NomeFantasia,
                   TRIM(e.CNPJBASE) AS CnpjBase,
                   COALESCE(TRIM(s.SEQCNPJ),'0001') AS SequencialCnpj,
                   COALESCE(e.OPTANTESIMPLES,'N') AS OptanteSimples
            FROM EMP e
            LEFT JOIN EST s ON s.EMP_CODIGO=e.CODIGO AND s.MATRIZ=1
            WHERE TRIM(e.CODIGO)=@Codigo
            """, new { Codigo = codigo }, commandTimeout: 120,
            cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("A empresa não foi encontrada no cadastro do Fortes.");

        var inicio = new DateTime(ano, 1, 1);
        var fim = inicio.AddYears(1);
        var inicioAnoMes = ano * 100 + 1;
        var fimAnoMes = ano * 100 + 12;

        var movimentos = (await connection.QueryAsync<MovimentoFiscalLinha>(new CommandDefinition("""
            SELECT x.Direcao,
                   COUNT(DISTINCT x.Documento) AS QuantidadeDocumentos,
                   SUM(x.ValorContabil) AS ValorContabil,
                   SUM(x.Icms) AS Icms,
                   SUM(x.BasePis) AS BasePis,
                   SUM(x.BaseCofins) AS BaseCofins
            FROM (
                SELECT CASE
                         WHEN TRIM(p.CFO_CODIGO) IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202') THEN 'S'
                         WHEN TRIM(p.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202') THEN 'E'
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3') THEN 'E'
                         ELSE 'S'
                       END AS Direcao,
                       'NFM-' || CAST(n.SEQ AS VARCHAR(20)) AS Documento,
                       CASE WHEN TRIM(p.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END
                         * (COALESCE(p.VRTOTAL,0)+COALESCE(p.IPIVR,0)+COALESCE(p.ICMSSUBSTVR,0)) AS ValorContabil,
                       CASE WHEN TRIM(p.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END
                         * ROUND(COALESCE(p.ICMSBASECALC,0)*COALESCE(p.ICMSALIQ,0)/100,2) AS Icms,
                       CASE WHEN TRIM(p.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END * CASE
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3')
                              AND TRIM(COALESCE(p.CSTPIS,'')) BETWEEN '50' AND '66'
                           THEN COALESCE(p.TFBASECALCPIS,0)
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
                           THEN COALESCE(p.TFBASECALCPIS,0)
                         ELSE 0
                       END AS BasePis,
                       CASE WHEN TRIM(p.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END * CASE
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3')
                              AND TRIM(COALESCE(p.CSTCOFINS,'')) BETWEEN '50' AND '66'
                           THEN COALESCE(p.TFBASECALCCOFINS,0)
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
                           THEN COALESCE(p.TFBASECALCCOFINS,0)
                         ELSE 0
                       END AS BaseCofins
                FROM NFM n
                INNER JOIN PNM p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3','5','6','7')
                  AND TRIM(p.CFO_CODIGO) NOT IN
                      ('1151','1152','1408','2151','2152','2408','2409',
                       '5151','5152','5408','5409','6151','6152','6408','6409')
                UNION ALL
                SELECT CASE
                         WHEN TRIM(i.CFO_CODIGO) IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202') THEN 'S'
                         WHEN TRIM(i.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202') THEN 'E'
                         WHEN SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3') THEN 'E'
                         ELSE 'S'
                       END AS Direcao,
                       'NFMI-' || CAST(n.SEQ AS VARCHAR(20)) AS Documento,
                       CASE WHEN TRIM(i.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END
                         * COALESCE(i.VALOR,0) AS ValorContabil,
                       CASE WHEN TRIM(i.CFO_CODIGO) IN
                           ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202',
                            '5201','5202','6201','6202','7201','7202') THEN -1 ELSE 1 END
                         * COALESCE(i.ICMSDEBCRED,0) AS Icms,
                       CASE
                         WHEN TRIM(i.CFO_CODIGO) IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202')
                           THEN -COALESCE(i.VALOR,0)
                         WHEN SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
                              AND TRIM(i.CFO_CODIGO) NOT IN ('5201','5202','6201','6202','7201','7202')
                           THEN COALESCE(i.VALOR,0)
                         ELSE 0
                       END AS BasePis,
                       CASE
                         WHEN TRIM(i.CFO_CODIGO) IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202')
                           THEN -COALESCE(i.VALOR,0)
                         WHEN SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
                              AND TRIM(i.CFO_CODIGO) NOT IN ('5201','5202','6201','6202','7201','7202')
                           THEN COALESCE(i.VALOR,0)
                         ELSE 0
                       END AS BaseCofins
                FROM NFM n
                INNER JOIN INM i ON i.EMP_CODIGO=n.EMP_CODIGO AND i.NFM_SEQ=n.SEQ
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND NOT EXISTS (
                      SELECT 1 FROM PNM p
                      WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ)
                  AND SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3','5','6','7')
                  AND TRIM(i.CFO_CODIGO) NOT IN
                      ('1151','1152','1408','2151','2152','2408','2409',
                       '5151','5152','5408','5409','6151','6152','6408','6409')
                UNION ALL
                SELECT CASE WHEN TRIM(n.OPERACAO)='E' THEN 'E' ELSE 'S' END AS Direcao,
                       'NFMH-' || CAST(n.SEQ AS VARCHAR(20)) AS Documento,
                       CASE WHEN COALESCE(n.TOTALPRODUTOS,0)>0
                            THEN n.TOTALPRODUTOS ELSE COALESCE(n.TOTALVR,0) END AS ValorContabil,
                       0 AS Icms,
                       CASE WHEN TRIM(n.OPERACAO)='S' THEN
                           CASE WHEN COALESCE(n.TOTALPRODUTOS,0)>0
                                THEN n.TOTALPRODUTOS ELSE COALESCE(n.TOTALVR,0) END
                           ELSE 0 END AS BasePis,
                       CASE WHEN TRIM(n.OPERACAO)='S' THEN
                           CASE WHEN COALESCE(n.TOTALPRODUTOS,0)>0
                                THEN n.TOTALPRODUTOS ELSE COALESCE(n.TOTALVR,0) END
                           ELSE 0 END AS BaseCofins
                FROM NFM n
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND TRIM(COALESCE(n.OPERACAO,'')) IN ('E','S')
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND NOT EXISTS (
                      SELECT 1 FROM PNM p
                      WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ)
                  AND NOT EXISTS (
                      SELECT 1 FROM INM i
                      WHERE i.EMP_CODIGO=n.EMP_CODIGO AND i.NFM_SEQ=n.SEQ)
                UNION ALL
                SELECT 'S' AS Direcao,
                       'NVC-' || CAST(n.SEQ AS VARCHAR(20)) AS Documento,
                       COALESCE(p.VRTOTAL,0) AS ValorContabil,
                       ROUND(COALESCE(p.ICMSBASECALC,0)*COALESCE(p.ICMSALIQ,0)/100,2) AS Icms,
                       COALESCE(p.TFBASECALCPIS,0) AS BasePis,
                       COALESCE(p.TFBASECALCCOFINS,0) AS BaseCofins
                FROM NVC n
                INNER JOIN PNC p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NVC_SEQ=n.SEQ
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND n.DTEMISSAO>=@Inicio AND n.DTEMISSAO<@Fim
                  AND SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('5','6','7')
                  AND TRIM(p.CFO_CODIGO) NOT IN
                      ('5151','5152','5408','5409','6151','6152','6408','6409')
            ) x
            GROUP BY x.Direcao
            """, new { Codigo = codigo, Inicio = inicio, Fim = fim }, commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();

        var basesCreditoIcms = (await connection.QueryAsync<BaseCreditoIcmsLinha>(new CommandDefinition("""
            SELECT x.TipoOperacao,
                   x.UfParticipante,
                   SUM(x.Valor) AS Valor
            FROM (
                SELECT CASE
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','5') THEN 'I'
                         WHEN SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('2','6') THEN 'E'
                         ELSE 'X'
                       END AS TipoOperacao,
                       COALESCE(NULLIF(TRIM(par.UFD_SIGLA),''), NULLIF(TRIM(n.UFD_SIGLA),''), '') AS UfParticipante,
                       CASE WHEN TRIM(p.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202')
                            THEN -1 ELSE 1 END
                         * (COALESCE(p.VRTOTAL,0)+COALESCE(p.IPIVR,0)+COALESCE(p.ICMSSUBSTVR,0)) AS Valor
                FROM NFM n
                INNER JOIN PNM p ON p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ
                LEFT JOIN PAR par ON par.CODIGO=n.PAR_CODIGO
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND (
                    (SUBSTRING(TRIM(p.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3')
                     AND TRIM(p.CFO_CODIGO) NOT IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202'))
                    OR TRIM(p.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202'))
                  AND TRIM(p.CFO_CODIGO) NOT IN
                      ('1151','1152','1408','2151','2152','2408','2409',
                       '5151','5152','5408','5409','6151','6152','6408','6409')
                UNION ALL
                SELECT CASE
                         WHEN SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('1','5') THEN 'I'
                         WHEN SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('2','6') THEN 'E'
                         ELSE 'X'
                       END AS TipoOperacao,
                       COALESCE(NULLIF(TRIM(par.UFD_SIGLA),''), NULLIF(TRIM(n.UFD_SIGLA),''), NULLIF(TRIM(i.UFD_SIGLA),''), '') AS UfParticipante,
                       CASE WHEN TRIM(i.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202')
                            THEN -1 ELSE 1 END * COALESCE(i.VALOR,0) AS Valor
                FROM NFM n
                INNER JOIN INM i ON i.EMP_CODIGO=n.EMP_CODIGO AND i.NFM_SEQ=n.SEQ
                LEFT JOIN PAR par ON par.CODIGO=n.PAR_CODIGO
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND NOT EXISTS (
                      SELECT 1 FROM PNM p
                      WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ)
                  AND (
                    (SUBSTRING(TRIM(i.CFO_CODIGO) FROM 1 FOR 1) IN ('1','2','3')
                     AND TRIM(i.CFO_CODIGO) NOT IN ('1201','1202','1203','1204','2201','2202','2203','2204','3201','3202'))
                    OR TRIM(i.CFO_CODIGO) IN ('5201','5202','6201','6202','7201','7202'))
                  AND TRIM(i.CFO_CODIGO) NOT IN
                      ('1151','1152','1408','2151','2152','2408','2409',
                       '5151','5152','5408','5409','6151','6152','6408','6409')
                UNION ALL
                SELECT CASE
                         WHEN COALESCE(NULLIF(TRIM(par.UFD_SIGLA),''), NULLIF(TRIM(n.UFD_SIGLA),''), '')
                              = COALESCE(NULLIF(TRIM(est.MUN_UFD_SIGLA),''), '') THEN 'I'
                         ELSE 'E'
                       END AS TipoOperacao,
                       COALESCE(NULLIF(TRIM(par.UFD_SIGLA),''), NULLIF(TRIM(n.UFD_SIGLA),''), '') AS UfParticipante,
                       CASE WHEN COALESCE(n.TOTALPRODUTOS,0)>0
                            THEN n.TOTALPRODUTOS ELSE COALESCE(n.TOTALVR,0) END AS Valor
                FROM NFM n
                LEFT JOIN PAR par ON par.CODIGO=n.PAR_CODIGO
                LEFT JOIN EST est ON est.EMP_CODIGO=n.EMP_CODIGO AND est.CODIGO=n.EST_CODIGO
                WHERE TRIM(n.EMP_CODIGO)=@Codigo
                  AND COALESCE(n.CANCELADO,0)=0
                  AND TRIM(COALESCE(n.OPERACAO,''))='E'
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)>=@Inicio
                  AND COALESCE(n.DTENTRADASAIDA,n.DTEMISSAO)<@Fim
                  AND NOT EXISTS (
                      SELECT 1 FROM PNM p
                      WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ)
                  AND NOT EXISTS (
                      SELECT 1 FROM INM i
                      WHERE i.EMP_CODIGO=n.EMP_CODIGO AND i.NFM_SEQ=n.SEQ)
            ) x
            GROUP BY x.TipoOperacao, x.UfParticipante
            """, new { Codigo = codigo, Inicio = inicio, Fim = fim }, commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();

        var servicos = await connection.QuerySingleAsync<MovimentoServicoLinha>(new CommandDefinition("""
            SELECT COUNT(DISTINCT d.SEQ) AS QuantidadeDocumentos,
                   COALESCE(SUM(CASE
                     WHEN COALESCE(i.SERVICOS,0)>0 THEN i.SERVICOS
                     WHEN COALESCE(i.VRBRUTO,0)>0 THEN i.VRBRUTO
                     ELSE COALESCE(i.SUBTOTAL,0)
                   END),0) AS Receita,
                   COALESCE(SUM(ROUND(COALESCE(i.SERVICOS,0)*COALESCE(i.ALIQUOTA,0)/100,2)),0) AS Iss,
                   COALESCE(SUM(i.TFBASECALCPIS),0) AS BasePis,
                   COALESCE(SUM(i.TFBASECALCCOFINS),0) AS BaseCofins
            FROM DSS d
            INNER JOIN ITS i ON i.EMP_CODIGO=d.EMP_CODIGO AND i.DSS_SEQ=d.SEQ
            WHERE TRIM(d.EMP_CODIGO)=@Codigo
              AND COALESCE(d.CANCELADO,0)=0
              AND d.DTEMISSAO>=@Inicio AND d.DTEMISSAO<@Fim
            """, new { Codigo = codigo, Inicio = inicio, Fim = fim }, commandTimeout: 120,
            cancellationToken: cancellationToken));

        var ajustes = await connection.QuerySingleAsync<AjustesIcmsLinha>(new CommandDefinition("""
            SELECT COALESCE(SUM(CASE WHEN MOVIMENTO=1 THEN VALOR ELSE 0 END),0) AS Debitos,
                   COALESCE(SUM(CASE WHEN MOVIMENTO=3 THEN VALOR ELSE 0 END),0) AS Creditos
            FROM ICD
            WHERE TRIM(EMP_CODIGO)=@Codigo
              AND ANOMES BETWEEN @InicioAnoMes AND @FimAnoMes
              AND APURACAO=1 AND MOVIMENTO IN (1,3)
            """, new { Codigo = codigo, InicioAnoMes = inicioAnoMes, FimAnoMes = fimAnoMes },
            commandTimeout: 120, cancellationToken: cancellationToken));

        var folha = await connection.QuerySingleAsync<FolhaDiretaLinha>(new CommandDefinition("""
            SELECT COUNT(DISTINCT g.ANOMES) AS Competencias,
                   COALESCE(SUM(CASE WHEN e.PROVDESC=1 THEN p.VALOR ELSE 0 END),0) AS FolhaBruta,
                   COALESCE(SUM(CASE WHEN e.PROVDESC=0 AND UPPER(TRIM(e.NOME))='FGTS' THEN p.VALOR ELSE 0 END),0) AS Fgts
            FROM FOL f
            INNER JOIN FPG g ON g.EMP_CODIGO=f.EMP_CODIGO AND g.FOL_SEQ=f.SEQ
            INNER JOIN EFP p ON p.EMP_CODIGO=f.EMP_CODIGO AND p.EFO_FOL_SEQ=f.SEQ
            LEFT JOIN EVE e ON e.CODIGO=p.EVE_CODIGO
              AND (e.EMP_CODIGO=p.EMP_CODIGO OR e.EMP_CODIGO IS NULL)
            WHERE TRIM(f.EMP_CODIGO)=@Codigo
              AND g.ANOMES BETWEEN @InicioAnoMes AND @FimAnoMes
              AND f.ENCERRADA='S' AND g.TIPO='04'
            """, new { Codigo = codigo, InicioAnoMes = inicioAnoMes, FimAnoMes = fimAnoMes },
            commandTimeout: 120, cancellationToken: cancellationToken));

        var inssPatronal = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition("""
            SELECT COALESCE(SUM(INSSEMPRESA),0)
            FROM GPS
            WHERE TRIM(EMP_CODIGO)=@Codigo
              AND ANOMES BETWEEN @InicioAnoMes AND @FimAnoMes
            """, new { Codigo = codigo, InicioAnoMes = inicioAnoMes, FimAnoMes = fimAnoMes },
            commandTimeout: 120, cancellationToken: cancellationToken)) ?? 0m;

        var entradas = movimentos.FirstOrDefault(x => x.Direcao == "E") ?? new MovimentoFiscalLinha();
        var saidas = movimentos.FirstOrDefault(x => x.Direcao == "S") ?? new MovimentoFiscalLinha();
        var entradasInternas = basesCreditoIcms.Where(x => x.TipoOperacao == "I").Sum(x => x.Valor);
        var entradasNordeste = basesCreditoIcms
            .Where(x => x.TipoOperacao == "E" && UfsNordeste.Contains(x.UfParticipante))
            .Sum(x => x.Valor);
        var entradasSulSudeste = basesCreditoIcms
            .Where(x => x.TipoOperacao == "E" && UfsSulSudeste.Contains(x.UfParticipante))
            .Sum(x => x.Valor);
        var entradasOutrasInterestaduais = basesCreditoIcms
            .Where(x => x.TipoOperacao == "E"
                        && !UfsNordeste.Contains(x.UfParticipante)
                        && !UfsSulSudeste.Contains(x.UfParticipante))
            .Sum(x => x.Valor);
        var entradasSemRegra = basesCreditoIcms.Where(x => x.TipoOperacao == "X").Sum(x => x.Valor);
        var creditoIcmsPorUf = entradasInternas * 20m / 100m
                              + entradasNordeste * 12m / 100m
                              + entradasSulSudeste * 7m / 100m
                              + entradasOutrasInterestaduais * 12m / 100m
                              + ajustes.Creditos;
        var empresa = new FortesEmpresaOpcao(
            empresaLinha.Codigo,
            empresaLinha.RazaoSocial,
            empresaLinha.NomeFantasia,
            MontarCnpj(empresaLinha.CnpjBase, empresaLinha.SequencialCnpj),
            string.Equals(empresaLinha.OptanteSimples?.Trim(), "S", StringComparison.OrdinalIgnoreCase));

        return new FortesDadosRegimeTributario(
            empresa,
            ano,
            saidas.ValorContabil,
            servicos.Receita,
            entradas.ValorContabil,
            saidas.Icms + ajustes.Debitos,
            Math.Max(0m, creditoIcmsPorUf),
            entradasInternas,
            entradasNordeste,
            entradasSulSudeste,
            entradasOutrasInterestaduais,
            entradasSemRegra,
            ajustes.Creditos,
            servicos.Iss,
            Math.Max(entradas.BasePis, entradas.BaseCofins),
            Math.Max(saidas.BasePis + servicos.BasePis, saidas.BaseCofins + servicos.BaseCofins),
            folha.FolhaBruta,
            inssPatronal,
            folha.Fgts,
            folha.Competencias,
            saidas.QuantidadeDocumentos,
            servicos.QuantidadeDocumentos);
    }

    private async Task<FbConnection> AbrirConexaoAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("FortesFolha")
            ?? throw new InvalidOperationException("A conexão com o Fortes não foi configurada.");
        var senha = configuration["FortesFolha:Password"];
        if (string.IsNullOrWhiteSpace(senha))
            senha = Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        if (string.IsNullOrWhiteSpace(senha))
            throw new InvalidOperationException(
                "A senha do banco do Fortes não foi configurada no Secret Manager ou na variável FORTES_FOLHA_PASSWORD.");

        var builder = new FbConnectionStringBuilder(connectionString) { Password = senha };
        var connection = new FbConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string MontarCnpj(string? baseCnpj, string? sequencial)
    {
        var dozeDigitos = SomenteDigitos(baseCnpj).PadLeft(8, '0')
                         + SomenteDigitos(sequencial).PadLeft(4, '0');
        if (dozeDigitos.Length != 12) return dozeDigitos;
        var primeiro = DigitoCnpj(dozeDigitos, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var segundo = DigitoCnpj(dozeDigitos + primeiro, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return dozeDigitos + primeiro + segundo;
    }

    private static int DigitoCnpj(string valor, int[] pesos)
    {
        var soma = valor.Select((c, i) => (c - '0') * pesos[i]).Sum();
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static string SomenteDigitos(string? valor) => string.IsNullOrWhiteSpace(valor)
        ? string.Empty
        : new string(valor.Where(char.IsDigit).ToArray());

    private sealed class EmpresaFortesLinha
    {
        public string Codigo { get; set; } = string.Empty;
        public string RazaoSocial { get; set; } = string.Empty;
        public string? NomeFantasia { get; set; }
        public string CnpjBase { get; set; } = string.Empty;
        public string SequencialCnpj { get; set; } = string.Empty;
        public string? OptanteSimples { get; set; }
    }

    private sealed class MovimentoFiscalLinha
    {
        public string Direcao { get; set; } = string.Empty;
        public int QuantidadeDocumentos { get; set; }
        public decimal ValorContabil { get; set; }
        public decimal Icms { get; set; }
        public decimal BasePis { get; set; }
        public decimal BaseCofins { get; set; }
    }

    private sealed class MovimentoServicoLinha
    {
        public int QuantidadeDocumentos { get; set; }
        public decimal Receita { get; set; }
        public decimal Iss { get; set; }
        public decimal BasePis { get; set; }
        public decimal BaseCofins { get; set; }
    }

    private sealed class BaseCreditoIcmsLinha
    {
        public string TipoOperacao { get; set; } = string.Empty;
        public string UfParticipante { get; set; } = string.Empty;
        public decimal Valor { get; set; }
    }

    private sealed class AjustesIcmsLinha
    {
        public decimal Debitos { get; set; }
        public decimal Creditos { get; set; }
    }

    private sealed class FolhaDiretaLinha
    {
        public int Competencias { get; set; }
        public decimal FolhaBruta { get; set; }
        public decimal Fgts { get; set; }
    }

    private static readonly HashSet<string> UfsNordeste =
        new(["AL", "BA", "CE", "MA", "PB", "PE", "PI", "RN", "SE"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UfsSulSudeste =
        new(["ES", "MG", "RJ", "SP", "PR", "RS", "SC"], StringComparer.OrdinalIgnoreCase);
}
