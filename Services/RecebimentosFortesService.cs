using Dapper;
using FirebirdSql.Data.FirebirdClient;
using System.Data;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IRecebimentosFortesService
{
    Task<IReadOnlyList<RecebimentoFortesPreviaItem>> ConferirAsync(
        int empresaId,
        int ano,
        int mes,
        CancellationToken cancellationToken = default);

    Task<RecebimentoFortesAplicacaoResultado> AplicarAsync(
        int empresaId,
        int ano,
        int mes,
        CancellationToken cancellationToken = default);
}

public sealed class RecebimentoFortesPreviaItem
{
    public string Cliente { get; init; } = string.Empty;
    public string NumeroNota { get; init; } = string.Empty;
    public DateTime? DataEmissao { get; init; }
    public DateTime Vencimento { get; init; }
    public decimal ValorNota { get; init; }
    public decimal ValorRecebidoCompetencia { get; init; }
    public decimal ValorRecebidoTotalConhecido { get; init; }
    public decimal DescontoSugerido { get; init; }
    public decimal ValorTitulo { get; init; }
    public decimal ValorPisRetido { get; init; }
    public decimal ValorCofinsRetido { get; init; }
    public decimal PercentualReducaoBasePisCofins { get; init; }
    public decimal ValorBasePisCofins { get; init; }
    public string? Cfop { get; init; }
    public decimal TotalBaixadoFortes { get; init; }
    public decimal SaldoFortes { get; init; }
    public int QuantidadeMovimentos { get; init; }
    public int QuantidadePendentes { get; init; }
    public string Situacao { get; init; } = string.Empty;
    public string Acao { get; init; } = string.Empty;
    public string? Alerta { get; init; }
    public bool PodeAplicar { get; init; }
    public int? DssSeq { get; init; }
    public bool NotaComercio { get; init; }
    public int? FdfSeq { get; init; }
    public int? TituloSeq { get; init; }
    public IReadOnlyList<RecebimentoFortesMovimento> Movimentos { get; init; } = [];
}

public sealed record RecebimentoFortesMovimento(
    string ChaveNatural,
    DateTime Data,
    decimal Valor);

public sealed record RecebimentoFortesAplicacaoResultado(
    int FaturasCriadas,
    int BaixasCriadas,
    int DetalhamentosAtualizados,
    int MovimentosJaExistentes,
    int ItensIgnorados);

public sealed class RecebimentosFortesService(
    IConfiguration configuration,
    IEmpresaRepository empresas,
    IRecebimentoRepository recebimentos) : IRecebimentosFortesService
{
    private const decimal Tolerancia = 0.02m;
    private const string CnpjCompass = "29571855000154";
    private const string CnpjAe = "04124583000113";

    public async Task<IReadOnlyList<RecebimentoFortesPreviaItem>> ConferirAsync(
        int empresaId,
        int ano,
        int mes,
        CancellationToken cancellationToken = default)
    {
        ValidarCompetencia(ano, mes);
        var empresa = await empresas.GetByIdAsync(empresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigoFortes = ValidarCodigoFortes(empresa.CodigoEmpresaFiscalFortes);
        var percentualReducaoBasePisCofins = ObterPercentualReducaoBasePisCofins(empresa.Cnpj);
        var notaComercio = SomenteDigitos(empresa.Cnpj) == CnpjAe;
        var competencia = new DateTime(ano, mes, 1);
        var origens = await recebimentos.GetPreparacaoFortesAsync(empresaId, ano, mes, cancellationToken);
        if (origens.Count == 0)
            return [];

        await using var connection = await AbrirConexaoAsync(cancellationToken);
        var resultado = new List<RecebimentoFortesPreviaItem>();
        var origensPreparadas = new List<RecebimentoFortesOrigemDto>();
        foreach (var movimento in origens.GroupBy(x => x.MovimentoChaveNatural))
        {
            var itens = movimento.ToArray();
            var notasVinculadas = SepararNotas(itens[0].NotasVinculadas);
            var notasEncontradas = itens
                .Where(x => !string.IsNullOrWhiteSpace(x.NumeroNota))
                .Select(x => NormalizarNumero(x.NumeroNota!))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (notasVinculadas.Count > 1 || notasEncontradas > 1)
            {
                var divisao = await DividirRecebimentoAsync(
                    connection, codigoFortes, itens, competencia, notaComercio, cancellationToken);
                if (divisao.Bloqueio is not null)
                    resultado.Add(divisao.Bloqueio);
                else
                    origensPreparadas.AddRange(divisao.Origens);
            }
            else
            {
                origensPreparadas.Add(itens[0]);
            }
        }

        var grupos = origensPreparadas
            .GroupBy(x => new
            {
                Nota = x.NumeroNota ?? x.NotasVinculadas ?? string.Empty,
                x.Cliente,
                Emissao = x.DataEmissao?.Date
            })
            .OrderBy(x => x.Min(item => item.DataRecebimento))
            .ThenBy(x => x.Key.Cliente)
            .ToArray();

        foreach (var grupo in grupos)
            resultado.Add(await ConferirGrupoAsync(
                connection, codigoFortes, grupo.ToArray(), competencia,
                percentualReducaoBasePisCofins, notaComercio, cancellationToken));
        return resultado
            .OrderBy(x => x.Vencimento)
            .ThenBy(x => x.Cliente)
            .ThenBy(x => x.NumeroNota)
            .ToArray();
    }

    public async Task<RecebimentoFortesAplicacaoResultado> AplicarAsync(
        int empresaId,
        int ano,
        int mes,
        CancellationToken cancellationToken = default)
    {
        var previa = await ConferirAsync(empresaId, ano, mes, cancellationToken);
        var empresa = await empresas.GetByIdAsync(empresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigoFortes = ValidarCodigoFortes(empresa.CodigoEmpresaFiscalFortes);
        var faturasCriadas = 0;
        var baixasCriadas = 0;
        var detalhamentosAtualizados = 0;
        var jaExistentes = previa.Sum(x => x.QuantidadeMovimentos - x.QuantidadePendentes);
        var ignorados = previa.Count(x => !x.PodeAplicar);

        await using var connection = await AbrirConexaoAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in previa.Where(x => x.PodeAplicar && x.DssSeq.HasValue))
            {
                var parametrosDocumento = new
                {
                    CodigoFortes = codigoFortes,
                    DocumentoSeq = item.DssSeq!.Value
                };
                var consultaDocumento = item.NotaComercio
                    ? "SELECT FDF_SEQ FROM NFM WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@DocumentoSeq WITH LOCK"
                    : "SELECT FDF_SEQ FROM DSS WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@DocumentoSeq WITH LOCK";
                var fdfSeq = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    consultaDocumento, parametrosDocumento, transaction,
                    commandTimeout: 120, cancellationToken: cancellationToken));

                int tituloSeq;
                if (!fdfSeq.HasValue)
                {
                    fdfSeq = await ProximaFaturaAsync(connection, transaction, codigoFortes, cancellationToken);
                    var tipoFatura = await ObterTipoDuplicataAsync(connection, transaction, codigoFortes, cancellationToken);
                    var numeroFatura = item.NumeroNota.Trim();
                    var titulo = item.NotaComercio
                        ? "001"
                        : $"{NormalizarNumero(numeroFatura)}/00";
                    if (titulo.Length > 60)
                        titulo = titulo[^60..];

                    var parametrosFatura = new
                    {
                        CodigoFortes = codigoFortes,
                        FdfSeq = fdfSeq.Value,
                        Numero = numeroFatura,
                        TipoFatura = tipoFatura,
                        item.ValorNota,
                        Desconto = item.DescontoSugerido,
                        Titulo = titulo,
                        item.Vencimento,
                        item.ValorTitulo,
                        item.ValorPisRetido,
                        item.ValorCofinsRetido,
                        DssSeq = item.DssSeq.Value
                    };

                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO FDF
                            (EMP_CODIGO,SEQ,NUMERO,OPERACAO,TPF_CODIGO,VALOR,DESCONTO)
                        VALUES
                            (@CodigoFortes,@FdfSeq,@Numero,2,@TipoFatura,@ValorNota,@Desconto)
                        """, parametrosFatura, transaction, commandTimeout: 120,
                        cancellationToken: cancellationToken));
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO TDF
                            (EMP_CODIGO,FDF_SEQ,SEQ,TITULO,DTVENCIMENTO,VALOR,
                             TFRETFONTECOFINS,TFRETFONTEPIS,TFRETFONTECSL,TFRETFONTEIRPJ,
                             TFRETFONTECOFINSMISTA,TFRETFONTEPISMISTA)
                        VALUES
                            (@CodigoFortes,@FdfSeq,1,@Titulo,@Vencimento,@ValorTitulo,
                             @ValorCofinsRetido,@ValorPisRetido,0,0,0,0)
                        """, parametrosFatura, transaction, commandTimeout: 120,
                        cancellationToken: cancellationToken));
                    var atualizarDocumento = item.NotaComercio
                        ? "UPDATE NFM SET FATURA='V', FDF_SEQ=@FdfSeq WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@DssSeq"
                        : "UPDATE DSS SET FATURA='P', FDF_SEQ=@FdfSeq WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@DssSeq";
                    await connection.ExecuteAsync(new CommandDefinition(
                        atualizarDocumento, parametrosFatura, transaction, commandTimeout: 120,
                        cancellationToken: cancellationToken));
                    tituloSeq = 1;
                    faturasCriadas++;

                    if (item.NotaComercio)
                    {
                        var quantidade = await AplicarBaixasComercioAsync(
                            connection, transaction, codigoFortes, fdfSeq.Value,
                            item,
                            [new TituloFortes { Seq = tituloSeq, Valor = item.ValorNota, TotalBaixado = 0 }],
                            cancellationToken);
                        if (!quantidade.HasValue)
                            ignorados++;
                        else
                        {
                            baixasCriadas += quantidade.Value.BaixasCriadas;
                            detalhamentosAtualizados += quantidade.Value.DetalhamentosAtualizados;
                        }
                        continue;
                    }
                }
                else
                {
                    var titulos = (await connection.QueryAsync<TituloFortes>(new CommandDefinition("""
                        SELECT t.SEQ, t.VALOR,
                               COALESCE(SUM(COALESCE(b.VALORPAGO,0)+COALESCE(b.VALORDESCONTO,0)),0) AS TotalBaixado
                        FROM TDF t
                        LEFT JOIN BTD b ON b.EMP_CODIGO=t.EMP_CODIGO
                         AND b.TDF_FDF_SEQ=t.FDF_SEQ AND b.TDF_SEQ=t.SEQ
                        WHERE t.EMP_CODIGO=@CodigoFortes AND t.FDF_SEQ=@FdfSeq
                        GROUP BY t.SEQ,t.VALOR ORDER BY t.SEQ
                        """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq.Value }, transaction,
                        commandTimeout: 120, cancellationToken: cancellationToken))).AsList();
                    if (item.NotaComercio)
                    {
                        var titulosAjustados = await AjustarFaturaComercioAsync(
                            connection, transaction, codigoFortes, fdfSeq.Value,
                            item.ValorNota, titulos, cancellationToken);
                        if (titulosAjustados is null)
                        {
                            ignorados++;
                            continue;
                        }
                        var quantidade = await AplicarBaixasComercioAsync(
                            connection, transaction, codigoFortes, fdfSeq.Value,
                            item, titulosAjustados, cancellationToken);
                        if (!quantidade.HasValue)
                            ignorados++;
                        else
                        {
                            baixasCriadas += quantidade.Value.BaixasCriadas;
                            detalhamentosAtualizados += quantidade.Value.DetalhamentosAtualizados;
                        }
                        continue;
                    }
                    if (titulos.Count != 1)
                    {
                        ignorados++;
                        continue;
                    }
                    tituloSeq = titulos[0].Seq;
                }

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE TDF
                       SET TFRETFONTECOFINS=@ValorCofinsRetido,
                           TFRETFONTEPIS=@ValorPisRetido
                     WHERE EMP_CODIGO=@CodigoFortes AND FDF_SEQ=@FdfSeq AND SEQ=@TituloSeq
                    """, new
                {
                    CodigoFortes = codigoFortes,
                    FdfSeq = fdfSeq.Value,
                    TituloSeq = tituloSeq,
                    item.ValorCofinsRetido,
                    item.ValorPisRetido
                }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));

                var baixas = (await ObterBaixasAsync(
                    connection, transaction, codigoFortes, fdfSeq.Value, tituloSeq, cancellationToken)).ToList();
                var pendentes = RemoverMovimentosJaExistentes(item.Movimentos, baixas);
                var valorTituloAtual = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                    SELECT VALOR FROM TDF
                    WHERE EMP_CODIGO=@CodigoFortes AND FDF_SEQ=@FdfSeq AND SEQ=@TituloSeq
                    """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq.Value, TituloSeq = tituloSeq }, transaction,
                    commandTimeout: 120, cancellationToken: cancellationToken));
                var saldo = valorTituloAtual - baixas.Sum(x => x.ValorPago + x.ValorDesconto);
                if (pendentes.Sum(x => x.Valor) > saldo + Tolerancia)
                {
                    ignorados++;
                    continue;
                }

                var proximaBaixa = baixas.Select(x => x.Seq).DefaultIfEmpty(0).Max() + 1;
                var valorPagoAcumulado = baixas.Sum(x => x.ValorPago);
                var pisRetidoAcumulado = baixas.Sum(x => x.ValorPisRetido);
                var cofinsRetidoAcumulado = baixas.Sum(x => x.ValorCofinsRetido);
                foreach (var movimento in pendentes.OrderBy(x => x.Data).ThenBy(x => x.Valor))
                {
                    var baixaSeq = proximaBaixa++;
                    valorPagoAcumulado += movimento.Valor;
                    var pisRetidoAlvo = CalcularRetencaoAcumulada(
                        item.ValorPisRetido, valorPagoAcumulado, valorTituloAtual);
                    var cofinsRetidoAlvo = CalcularRetencaoAcumulada(
                        item.ValorCofinsRetido, valorPagoAcumulado, valorTituloAtual);
                    var pisRetidoMovimento = Math.Max(0, Math.Round(
                        pisRetidoAlvo - pisRetidoAcumulado, 2, MidpointRounding.AwayFromZero));
                    var cofinsRetidoMovimento = Math.Max(0, Math.Round(
                        cofinsRetidoAlvo - cofinsRetidoAcumulado, 2, MidpointRounding.AwayFromZero));
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO BTD
                            (EMP_CODIGO,TDF_FDF_SEQ,TDF_SEQ,SEQ,DTBAIXA,VALORPAGO,
                             VALORMULTA,VALORJUROS,VALORDESCONTO,
                             TFRETFONTECOFINS,TFRETFONTEPIS,
                             TFRETFONTECOFINSMISTA,TFRETFONTEPISMISTA)
                        VALUES
                            (@CodigoFortes,@FdfSeq,@TituloSeq,@Seq,@Data,@Valor,0,0,0,
                             @CofinsRetido,@PisRetido,0,0)
                        """, new
                    {
                        CodigoFortes = codigoFortes,
                        FdfSeq = fdfSeq.Value,
                        TituloSeq = tituloSeq,
                        Seq = baixaSeq,
                        movimento.Data,
                        movimento.Valor,
                        CofinsRetido = cofinsRetidoMovimento,
                        PisRetido = pisRetidoMovimento
                    }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));
                    await InserirDetalhamentoPisCofinsAsync(
                        connection, transaction, codigoFortes, fdfSeq.Value, tituloSeq,
                        baixaSeq, movimento.Valor, item.PercentualReducaoBasePisCofins,
                        null, cancellationToken);
                    pisRetidoAcumulado += pisRetidoMovimento;
                    cofinsRetidoAcumulado += cofinsRetidoMovimento;
                    baixasCriadas++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new RecebimentoFortesAplicacaoResultado(
            faturasCriadas, baixasCriadas, detalhamentosAtualizados, jaExistentes, ignorados);
    }

    private static async Task<(int BaixasCriadas, int DetalhamentosAtualizados)?> AplicarBaixasComercioAsync(
        FbConnection connection,
        IDbTransaction transaction,
        string codigoFortes,
        int fdfSeq,
        RecebimentoFortesPreviaItem item,
        IReadOnlyList<TituloFortes> titulos,
        CancellationToken cancellationToken)
    {
        var baixas = new List<BaixaFortes>();
        foreach (var titulo in titulos)
        {
            baixas.AddRange(await ObterBaixasAsync(
                connection, transaction, codigoFortes, fdfSeq, titulo.Seq, cancellationToken));
        }

        var pendentes = RemoverMovimentosJaExistentes(item.Movimentos, baixas);
        var baixasComDetalhamentoPendente = LocalizarBaixasDosMovimentos(item.Movimentos, baixas)
            .Where(x => x.DetalhamentosCompletos == 0)
            .ToArray();
        foreach (var baixa in baixasComDetalhamentoPendente)
        {
            await InserirDetalhamentoPisCofinsAsync(
                connection, transaction, codigoFortes, fdfSeq, baixa.TituloSeq,
                baixa.Seq, baixa.ValorPago, item.PercentualReducaoBasePisCofins,
                item.Cfop, cancellationToken);
        }
        if (!TentarAlocarMovimentosNosTitulos(pendentes, titulos, baixas, out var alocacoes))
            return null;

        var proximasBaixas = titulos.ToDictionary(
            x => x.Seq,
            x => baixas.Where(b => b.TituloSeq == x.Seq).Select(b => b.Seq).DefaultIfEmpty(0).Max() + 1);
        var quantidade = 0;
        foreach (var alocacao in alocacoes)
        {
            var baixaSeq = proximasBaixas[alocacao.TituloSeq]++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO BTD
                    (EMP_CODIGO,TDF_FDF_SEQ,TDF_SEQ,SEQ,DTBAIXA,VALORPAGO,
                     VALORMULTA,VALORJUROS,VALORDESCONTO,
                     TFRETFONTECOFINS,TFRETFONTEPIS,
                     TFRETFONTECOFINSMISTA,TFRETFONTEPISMISTA)
                VALUES
                    (@CodigoFortes,@FdfSeq,@TituloSeq,@Seq,@Data,@Valor,0,0,0,0,0,0,0)
                """, new
            {
                CodigoFortes = codigoFortes,
                FdfSeq = fdfSeq,
                TituloSeq = alocacao.TituloSeq,
                Seq = baixaSeq,
                alocacao.Movimento.Data,
                alocacao.Movimento.Valor
            }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));
            await InserirDetalhamentoPisCofinsAsync(
                connection, transaction, codigoFortes, fdfSeq, alocacao.TituloSeq,
                baixaSeq, alocacao.Movimento.Valor,
                item.PercentualReducaoBasePisCofins, item.Cfop, cancellationToken);
            quantidade++;
        }

        return (quantidade, baixasComDetalhamentoPendente.Length);
    }

    private static async Task<IReadOnlyList<TituloFortes>?> AjustarFaturaComercioAsync(
        FbConnection connection,
        IDbTransaction transaction,
        string codigoFortes,
        int fdfSeq,
        decimal valorNota,
        IReadOnlyList<TituloFortes> titulos,
        CancellationToken cancellationToken)
    {
        var projetados = ProjetarTitulosComValorIntegral(titulos, null, valorNota);
        if (projetados is null)
            return null;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE FDF
               SET VALOR=@ValorNota, DESCONTO=0
             WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@FdfSeq
            """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq, ValorNota = valorNota },
            transaction, commandTimeout: 120, cancellationToken: cancellationToken));

        foreach (var titulo in projetados)
        {
            var atual = titulos.Single(x => x.Seq == titulo.Seq);
            if (Math.Abs(atual.Valor - titulo.Valor) <= Tolerancia)
                continue;

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE TDF
                   SET VALOR=@Valor
                 WHERE EMP_CODIGO=@CodigoFortes AND FDF_SEQ=@FdfSeq AND SEQ=@TituloSeq
                """, new
            {
                CodigoFortes = codigoFortes,
                FdfSeq = fdfSeq,
                TituloSeq = titulo.Seq,
                titulo.Valor
            }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));
        }

        return projetados;
    }

    private static IReadOnlyList<TituloFortes>? ProjetarTitulosComValorIntegral(
        IReadOnlyList<TituloFortes> titulos,
        IReadOnlyList<BaixaFortes>? baixas,
        decimal valorNota)
    {
        if (titulos.Count == 0 || valorNota <= 0)
            return null;

        var projetados = titulos
            .OrderBy(x => x.Seq)
            .Select(x => new TituloFortes
            {
                Seq = x.Seq,
                Valor = Math.Round(x.Valor, 2, MidpointRounding.AwayFromZero),
                TotalBaixado = baixas is null
                    ? Math.Round(x.TotalBaixado, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(baixas.Where(b => b.TituloSeq == x.Seq)
                        .Sum(b => b.ValorPago + b.ValorDesconto), 2, MidpointRounding.AwayFromZero)
            })
            .ToList();
        if (projetados.Any(x => x.Valor + Tolerancia < x.TotalBaixado))
            return null;

        var diferenca = Math.Round(
            valorNota - projetados.Sum(x => x.Valor), 2, MidpointRounding.AwayFromZero);
        if (diferenca > Tolerancia)
        {
            projetados[^1].Valor = Math.Round(
                projetados[^1].Valor + diferenca, 2, MidpointRounding.AwayFromZero);
        }
        else if (diferenca < -Tolerancia)
        {
            var reducaoPendente = -diferenca;
            for (var indice = projetados.Count - 1; indice >= 0 && reducaoPendente > Tolerancia; indice--)
            {
                var titulo = projetados[indice];
                var disponivel = Math.Max(0, Math.Round(
                    titulo.Valor - titulo.TotalBaixado, 2, MidpointRounding.AwayFromZero));
                var reducao = Math.Min(disponivel, reducaoPendente);
                titulo.Valor = Math.Round(
                    titulo.Valor - reducao, 2, MidpointRounding.AwayFromZero);
                reducaoPendente = Math.Round(
                    reducaoPendente - reducao, 2, MidpointRounding.AwayFromZero);
            }
            if (reducaoPendente > Tolerancia)
                return null;
        }

        var residuo = Math.Round(
            valorNota - projetados.Sum(x => x.Valor), 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(residuo) > Tolerancia)
            return null;
        if (residuo != 0)
            projetados[^1].Valor += residuo;
        return projetados;
    }

    private async Task<DivisaoRecebimento> DividirRecebimentoAsync(
        FbConnection connection,
        string codigoFortes,
        IReadOnlyList<RecebimentoFortesOrigemDto> origens,
        DateTime competencia,
        bool notaComercio,
        CancellationToken cancellationToken)
    {
        var primeira = origens[0];
        var numeros = SepararNotas(primeira.NotasVinculadas);
        var valorRecebido = Math.Round(primeira.ValorRecebido, 2);
        var movimentoOriginal = new RecebimentoFortesMovimento(
            primeira.MovimentoChaveNatural, primeira.DataRecebimento.Date, valorRecebido);

        RecebimentoFortesPreviaItem Bloquear(string mensagem) => Bloqueado(
            primeira.Cliente,
            primeira.NotasVinculadas ?? string.Join("; ", numeros),
            primeira.DataEmissao,
            primeira.DataRecebimento.Date,
            valorRecebido,
            valorRecebido,
            1,
            [movimentoOriginal],
            mensagem);

        if (numeros.Count < 2)
            return new([], Bloquear("O recebimento não possui notas suficientes para realizar a divisão."));

        var detalhes = new List<NotaParaDivisao>(numeros.Count);
        foreach (var numero in numeros)
        {
            var origensNota = origens.Where(x =>
                !string.IsNullOrWhiteSpace(x.NumeroNota) &&
                NormalizarNumero(x.NumeroNota!) == NormalizarNumero(numero)).ToArray();
            if (origensNota.Length != 1)
                return new([], Bloquear($"A nota {numero} não foi vinculada de forma única à linha da planilha."));

            var origem = origensNota[0];
            var notas = await LocalizarNotasAsync(
                connection, codigoFortes, numero, origem.DataEmissao, competencia, notaComercio, cancellationToken);
            if (notas.Count != 1)
                return new([], Bloquear(notas.Count == 0
                    ? $"A nota {numero} não foi localizada no Fortes."
                    : $"Mais de uma nota do Fortes corresponde ao número {numero}."));

            var nota = notas[0];
            var saldo = nota.Valor;
            IReadOnlyList<BaixaFortes> baixas = [];
            if (nota.FdfSeq.HasValue)
            {
                var financeiro = await ObterFinanceiroAsync(
                    connection, null, codigoFortes, nota.FdfSeq.Value, cancellationToken);
                if (financeiro.Titulos.Count != 1)
                    return new([], Bloquear(
                        $"A fatura da nota {numero} possui quantidade de títulos diferente de um."));

                baixas = financeiro.Baixas;
                saldo = Math.Max(0, Math.Round(
                    financeiro.Titulos[0].Valor -
                    baixas.Sum(x => x.ValorPago + x.ValorDesconto), 2));
            }

            detalhes.Add(new(origem, nota, saldo, baixas));
        }

        decimal[]? distribuicao = null;
        var candidatos = new List<decimal[]>();
        if (detalhes.All(x => x.Origem.ValorFaturado is > 0))
            candidatos.Add(detalhes.Select(x => Math.Round(x.Origem.ValorFaturado!.Value, 2)).ToArray());
        candidatos.Add(detalhes.Select(x => Math.Round(x.Nota.Valor, 2)).ToArray());
        candidatos.Add(detalhes.Select(x => Math.Round(x.Saldo, 2)).ToArray());

        foreach (var candidato in candidatos)
        {
            if (Math.Abs(candidato.Sum() - valorRecebido) > Tolerancia)
                continue;

            var compativel = true;
            for (var indice = 0; indice < candidato.Length; indice++)
            {
                var valor = candidato[indice];
                var detalhe = detalhes[indice];
                var baixaJaExistente = detalhe.Baixas.Any(x =>
                    x.Data?.Date == primeira.DataRecebimento.Date &&
                    Math.Abs(Math.Round(x.ValorPago, 2) - valor) <= Tolerancia);
                if (valor < 0 || (valor > detalhe.Saldo + Tolerancia && !baixaJaExistente))
                {
                    compativel = false;
                    break;
                }
            }

            if (compativel)
            {
                distribuicao = candidato;
                break;
            }
        }

        if (distribuicao is null)
            return new([], Bloquear(
                "Não foi possível dividir automaticamente: o recebido não coincide com a soma dos valores das notas nem com a soma dos saldos restantes no Fortes."));

        var preparadas = new List<RecebimentoFortesOrigemDto>(detalhes.Count);
        for (var indice = 0; indice < detalhes.Count; indice++)
        {
            var valor = distribuicao[indice];
            if (valor <= 0)
                continue;
            var detalhe = detalhes[indice];
            preparadas.Add(new RecebimentoFortesOrigemDto
            {
                MovimentoChaveNatural = $"{primeira.MovimentoChaveNatural}|{NormalizarNumero(detalhe.Nota.Numero)}",
                DataRecebimento = primeira.DataRecebimento,
                ValorRecebido = valor,
                Cliente = primeira.Cliente,
                NotasVinculadas = detalhe.Origem.NumeroNota,
                NumeroNota = detalhe.Origem.NumeroNota,
                DataEmissao = detalhe.Origem.DataEmissao ?? detalhe.Nota.DataEmissao,
                ValorFaturado = detalhe.Origem.ValorFaturado,
                DescontoInformado = detalhe.Origem.DescontoInformado,
                ValorRecebidoTotalConhecido = valor
            });
        }

        return new(preparadas, null);
    }

    private async Task<RecebimentoFortesPreviaItem> ConferirGrupoAsync(
        FbConnection connection,
        string codigoFortes,
        IReadOnlyList<RecebimentoFortesOrigemDto> origens,
        DateTime competencia,
        decimal percentualReducaoBasePisCofins,
        bool notaComercio,
        CancellationToken cancellationToken)
    {
        var primeira = origens[0];
        var numeroInformado = primeira.NumeroNota ?? primeira.NotasVinculadas ?? string.Empty;
        var movimentos = origens.Select(x => new RecebimentoFortesMovimento(
            x.MovimentoChaveNatural, x.DataRecebimento.Date, Math.Round(x.ValorRecebido, 2))).ToArray();
        var itemBase = new
        {
            primeira.Cliente,
            NumeroNota = numeroInformado,
            primeira.DataEmissao,
            Vencimento = movimentos.Min(x => x.Data),
            ValorRecebidoCompetencia = movimentos.Sum(x => x.Valor),
            primeira.ValorRecebidoTotalConhecido,
            QuantidadeMovimentos = movimentos.Length,
            Movimentos = movimentos
        };

        if (string.IsNullOrWhiteSpace(numeroInformado) || numeroInformado.Contains(';'))
            return Bloqueado(itemBase.Cliente, itemBase.NumeroNota, itemBase.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "O recebimento não possui vínculo único com uma nota.");

        var notas = await LocalizarNotasAsync(
            connection, codigoFortes, numeroInformado, primeira.DataEmissao, competencia, notaComercio, cancellationToken);
        if (notas.Count == 0)
            return Bloqueado(itemBase.Cliente, itemBase.NumeroNota, itemBase.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "Nota não localizada no Fortes pela emissão e pelo número.");
        if (notas.Count > 1)
            return Bloqueado(itemBase.Cliente, itemBase.NumeroNota, itemBase.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "Mais de uma nota do Fortes corresponde ao número informado.");

        var nota = notas[0];
        var alertaDataEmissao = primeira.DataEmissao.HasValue &&
            primeira.DataEmissao.Value.Date != nota.DataEmissao.Date
                ? $"A emissão da planilha ({primeira.DataEmissao:dd/MM/yyyy}) difere do Fortes ({nota.DataEmissao:dd/MM/yyyy}); a nota foi vinculada pelo número."
                : null;
        var valorBasePisCofins = movimentos.Sum(x =>
            CalcularBasePisCofins(x.Valor, percentualReducaoBasePisCofins));
        if (notaComercio)
        {
            return await ConferirGrupoComercioAsync(
                connection, codigoFortes, primeira, nota, movimentos,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido,
                valorBasePisCofins, percentualReducaoBasePisCofins,
                alertaDataEmissao, cancellationToken);
        }

        var descontosInformados = origens.Where(x => x.DescontoInformado.HasValue)
            .Select(x => Math.Round(x.DescontoInformado!.Value, 2)).Distinct().ToArray();
        if (descontosInformados.Length > 1)
            return Bloqueado(itemBase.Cliente, nota.Numero, itemBase.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "A planilha informa descontos diferentes para a mesma nota.", nota.Valor, nota.Seq);

        var desconto = descontosInformados.SingleOrDefault();
        if (descontosInformados.Length == 0)
            desconto = Math.Max(0, Math.Round(nota.Valor - itemBase.ValorRecebidoTotalConhecido, 2));
        var valorTituloSugerido = Math.Round(nota.Valor - desconto, 2);
        if (desconto < 0 || valorTituloSugerido <= 0 || itemBase.ValorRecebidoTotalConhecido > valorTituloSugerido + Tolerancia)
            return Bloqueado(itemBase.Cliente, nota.Numero, itemBase.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "O desconto ou o total recebido é incompatível com o valor da nota.", nota.Valor, nota.Seq);

        if (!nota.FdfSeq.HasValue)
        {
            return new RecebimentoFortesPreviaItem
            {
                Cliente = itemBase.Cliente,
                NumeroNota = nota.Numero,
                DataEmissao = nota.DataEmissao,
                Vencimento = itemBase.Vencimento,
                ValorNota = nota.Valor,
                ValorRecebidoCompetencia = itemBase.ValorRecebidoCompetencia,
                ValorRecebidoTotalConhecido = itemBase.ValorRecebidoTotalConhecido,
                DescontoSugerido = desconto,
                ValorTitulo = valorTituloSugerido,
                ValorPisRetido = nota.ValorPisRetido,
                ValorCofinsRetido = nota.ValorCofinsRetido,
                PercentualReducaoBasePisCofins = percentualReducaoBasePisCofins,
                ValorBasePisCofins = valorBasePisCofins,
                Cfop = nota.Cfop,
                SaldoFortes = valorTituloSugerido,
                QuantidadeMovimentos = itemBase.QuantidadeMovimentos,
                QuantidadePendentes = itemBase.QuantidadeMovimentos,
                Situacao = "Sem fatura",
                Acao = "Criar fatura, título e baixa(s)",
                Alerta = alertaDataEmissao,
                PodeAplicar = true,
                DssSeq = nota.Seq,
                NotaComercio = nota.NotaComercio,
                Movimentos = movimentos
            };
        }

        var financeiro = await ObterFinanceiroAsync(
            connection, null, codigoFortes, nota.FdfSeq.Value, cancellationToken);
        if (financeiro.Titulos.Count != 1)
            return Bloqueado(itemBase.Cliente, nota.Numero, nota.DataEmissao, itemBase.Vencimento,
                itemBase.ValorRecebidoCompetencia, itemBase.ValorRecebidoTotalConhecido, itemBase.QuantidadeMovimentos,
                movimentos, "A fatura existente possui quantidade de títulos diferente de um; confira manualmente.",
                nota.Valor, nota.Seq, nota.FdfSeq);

        var tituloExistente = financeiro.Titulos[0];
        var pendentes = RemoverMovimentosJaExistentes(movimentos, financeiro.Baixas);
        var totalBaixado = financeiro.Baixas.Sum(x => x.ValorPago + x.ValorDesconto);
        var saldo = Math.Max(0, Math.Round(tituloExistente.Valor - totalBaixado, 2));
        var excedeSaldo = pendentes.Sum(x => x.Valor) > saldo + Tolerancia;
        var jaLancado = pendentes.Count == 0;

        return new RecebimentoFortesPreviaItem
        {
            Cliente = itemBase.Cliente,
            NumeroNota = nota.Numero,
            DataEmissao = nota.DataEmissao,
            Vencimento = itemBase.Vencimento,
            ValorNota = nota.Valor,
            ValorRecebidoCompetencia = itemBase.ValorRecebidoCompetencia,
            ValorRecebidoTotalConhecido = itemBase.ValorRecebidoTotalConhecido,
            DescontoSugerido = financeiro.Desconto,
            ValorTitulo = tituloExistente.Valor,
            ValorPisRetido = nota.ValorPisRetido,
            ValorCofinsRetido = nota.ValorCofinsRetido,
            PercentualReducaoBasePisCofins = percentualReducaoBasePisCofins,
            ValorBasePisCofins = valorBasePisCofins,
            TotalBaixadoFortes = totalBaixado,
            SaldoFortes = saldo,
            QuantidadeMovimentos = itemBase.QuantidadeMovimentos,
            QuantidadePendentes = pendentes.Count,
            Situacao = jaLancado ? "Já lançado" : excedeSaldo ? "Saldo insuficiente" : "Fatura existente",
            Acao = jaLancado ? "Nenhuma alteração" : excedeSaldo ? "Conferir manualmente" : "Adicionar baixa(s) pendente(s)",
            Alerta = CombinarAlertas(
                alertaDataEmissao,
                excedeSaldo
                    ? $"As baixas pendentes excedem o saldo do título em {pendentes.Sum(x => x.Valor) - saldo:C2}."
                    : null),
            PodeAplicar = !jaLancado && !excedeSaldo,
            DssSeq = nota.Seq,
            NotaComercio = nota.NotaComercio,
            FdfSeq = nota.FdfSeq,
            TituloSeq = tituloExistente.Seq,
            Movimentos = movimentos
        };
    }

    private static async Task<RecebimentoFortesPreviaItem> ConferirGrupoComercioAsync(
        FbConnection connection,
        string codigoFortes,
        RecebimentoFortesOrigemDto primeira,
        NotaFortes nota,
        IReadOnlyList<RecebimentoFortesMovimento> movimentos,
        decimal valorRecebidoCompetencia,
        decimal valorRecebidoTotalConhecido,
        decimal valorBasePisCofins,
        decimal percentualReducaoBasePisCofins,
        string? alertaDataEmissao,
        CancellationToken cancellationToken)
    {
        var vencimento = movimentos.Min(x => x.Data);
        if (valorRecebidoTotalConhecido > nota.Valor + Tolerancia)
        {
            return Bloqueado(
                primeira.Cliente, nota.Numero, nota.DataEmissao, vencimento,
                valorRecebidoCompetencia, valorRecebidoTotalConhecido, movimentos.Count, movimentos,
                "O total recebido informado excede o valor da nota comercial.", nota.Valor, nota.Seq,
                notaComercio: true);
        }

        if (nota.QuantidadeCfops != 1 || string.IsNullOrWhiteSpace(nota.Cfop))
        {
            return Bloqueado(
                primeira.Cliente, nota.Numero, nota.DataEmissao, vencimento,
                valorRecebidoCompetencia, valorRecebidoTotalConhecido, movimentos.Count, movimentos,
                "A nota comercial não possui um único CFOP identificável para o detalhamento de PIS/COFINS.",
                nota.Valor, nota.Seq, nota.FdfSeq, notaComercio: true, cfop: nota.Cfop);
        }

        if (!nota.FdfSeq.HasValue)
        {
            var saldoAposAplicacao = Math.Max(0, Math.Round(
                nota.Valor - movimentos.Sum(x => x.Valor), 2));

            return new RecebimentoFortesPreviaItem
            {
                Cliente = primeira.Cliente,
                NumeroNota = nota.Numero,
                DataEmissao = nota.DataEmissao,
                Vencimento = vencimento,
                ValorNota = nota.Valor,
                ValorRecebidoCompetencia = valorRecebidoCompetencia,
                ValorRecebidoTotalConhecido = valorRecebidoTotalConhecido,
                DescontoSugerido = 0,
                ValorTitulo = nota.Valor,
                PercentualReducaoBasePisCofins = percentualReducaoBasePisCofins,
                ValorBasePisCofins = valorBasePisCofins,
                Cfop = nota.Cfop,
                TotalBaixadoFortes = 0,
                SaldoFortes = saldoAposAplicacao,
                QuantidadeMovimentos = movimentos.Count,
                QuantidadePendentes = movimentos.Count,
                Situacao = "Sem fatura",
                Acao = "Criar fatura integral e baixar somente o recebido",
                Alerta = CombinarAlertas(
                    alertaDataEmissao,
                    saldoAposAplicacao > Tolerancia
                        ? $"Saldo devedor após aplicar: {saldoAposAplicacao:C2}."
                        : null),
                PodeAplicar = true,
                DssSeq = nota.Seq,
                NotaComercio = true,
                Movimentos = movimentos
            };
        }

        var financeiro = await ObterFinanceiroAsync(
            connection, null, codigoFortes, nota.FdfSeq.Value, cancellationToken);
        if (financeiro.Valor > nota.Valor + Tolerancia || financeiro.Titulos.Count == 0)
        {
            return Bloqueado(
                primeira.Cliente, nota.Numero, nota.DataEmissao, vencimento,
                valorRecebidoCompetencia, valorRecebidoTotalConhecido, movimentos.Count, movimentos,
                "A fatura da nota comercial é incompatível com o valor da nota ou não possui títulos.",
                nota.Valor, nota.Seq, nota.FdfSeq, notaComercio: true);
        }

        var titulosProjetados = ProjetarTitulosComValorIntegral(
            financeiro.Titulos, financeiro.Baixas, nota.Valor);
        if (titulosProjetados is null)
        {
            return Bloqueado(
                primeira.Cliente, nota.Numero, nota.DataEmissao, vencimento,
                valorRecebidoCompetencia, valorRecebidoTotalConhecido, movimentos.Count, movimentos,
                "Não foi possível ajustar os títulos existentes ao valor integral da nota sem reduzir uma baixa já realizada.",
                nota.Valor, nota.Seq, nota.FdfSeq, notaComercio: true, cfop: nota.Cfop);
        }

        var pendentes = RemoverMovimentosJaExistentes(movimentos, financeiro.Baixas);
        var baixasCorrespondentes = LocalizarBaixasDosMovimentos(movimentos, financeiro.Baixas);
        var detalhamentosPendentes = baixasCorrespondentes.Any(x => x.DetalhamentosCompletos == 0);
        var podeAlocar = TentarAlocarMovimentosNosTitulos(
            pendentes, titulosProjetados, financeiro.Baixas, out _);
        var totalBaixadoTitulos = financeiro.Baixas.Sum(x => x.ValorPago + x.ValorDesconto);
        var saldoProjetado = Math.Max(0, Math.Round(
            nota.Valor - totalBaixadoTitulos - pendentes.Sum(x => x.Valor), 2));
        var baixasAcimaDoRecebido =
            totalBaixadoTitulos > valorRecebidoTotalConhecido + Tolerancia;
        var ajusteValorPendente =
            Math.Abs(financeiro.Valor - nota.Valor) > Tolerancia ||
            Math.Abs(financeiro.Titulos.Sum(x => x.Valor) - nota.Valor) > Tolerancia;
        var parcelasIncompativeis = pendentes.Count > 0 && !podeAlocar;
        var jaLancado = pendentes.Count == 0 && !detalhamentosPendentes &&
            !ajusteValorPendente && !baixasAcimaDoRecebido;

        return new RecebimentoFortesPreviaItem
        {
            Cliente = primeira.Cliente,
            NumeroNota = nota.Numero,
            DataEmissao = nota.DataEmissao,
            Vencimento = vencimento,
            ValorNota = nota.Valor,
            ValorRecebidoCompetencia = valorRecebidoCompetencia,
            ValorRecebidoTotalConhecido = valorRecebidoTotalConhecido,
            DescontoSugerido = 0,
            ValorTitulo = nota.Valor,
            ValorPisRetido = 0,
            ValorCofinsRetido = 0,
            PercentualReducaoBasePisCofins = percentualReducaoBasePisCofins,
            ValorBasePisCofins = valorBasePisCofins,
            Cfop = nota.Cfop,
            TotalBaixadoFortes = totalBaixadoTitulos,
            SaldoFortes = saldoProjetado,
            QuantidadeMovimentos = movimentos.Count,
            QuantidadePendentes = pendentes.Count,
            Situacao = baixasAcimaDoRecebido
                ? "Baixas divergentes"
                : parcelasIncompativeis
                ? "Parcelas incompatíveis"
                : jaLancado
                ? "Já considerado"
                : ajusteValorPendente
                    ? "Ajuste de fatura"
                : pendentes.Count == 0 && detalhamentosPendentes
                    ? "Detalhamento pendente"
                    : podeAlocar ? "Fatura existente" : "Parcelas incompatíveis",
            Acao = baixasAcimaDoRecebido
                ? "Conferir baixas manualmente"
                : parcelasIncompativeis
                ? "Conferir manualmente"
                : jaLancado
                ? "Nenhuma alteração"
                : ajusteValorPendente && pendentes.Count > 0
                    ? "Ajustar fatura integral e adicionar baixa(s)"
                : ajusteValorPendente
                    ? "Ajustar fatura e título ao valor integral"
                : pendentes.Count == 0 && detalhamentosPendentes
                    ? "Completar PIS/COFINS"
                    : podeAlocar ? "Adicionar baixa(s) pendente(s)" : "Conferir manualmente",
            Alerta = CombinarAlertas(
                alertaDataEmissao,
                saldoProjetado > Tolerancia
                    ? $"Saldo devedor após aplicar: {saldoProjetado:C2}."
                    : null,
                baixasAcimaDoRecebido
                    ? $"O Fortes possui {totalBaixadoTitulos:C2} em baixas, acima do total recebido conhecido de {valorRecebidoTotalConhecido:C2}."
                    : null,
                !jaLancado && !podeAlocar
                    ? "As parcelas pendentes não cabem nos saldos dos títulos existentes."
                    : null),
            PodeAplicar = !baixasAcimaDoRecebido && !parcelasIncompativeis &&
                (ajusteValorPendente || detalhamentosPendentes || pendentes.Count > 0),
            DssSeq = nota.Seq,
            NotaComercio = true,
            FdfSeq = nota.FdfSeq,
            TituloSeq = financeiro.Titulos.Count == 1 ? financeiro.Titulos[0].Seq : null,
            Movimentos = movimentos
        };
    }

    private static RecebimentoFortesPreviaItem Bloqueado(
        string cliente,
        string numeroNota,
        DateTime? emissao,
        DateTime vencimento,
        decimal recebidoCompetencia,
        decimal recebidoTotal,
        int quantidadeMovimentos,
        IReadOnlyList<RecebimentoFortesMovimento> movimentos,
        string alerta,
        decimal valorNota = 0,
        int? dssSeq = null,
        int? fdfSeq = null,
        bool notaComercio = false,
        string? cfop = null) => new()
        {
            Cliente = cliente,
            NumeroNota = numeroNota,
            DataEmissao = emissao,
            Vencimento = vencimento,
            ValorNota = valorNota,
            ValorRecebidoCompetencia = recebidoCompetencia,
            ValorRecebidoTotalConhecido = recebidoTotal,
            QuantidadeMovimentos = quantidadeMovimentos,
            Situacao = "Bloqueado",
            Acao = "Conferir manualmente",
            Alerta = alerta,
            DssSeq = dssSeq,
            NotaComercio = notaComercio,
            Cfop = cfop,
            FdfSeq = fdfSeq,
            Movimentos = movimentos
        };

    private static string? CombinarAlertas(params string?[] alertas)
    {
        var preenchidos = alertas.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return preenchidos.Length == 0 ? null : string.Join(" ", preenchidos);
    }

    private static async Task<IReadOnlyList<NotaFortes>> LocalizarNotasAsync(
        FbConnection connection,
        string codigoFortes,
        string numeroInformado,
        DateTime? emissao,
        DateTime competencia,
        bool notaComercio,
        CancellationToken cancellationToken)
    {
        var informado = NormalizarNumero(numeroInformado);
        var sufixoComparacao = informado.Length > 8 ? informado[^8..] : informado;

        NotaFortes[] Filtrar(IEnumerable<NotaFortes> notas) => notas.Where(x =>
        {
            var numero = NormalizarNumero(x.Numero);
            return numero == informado || numero.EndsWith(sufixoComparacao, StringComparison.Ordinal);
        }).ToArray();

        if (notaComercio)
        {
            IEnumerable<NotaFortes> candidatas;
            if (emissao.HasValue)
            {
                candidatas = await connection.QueryAsync<NotaFortes>(new CommandDefinition("""
                    SELECT n.SEQ, TRIM(n.NUMERO) AS Numero, n.DTEMISSAO AS DataEmissao,
                           COALESCE(n.TOTALVR,0) AS Valor, n.FDF_SEQ AS FdfSeq,
                           COALESCE(n.TFRETFONTEPIS,0) AS ValorPisRetido,
                           COALESCE(n.TFRETFONTECOFINS,0) AS ValorCofinsRetido,
                           (SELECT MIN(TRIM(p.CFO_CODIGO)) FROM PNM p
                             WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ) AS Cfop,
                           (SELECT COUNT(DISTINCT TRIM(p.CFO_CODIGO)) FROM PNM p
                             WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ) AS QuantidadeCfops
                    FROM NFM n
                    WHERE n.EMP_CODIGO=@CodigoFortes AND n.OPERACAO='S'
                      AND n.DTEMISSAO=@Emissao AND COALESCE(n.CANCELADO,0)=0
                    """, new { CodigoFortes = codigoFortes, Emissao = emissao.Value.Date },
                    commandTimeout: 120, cancellationToken: cancellationToken));
            }
            else
            {
                var fimCompetenciaComercio = new DateTime(competencia.Year, competencia.Month, 1).AddMonths(1);
                candidatas = await connection.QueryAsync<NotaFortes>(new CommandDefinition("""
                    SELECT FIRST 50 n.SEQ, TRIM(n.NUMERO) AS Numero, n.DTEMISSAO AS DataEmissao,
                           COALESCE(n.TOTALVR,0) AS Valor, n.FDF_SEQ AS FdfSeq,
                           COALESCE(n.TFRETFONTEPIS,0) AS ValorPisRetido,
                           COALESCE(n.TFRETFONTECOFINS,0) AS ValorCofinsRetido,
                           (SELECT MIN(TRIM(p.CFO_CODIGO)) FROM PNM p
                             WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ) AS Cfop,
                           (SELECT COUNT(DISTINCT TRIM(p.CFO_CODIGO)) FROM PNM p
                             WHERE p.EMP_CODIGO=n.EMP_CODIGO AND p.NFM_SEQ=n.SEQ) AS QuantidadeCfops
                    FROM NFM n
                    WHERE n.EMP_CODIGO=@CodigoFortes AND n.OPERACAO='S'
                      AND TRIM(n.NUMERO) LIKE '%' || @Sufixo
                      AND n.DTEMISSAO<@FimCompetencia AND COALESCE(n.CANCELADO,0)=0
                    ORDER BY n.DTEMISSAO DESC
                    """, new
                {
                    CodigoFortes = codigoFortes,
                    Sufixo = sufixoComparacao,
                    FimCompetencia = fimCompetenciaComercio
                }, commandTimeout: 120, cancellationToken: cancellationToken));
            }

            var encontradas = Filtrar(candidatas);
            foreach (var encontrada in encontradas)
                encontrada.NotaComercio = true;
            return encontradas;
        }

        if (emissao.HasValue)
        {
            var candidatasNaData = await connection.QueryAsync<NotaFortes>(new CommandDefinition("""
                SELECT d.SEQ, TRIM(d.NRINICIAL) AS Numero, d.DTEMISSAO AS DataEmissao,
                       COALESCE(d.VRTOTAL,d.VRBRUTO,0) AS Valor, d.FDF_SEQ AS FdfSeq,
                       COALESCE(d.TFRETFONTEPIS,0) AS ValorPisRetido,
                       COALESCE(d.TFRETFONTECOFINS,0) AS ValorCofinsRetido
                FROM DSS d
                WHERE d.EMP_CODIGO=@CodigoFortes AND d.DTEMISSAO=@Emissao
                  AND COALESCE(d.CANCELADO,0)=0
                """, new { CodigoFortes = codigoFortes, Emissao = emissao.Value.Date },
                commandTimeout: 120, cancellationToken: cancellationToken));
            var encontradasNaData = Filtrar(candidatasNaData);
            if (encontradasNaData.Length > 0)
                return encontradasNaData;
        }

        var inicioCompetencia = new DateTime(competencia.Year, competencia.Month, 1);
        var fimCompetencia = inicioCompetencia.AddMonths(1);
        if (emissao.HasValue &&
            (emissao.Value.Year != competencia.Year || emissao.Value.Month != competencia.Month))
            return [];

        var candidatasPorNumero = await connection.QueryAsync<NotaFortes>(new CommandDefinition(
            """
            SELECT FIRST 20 d.SEQ, TRIM(d.NRINICIAL) AS Numero, d.DTEMISSAO AS DataEmissao,
                   COALESCE(d.VRTOTAL,d.VRBRUTO,0) AS Valor, d.FDF_SEQ AS FdfSeq,
                   COALESCE(d.TFRETFONTEPIS,0) AS ValorPisRetido,
                   COALESCE(d.TFRETFONTECOFINS,0) AS ValorCofinsRetido
            FROM DSS d
            WHERE d.EMP_CODIGO=@CodigoFortes AND TRIM(d.NRINICIAL) LIKE '%' || @Sufixo
              AND d.DTEMISSAO>=@InicioCompetencia AND d.DTEMISSAO<@FimCompetencia
              AND COALESCE(d.CANCELADO,0)=0
            ORDER BY d.DTEMISSAO DESC
            """, new
            {
                CodigoFortes = codigoFortes,
                Sufixo = sufixoComparacao,
                InicioCompetencia = inicioCompetencia,
                FimCompetencia = fimCompetencia
            },
            commandTimeout: 120, cancellationToken: cancellationToken));
        return Filtrar(candidatasPorNumero);
    }

    private static async Task<FinanceiroFortes> ObterFinanceiroAsync(
        FbConnection connection,
        IDbTransaction? transaction,
        string codigoFortes,
        int fdfSeq,
        CancellationToken cancellationToken)
    {
        var fatura = await connection.QuerySingleAsync<FaturaFortes>(new CommandDefinition("""
            SELECT VALOR, COALESCE(DESCONTO,0) AS Desconto
            FROM FDF WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@FdfSeq
            """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));
        var titulos = (await connection.QueryAsync<TituloFortes>(new CommandDefinition("""
            SELECT SEQ, COALESCE(VALOR,0) AS Valor, 0 AS TotalBaixado
            FROM TDF WHERE EMP_CODIGO=@CodigoFortes AND FDF_SEQ=@FdfSeq ORDER BY SEQ
            """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken))).AsList();
        var baixas = new List<BaixaFortes>();
        foreach (var titulo in titulos)
            baixas.AddRange(await ObterBaixasAsync(
                connection, transaction, codigoFortes, fdfSeq, titulo.Seq, cancellationToken));
        return new FinanceiroFortes(fatura.Valor, fatura.Desconto, titulos, baixas);
    }

    private static async Task<IReadOnlyList<BaixaFortes>> ObterBaixasAsync(
        FbConnection connection,
        IDbTransaction? transaction,
        string codigoFortes,
        int fdfSeq,
        int tituloSeq,
        CancellationToken cancellationToken) =>
        (await connection.QueryAsync<BaixaFortes>(new CommandDefinition("""
            SELECT b.SEQ, b.DTBAIXA AS Data, COALESCE(b.VALORPAGO,0) AS ValorPago,
                   COALESCE(b.VALORDESCONTO,0) AS ValorDesconto,
                   COALESCE(b.TFRETFONTEPIS,0) AS ValorPisRetido,
                   COALESCE(b.TFRETFONTECOFINS,0) AS ValorCofinsRetido,
                   b.TDF_SEQ AS TituloSeq,
                   (SELECT COUNT(*) FROM ARC a
                     WHERE a.EMP_CODIGO=b.EMP_CODIGO
                       AND a.BTD_TDF_FDF_SEQ=b.TDF_FDF_SEQ
                       AND a.BTD_TDF_SEQ=b.TDF_SEQ AND a.BTD_SEQ=b.SEQ
                       AND a.CFO_CODIGO IS NOT NULL
                       AND a.CSTCOFINS='01' AND a.CSTPIS='01'
                       AND a.TFBASECALCCOFINS IS NOT NULL AND a.TFBASECALCPIS IS NOT NULL)
                       AS DetalhamentosCompletos
            FROM BTD b
            WHERE b.EMP_CODIGO=@CodigoFortes AND b.TDF_FDF_SEQ=@FdfSeq AND b.TDF_SEQ=@TituloSeq
            ORDER BY b.SEQ
            """, new { CodigoFortes = codigoFortes, FdfSeq = fdfSeq, TituloSeq = tituloSeq }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken))).AsList();

    private static List<RecebimentoFortesMovimento> RemoverMovimentosJaExistentes(
        IReadOnlyList<RecebimentoFortesMovimento> movimentos,
        IReadOnlyList<BaixaFortes> baixas)
    {
        var existentes = baixas
            .GroupBy(x => (x.Data?.Date, Valor: Math.Round(x.ValorPago, 2)))
            .ToDictionary(x => x.Key, x => x.Count());
        var pendentes = new List<RecebimentoFortesMovimento>();
        foreach (var movimento in movimentos.OrderBy(x => x.Data).ThenBy(x => x.Valor))
        {
            var chave = ((DateTime?)movimento.Data.Date, Math.Round(movimento.Valor, 2));
            if (existentes.TryGetValue(chave, out var quantidade) && quantidade > 0)
                existentes[chave] = quantidade - 1;
            else
                pendentes.Add(movimento);
        }
        return pendentes;
    }

    private static IReadOnlyList<BaixaFortes> LocalizarBaixasDosMovimentos(
        IReadOnlyList<RecebimentoFortesMovimento> movimentos,
        IReadOnlyList<BaixaFortes> baixas)
    {
        var disponiveis = baixas
            .GroupBy(x => (x.Data?.Date, Valor: Math.Round(x.ValorPago, 2)))
            .ToDictionary(x => x.Key, x => new Queue<BaixaFortes>(x.OrderBy(item => item.TituloSeq).ThenBy(item => item.Seq)));
        var localizadas = new List<BaixaFortes>();
        foreach (var movimento in movimentos.OrderBy(x => x.Data).ThenBy(x => x.Valor))
        {
            var chave = ((DateTime?)movimento.Data.Date, Math.Round(movimento.Valor, 2));
            if (disponiveis.TryGetValue(chave, out var correspondentes) && correspondentes.Count > 0)
                localizadas.Add(correspondentes.Dequeue());
        }
        return localizadas;
    }

    private static bool TentarAlocarMovimentosNosTitulos(
        IReadOnlyList<RecebimentoFortesMovimento> movimentos,
        IReadOnlyList<TituloFortes> titulos,
        IReadOnlyList<BaixaFortes> baixas,
        out IReadOnlyList<MovimentoTitulo> alocacoes)
    {
        var saldos = titulos.ToDictionary(
            x => x.Seq,
            x => Math.Max(0, Math.Round(
                x.Valor - baixas.Where(b => b.TituloSeq == x.Seq)
                    .Sum(b => b.ValorPago + b.ValorDesconto), 2)));
        var resultado = new List<MovimentoTitulo>(movimentos.Count);
        foreach (var movimento in movimentos.OrderBy(x => x.Data).ThenBy(x => x.Valor))
        {
            var titulo = titulos
                .Where(x => Math.Abs(saldos[x.Seq] - movimento.Valor) <= Tolerancia)
                .OrderBy(x => x.Seq)
                .FirstOrDefault()
                ?? titulos.Where(x => saldos[x.Seq] + Tolerancia >= movimento.Valor)
                    .OrderBy(x => x.Seq)
                    .FirstOrDefault();
            if (titulo is null)
            {
                alocacoes = [];
                return false;
            }

            saldos[titulo.Seq] = Math.Max(0, Math.Round(saldos[titulo.Seq] - movimento.Valor, 2));
            resultado.Add(new MovimentoTitulo(titulo.Seq, movimento));
        }

        alocacoes = resultado;
        return true;
    }

    private static decimal CalcularRetencaoAcumulada(
        decimal retencaoTotal,
        decimal valorPagoAcumulado,
        decimal valorTitulo)
    {
        var total = Math.Max(0, Math.Round(retencaoTotal, 2, MidpointRounding.AwayFromZero));
        if (total == 0 || valorPagoAcumulado <= 0)
            return 0;
        if (valorTitulo <= 0 || valorPagoAcumulado >= valorTitulo - Tolerancia)
            return total;
        return Math.Min(total, Math.Round(
            total * valorPagoAcumulado / valorTitulo, 2, MidpointRounding.AwayFromZero));
    }

    private static decimal ObterPercentualReducaoBasePisCofins(string cnpj) =>
        SomenteDigitos(cnpj) == CnpjCompass ? 5m : 0m;

    private static decimal CalcularBasePisCofins(decimal valorReceita, decimal percentualReducao)
    {
        var receita = Math.Round(valorReceita, 2, MidpointRounding.AwayFromZero);
        var percentualAplicavel = Math.Clamp(percentualReducao, 0m, 100m);
        return Math.Round(receita * (1m - percentualAplicavel / 100m), 2, MidpointRounding.AwayFromZero);
    }

    private static async Task InserirDetalhamentoPisCofinsAsync(
        FbConnection connection,
        IDbTransaction transaction,
        string codigoFortes,
        int fdfSeq,
        int tituloSeq,
        int baixaSeq,
        decimal valorRecebido,
        decimal percentualReducaoBase,
        string? cfop,
        CancellationToken cancellationToken)
    {
        var valorReceita = Math.Round(valorRecebido, 2, MidpointRounding.AwayFromZero);
        var valorBase = CalcularBasePisCofins(valorReceita, percentualReducaoBase);
        var valorCofins = Math.Round(valorBase * 0.03m, 2, MidpointRounding.AwayFromZero);
        var valorPis = Math.Round(valorBase * 0.0065m, 2, MidpointRounding.AwayFromZero);
        var cfopNormalizado = NormalizarCfop(cfop);
        var parametrosDetalhamento = new
        {
            CodigoFortes = codigoFortes,
            FdfSeq = fdfSeq,
            TituloSeq = tituloSeq,
            BaixaSeq = baixaSeq,
            Cfop = cfopNormalizado,
            ValorReceita = valorReceita,
            ValorBase = valorBase,
            ValorCofins = valorCofins,
            ValorPis = valorPis
        };
        var existe = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM ARC
            WHERE EMP_CODIGO=@CodigoFortes AND BTD_TDF_FDF_SEQ=@FdfSeq
              AND BTD_TDF_SEQ=@TituloSeq AND BTD_SEQ=@BaixaSeq
            """, parametrosDetalhamento,
            transaction, commandTimeout: 120, cancellationToken: cancellationToken));
        if (existe > 0)
        {
            if (cfopNormalizado is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE ARC
                       SET CFO_CODIGO=@Cfop,
                           CSTCOFINS='01', TIPOCALCULOCOFINS=1,
                           VALORRECEITACOFINS=@ValorReceita, TFBASECALCCOFINS=@ValorBase,
                           ALIQUOTACOFINSPERC=3, VALORCOFINS=@ValorCofins,
                           CSTPIS='01', TIPOCALCULOPIS=1,
                           VALORRECEITAPIS=@ValorReceita, TFBASECALCPIS=@ValorBase,
                           ALIQUOTAPISPERC=0.65, VALORPIS=@ValorPis,
                           EXCLUSAOBCPISCOFINS=0
                     WHERE EMP_CODIGO=@CodigoFortes AND BTD_TDF_FDF_SEQ=@FdfSeq
                       AND BTD_TDF_SEQ=@TituloSeq AND BTD_SEQ=@BaixaSeq
                    """, parametrosDetalhamento, transaction,
                    commandTimeout: 120, cancellationToken: cancellationToken));
            }
            return;
        }

        var ultimaSeq = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT FIRST 1 SEQ FROM ARC
            WHERE EMP_CODIGO=@CodigoFortes
            ORDER BY SEQ DESC
            WITH LOCK
            """, new { CodigoFortes = codigoFortes }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ARC
                (EMP_CODIGO,BTD_TDF_FDF_SEQ,BTD_TDF_SEQ,BTD_SEQ,SEQ,
                 CFO_CODIGO,CSTCOFINS,TIPOCALCULOCOFINS,VALORRECEITACOFINS,TFBASECALCCOFINS,
                 ALIQUOTACOFINSPERC,VALORCOFINS,
                 CSTPIS,TIPOCALCULOPIS,VALORRECEITAPIS,TFBASECALCPIS,
                 ALIQUOTAPISPERC,VALORPIS,EXCLUSAOBCPISCOFINS)
            VALUES
                (@CodigoFortes,@FdfSeq,@TituloSeq,@BaixaSeq,@Seq,
                 @Cfop,'01',1,@ValorReceita,@ValorBase,3,@ValorCofins,
                 '01',1,@ValorReceita,@ValorBase,0.65,@ValorPis,0)
            """, new
        {
            CodigoFortes = codigoFortes,
            FdfSeq = fdfSeq,
            TituloSeq = tituloSeq,
            BaixaSeq = baixaSeq,
            Seq = (ultimaSeq ?? 0) + 1,
            Cfop = cfopNormalizado,
            ValorReceita = valorReceita,
            ValorBase = valorBase,
            ValorCofins = valorCofins,
            ValorPis = valorPis
        }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));
    }

    private static async Task<int> ProximaFaturaAsync(
        FbConnection connection,
        IDbTransaction transaction,
        string codigoFortes,
        CancellationToken cancellationToken)
    {
        var contador = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT SEQ FROM KFDF_SEQ WHERE EMP_CODIGO=@CodigoFortes WITH LOCK
            """, new { CodigoFortes = codigoFortes }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));
        var maior = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(MAX(SEQ),0) FROM FDF WHERE EMP_CODIGO=@CodigoFortes
            """, new { CodigoFortes = codigoFortes }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));
        return Math.Max(contador ?? 0, maior) + 1;
    }

    private static async Task<int> ObterTipoDuplicataAsync(
        FbConnection connection,
        IDbTransaction transaction,
        string codigoFortes,
        CancellationToken cancellationToken)
    {
        var codigo = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT FIRST 1 CODIGO FROM TPF
            WHERE EMP_CODIGO=@CodigoFortes AND UPPER(TRIM(DESCRICAO))='DUPLICATA'
            ORDER BY CODIGO
            """, new { CodigoFortes = codigoFortes }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));
        return codigo ?? throw new InvalidOperationException(
            $"A empresa {codigoFortes} não possui o tipo de fatura DUPLICATA no Fortes.");
    }

    private async Task<FbConnection> AbrirConexaoAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("FortesFolha")
            ?? throw new InvalidOperationException("A conexão com o Fortes não foi configurada.");
        var builder = new FbConnectionStringBuilder(connectionString);
        var senha = configuration["FortesFolha:Password"];
        if (string.IsNullOrWhiteSpace(senha))
            senha = Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        if (string.IsNullOrWhiteSpace(senha))
            throw new InvalidOperationException(
                "A senha do banco do Fortes não foi configurada. Use o segredo FortesFolha:Password ou a variável FORTES_FOLHA_PASSWORD.");
        builder.Password = senha;
        var connection = new FbConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void ValidarCompetencia(int ano, int mes)
    {
        if (mes is < 1 or > 12 || ano is < 2000 or > 2100)
            throw new InvalidOperationException("Competência inválida.");
    }

    private static string ValidarCodigoFortes(string? codigo)
    {
        codigo = codigo?.Trim();
        if (string.IsNullOrWhiteSpace(codigo))
            throw new InvalidOperationException("Informe o código da empresa no Fiscal Fortes no cadastro da empresa.");
        if (codigo.Length > 4)
            throw new InvalidOperationException("O código da empresa no Fiscal Fortes deve possuir no máximo 4 caracteres.");
        return codigo;
    }

    private static IReadOnlyList<string> SepararNotas(string? notas) =>
        string.IsNullOrWhiteSpace(notas)
            ? []
            : notas.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .DistinctBy(NormalizarNumero)
                .ToArray();

    private static string NormalizarNumero(string valor)
    {
        var digitos = SomenteDigitos(valor).TrimStart('0');
        return digitos.Length == 0 ? "0" : digitos;
    }

    private static string? NormalizarCfop(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        var digitos = SomenteDigitos(valor);
        return digitos.Length == 4 ? digitos : null;
    }

    private static string SomenteDigitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private sealed class NotaFortes
    {
        public int Seq { get; set; }
        public string Numero { get; set; } = string.Empty;
        public DateTime DataEmissao { get; set; }
        public decimal Valor { get; set; }
        public int? FdfSeq { get; set; }
        public decimal ValorPisRetido { get; set; }
        public decimal ValorCofinsRetido { get; set; }
        public bool NotaComercio { get; set; }
        public string? Cfop { get; set; }
        public int QuantidadeCfops { get; set; }
    }

    private sealed class FaturaFortes
    {
        public decimal Valor { get; set; }
        public decimal Desconto { get; set; }
    }

    private sealed class TituloFortes
    {
        public int Seq { get; set; }
        public decimal Valor { get; set; }
        public decimal TotalBaixado { get; set; }
    }

    private sealed class BaixaFortes
    {
        public int Seq { get; set; }
        public DateTime? Data { get; set; }
        public decimal ValorPago { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal ValorPisRetido { get; set; }
        public decimal ValorCofinsRetido { get; set; }
        public int TituloSeq { get; set; }
        public int DetalhamentosCompletos { get; set; }
    }

    private sealed record NotaParaDivisao(
        RecebimentoFortesOrigemDto Origem,
        NotaFortes Nota,
        decimal Saldo,
        IReadOnlyList<BaixaFortes> Baixas);

    private sealed record DivisaoRecebimento(
        IReadOnlyList<RecebimentoFortesOrigemDto> Origens,
        RecebimentoFortesPreviaItem? Bloqueio);

    private sealed record MovimentoTitulo(
        int TituloSeq,
        RecebimentoFortesMovimento Movimento);

    private sealed record FinanceiroFortes(
        decimal Valor,
        decimal Desconto,
        IReadOnlyList<TituloFortes> Titulos,
        IReadOnlyList<BaixaFortes> Baixas);
}
