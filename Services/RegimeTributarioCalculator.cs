namespace N3.AnalisadorFiscal.Web.Services;

public enum TipoOperacaoMercadoria
{
    Comercio,
    Industria
}

public enum EnquadramentoServicoSimples
{
    FatorR,
    AnexoIII,
    AnexoIV,
    AnexoV
}

public sealed class CenarioTributario
{
    public decimal ReceitaMercadorias { get; set; }
    public decimal ReceitaServicos { get; set; }
    public decimal ReceitaBruta12Meses { get; set; }
    public decimal Folha12Meses { get; set; }
    public decimal FolhaFatorR12Meses { get; set; }
    public decimal CustoMercadoriasInsumos { get; set; }
    public decimal OutrasDespesasDedutiveis { get; set; }
    public decimal AjustesLucroReal { get; set; }
    public decimal BaseCreditoPisCofins { get; set; }
    public decimal CreditoIcms { get; set; }
    public decimal AliquotaIcmsDebito { get; set; } = 20m;
    public decimal AliquotaIcmsCreditoPotencial { get; set; } = 20m;
    public decimal EntradasIcmsInternas { get; set; }
    public decimal EntradasIcmsNordeste { get; set; }
    public decimal EntradasIcmsSulSudeste { get; set; }
    public decimal EntradasIcmsOutrasInterestaduais { get; set; }
    public decimal EntradasIcmsSemRegra { get; set; }
    public decimal AjustesCreditoIcms { get; set; }
    public decimal AliquotaIss { get; set; } = 5m;
    public decimal AliquotaEncargosPatronais { get; set; } = 28.8m;
    public decimal PercentualReceitaTributadaIcms { get; set; } = 100m;
    public decimal PercentualReceitaTributadaPisCofins { get; set; } = 100m;
    public decimal TributosForaDas { get; set; }
    public TipoOperacaoMercadoria TipoMercadoria { get; set; } = TipoOperacaoMercadoria.Comercio;
    public EnquadramentoServicoSimples EnquadramentoServico { get; set; } = EnquadramentoServicoSimples.FatorR;
    public bool ImpedimentoSimples { get; set; }
    public bool ImpedimentoPresumido { get; set; }
    public bool AplicarTratamentoSeridoComercio { get; set; }
}

public sealed record TributoEstimado(string Nome, decimal Valor, string? Observacao = null);

public sealed class ResultadoRegimeTributario
{
    public required string Regime { get; init; }
    public bool Elegivel { get; init; }
    public required string Situacao { get; init; }
    public required IReadOnlyList<TributoEstimado> Tributos { get; init; }
    public decimal CreditosAproveitados { get; init; }
    public decimal Total => Tributos.Sum(x => x.Valor);
    public decimal AliquotaEfetiva { get; init; }
}

public sealed class ResultadoComparacaoTributaria
{
    public required IReadOnlyList<ResultadoRegimeTributario> Regimes { get; init; }
    public required IReadOnlyList<string> FatosRelevantes { get; init; }
    public decimal ReceitaTotal { get; init; }
    public decimal FatorR { get; init; }
    public decimal LucroRealEstimado { get; init; }
    public decimal SaidasMercadorias { get; init; }
    public decimal EntradasMercadorias { get; init; }
    public decimal DebitoIcmsEstimado { get; init; }
    public decimal CreditoIcmsPotencial { get; init; }
    public decimal CreditoIcmsAproveitavel { get; init; }
    public decimal AliquotaIcmsDebito { get; init; }
    public decimal AliquotaIcmsCreditoPotencial { get; init; }
    public decimal EntradasIcmsInternas { get; init; }
    public decimal EntradasIcmsNordeste { get; init; }
    public decimal EntradasIcmsSulSudeste { get; init; }
    public decimal EntradasIcmsOutrasInterestaduais { get; init; }
    public decimal EntradasIcmsSemRegra { get; init; }
    public decimal AjustesCreditoIcms { get; init; }
    public decimal DifalIcmsSimples { get; init; }
    public decimal PercentualReceitaTributadaIcms { get; init; }
    public decimal PercentualReceitaTributadaPisCofins { get; init; }
    public bool TratamentoSeridoComercioAplicado { get; init; }
    public ResultadoRegimeTributario? MenorCarga => Regimes.Where(x => x.Elegivel).MinBy(x => x.Total);
}

public static class RegimeTributarioCalculator
{
    private const decimal LimiteSimples = 4_800_000m;
    private const decimal LimitePresumido = 78_000_000m;

    private static readonly FaixaSimples[] AnexoI =
    [
        Faixa(180_000m, 4m, 0m, 5.5m, 3.5m, 12.74m, 2.76m, 41.5m, icms: 34m),
        Faixa(360_000m, 7.3m, 5_940m, 5.5m, 3.5m, 12.74m, 2.76m, 41.5m, icms: 34m),
        Faixa(720_000m, 9.5m, 13_860m, 5.5m, 3.5m, 12.74m, 2.76m, 42m, icms: 33.5m),
        Faixa(1_800_000m, 10.7m, 22_500m, 5.5m, 3.5m, 12.74m, 2.76m, 42m, icms: 33.5m),
        Faixa(3_600_000m, 14.3m, 87_300m, 5.5m, 3.5m, 12.74m, 2.76m, 42m, icms: 33.5m),
        Faixa(4_800_000m, 19m, 378_000m, 13.5m, 10m, 28.27m, 6.13m, 42.1m)
    ];

    private static readonly FaixaSimples[] AnexoII =
    [
        Faixa(180_000m, 4.5m, 0m, 5.5m, 3.5m, 11.51m, 2.49m, 37.5m, 7.5m, 32m),
        Faixa(360_000m, 7.8m, 5_940m, 5.5m, 3.5m, 11.51m, 2.49m, 37.5m, 7.5m, 32m),
        Faixa(720_000m, 10m, 13_860m, 5.5m, 3.5m, 11.51m, 2.49m, 37.5m, 7.5m, 32m),
        Faixa(1_800_000m, 11.2m, 22_500m, 5.5m, 3.5m, 11.51m, 2.49m, 37.5m, 7.5m, 32m),
        Faixa(3_600_000m, 14.7m, 85_500m, 5.5m, 3.5m, 11.51m, 2.49m, 37.5m, 7.5m, 32m),
        Faixa(4_800_000m, 30m, 720_000m, 8.5m, 7.5m, 20.96m, 4.54m, 23.5m, 35m)
    ];

    private static readonly FaixaSimples[] AnexoIII =
    [
        Faixa(180_000m, 6m, 0m, 4m, 3.5m, 12.82m, 2.78m, 43.4m, iss: 33.5m),
        Faixa(360_000m, 11.2m, 9_360m, 4m, 3.5m, 14.05m, 3.05m, 43.4m, iss: 32m),
        Faixa(720_000m, 13.5m, 17_640m, 4m, 3.5m, 13.64m, 2.96m, 43.4m, iss: 32.5m),
        Faixa(1_800_000m, 16m, 35_640m, 4m, 3.5m, 13.64m, 2.96m, 43.4m, iss: 32.5m),
        Faixa(3_600_000m, 21m, 125_640m, 4m, 3.5m, 12.82m, 2.78m, 43.4m, iss: 33.5m),
        Faixa(4_800_000m, 33m, 648_000m, 35m, 15m, 16.03m, 3.47m, 30.5m)
    ];

    private static readonly FaixaSimples[] AnexoIV =
    [
        Faixa(180_000m, 4.5m, 0m, 18.8m, 15.2m, 17.67m, 3.83m, iss: 44.5m),
        Faixa(360_000m, 9m, 8_100m, 19.8m, 15.2m, 20.55m, 4.45m, iss: 40m),
        Faixa(720_000m, 10.2m, 12_420m, 20.8m, 15.2m, 19.73m, 4.27m, iss: 40m),
        Faixa(1_800_000m, 14m, 39_780m, 17.8m, 19.2m, 18.9m, 4.1m, iss: 40m),
        Faixa(3_600_000m, 22m, 183_780m, 18.8m, 19.2m, 18.08m, 3.92m, iss: 40m),
        Faixa(4_800_000m, 33m, 828_000m, 53.5m, 21.5m, 20.55m, 4.45m)
    ];

    private static readonly FaixaSimples[] AnexoV =
    [
        Faixa(180_000m, 15.5m, 0m, 25m, 15m, 14.1m, 3.05m, 28.85m, iss: 14m),
        Faixa(360_000m, 18m, 4_500m, 23m, 15m, 14.1m, 3.05m, 27.85m, iss: 17m),
        Faixa(720_000m, 19.5m, 9_900m, 24m, 15m, 14.92m, 3.23m, 23.85m, iss: 19m),
        Faixa(1_800_000m, 20.5m, 17_100m, 21m, 15m, 15.74m, 3.41m, 23.85m, iss: 21m),
        Faixa(3_600_000m, 23m, 62_100m, 23m, 12.5m, 14.1m, 3.05m, 23.85m, iss: 23.5m),
        Faixa(4_800_000m, 30.5m, 540_000m, 35m, 15.5m, 16.44m, 3.56m, 29.5m)
    ];

    public static ResultadoComparacaoTributaria Calcular(CenarioTributario original)
    {
        var c = Normalizar(original);
        var receita = c.ReceitaMercadorias + c.ReceitaServicos;
        var rbt12 = c.ReceitaBruta12Meses > 0 ? c.ReceitaBruta12Meses : receita;
        var folhaFatorR = c.FolhaFatorR12Meses > 0 ? c.FolhaFatorR12Meses : c.Folha12Meses;
        var fatorR = rbt12 > 0 ? folhaFatorR / rbt12 : 0m;
        var encargos = c.Folha12Meses * c.AliquotaEncargosPatronais / 100m;
        var lucroAntesTributos = receita - c.CustoMercadoriasInsumos - c.OutrasDespesasDedutiveis
                                - c.Folha12Meses - encargos + c.AjustesLucroReal;
        var temDetalhamentoIcmsPorUf = c.EntradasIcmsInternas != 0m
                                      || c.EntradasIcmsNordeste != 0m
                                      || c.EntradasIcmsSulSudeste != 0m
                                      || c.EntradasIcmsOutrasInterestaduais != 0m
                                      || c.EntradasIcmsSemRegra != 0m;
        var creditoIcmsPotencial = temDetalhamentoIcmsPorUf
            ? c.EntradasIcmsInternas * 20m / 100m
              + c.EntradasIcmsNordeste * 12m / 100m
              + c.EntradasIcmsSulSudeste * 7m / 100m
              + c.EntradasIcmsOutrasInterestaduais * 12m / 100m
              + c.AjustesCreditoIcms
            : c.CustoMercadoriasInsumos * c.AliquotaIcmsCreditoPotencial / 100m;
        var difalIcmsSimples = CalcularDifalIcmsSimples(c);

        var debitoIcmsEstimado = CalcularDebitoIcms(c);
        var regimes = new[]
        {
            CalcularSimples(c, receita, rbt12, fatorR, encargos, difalIcmsSimples),
            CalcularPresumido(c, receita, encargos),
            CalcularReal(c, receita, encargos, lucroAntesTributos)
        };

        return new ResultadoComparacaoTributaria
        {
            ReceitaTotal = receita,
            FatorR = fatorR,
            LucroRealEstimado = lucroAntesTributos,
            SaidasMercadorias = c.ReceitaMercadorias,
            EntradasMercadorias = c.CustoMercadoriasInsumos,
            DebitoIcmsEstimado = debitoIcmsEstimado,
            CreditoIcmsPotencial = Math.Max(0m, creditoIcmsPotencial),
            CreditoIcmsAproveitavel = Math.Min(
                debitoIcmsEstimado,
                c.CreditoIcms),
            AliquotaIcmsDebito = c.AliquotaIcmsDebito,
            AliquotaIcmsCreditoPotencial = c.AliquotaIcmsCreditoPotencial,
            EntradasIcmsInternas = c.EntradasIcmsInternas,
            EntradasIcmsNordeste = c.EntradasIcmsNordeste,
            EntradasIcmsSulSudeste = c.EntradasIcmsSulSudeste,
            EntradasIcmsOutrasInterestaduais = c.EntradasIcmsOutrasInterestaduais,
            EntradasIcmsSemRegra = c.EntradasIcmsSemRegra,
            AjustesCreditoIcms = c.AjustesCreditoIcms,
            DifalIcmsSimples = difalIcmsSimples,
            PercentualReceitaTributadaIcms = c.PercentualReceitaTributadaIcms,
            PercentualReceitaTributadaPisCofins = c.PercentualReceitaTributadaPisCofins,
            TratamentoSeridoComercioAplicado = c.AplicarTratamentoSeridoComercio,
            Regimes = regimes,
            FatosRelevantes = MontarFatos(c, receita, rbt12, fatorR, lucroAntesTributos, regimes)
        };
    }

    private static ResultadoRegimeTributario CalcularSimples(
        CenarioTributario c, decimal receita, decimal rbt12, decimal fatorR, decimal encargos,
        decimal difalIcmsEntradas)
    {
        var anexoMercadoria = c.TipoMercadoria == TipoOperacaoMercadoria.Industria ? AnexoII : AnexoI;
        var nomeMercadoria = c.TipoMercadoria == TipoOperacaoMercadoria.Industria ? "Anexo II" : "Anexo I";
        var (anexoServico, nomeServico) = ObterAnexoServico(c.EnquadramentoServico, fatorR);
        var faixaMercadoria = ObterFaixaSimples(rbt12, anexoMercadoria);
        var faixaServico = ObterFaixaSimples(rbt12, anexoServico);
        var aliquotaMercadoria = AliquotaEfetivaSimples(rbt12, faixaMercadoria);
        var aliquotaServico = AliquotaEfetivaSimples(rbt12, faixaServico);
        var cppForaDas = c.ReceitaServicos > 0 && c.EnquadramentoServico == EnquadramentoServicoSimples.AnexoIV;
        var dasMercadorias = c.ReceitaMercadorias * aliquotaMercadoria;
        var dasServicos = c.ReceitaServicos * aliquotaServico;
        var partilha = RatearDas(c.ReceitaMercadorias, dasMercadorias, faixaMercadoria)
            + RatearDas(c.ReceitaServicos, dasServicos, faixaServico);
        if (c.AplicarTratamentoSeridoComercio)
            partilha = partilha with { Icms = partilha.Icms * c.PercentualReceitaTributadaIcms / 100m };
        var observacaoDas = $"Partilha estimada do DAS: mercadorias em {nomeMercadoria} ({aliquotaMercadoria:P2})"
                            + (c.ReceitaServicos > 0 ? $" e serviços em {nomeServico} ({aliquotaServico:P2})" : string.Empty);
        var observacaoIcms = c.AplicarTratamentoSeridoComercio
            ? $"Parcela do DAS ajustada: {c.PercentualReceitaTributadaIcms:N2}% da receita de mercadorias tratada como tributada por ICMS"
            : null;

        var tributos = new List<TributoEstimado>
        {
            new("IRPJ", partilha.Irpj, observacaoDas),
            new("CSLL", partilha.Csll),
            new("PIS/Pasep", partilha.Pis),
            new("Cofins", partilha.Cofins),
            new("CPP / encargos patronais", partilha.Cpp + (cppForaDas ? encargos : 0m),
                cppForaDas ? "CPP do Anexo IV calculada fora do DAS" : "CPP repartida dentro do DAS"),
            new("ICMS", partilha.Icms, observacaoIcms),
            new("ICMS - DIFAL das entradas", difalIcmsEntradas,
                $"Diferença entre a alíquota interna de {c.AliquotaIcmsDebito:N2}% e a alíquota interestadual pela UF do participante"),
            new("IPI", partilha.Ipi),
            new("ISS", partilha.Iss),
            new("Tributos fora do DAS", c.TributosForaDas, "Ex.: ICMS-ST, antecipações e tributos retidos")
        };

        var elegivel = !c.ImpedimentoSimples && rbt12 <= LimiteSimples;
        var situacao = c.ImpedimentoSimples
            ? "Impedimento informado no cenário"
            : rbt12 > LimiteSimples
                ? "Receita acima do limite de R$ 4,8 milhões"
                : $"Estimativa por {nomeMercadoria} e {nomeServico}";

        return CriarResultado("Simples Nacional", elegivel, situacao, tributos, receita, 0m);
    }

    private static ResultadoRegimeTributario CalcularPresumido(CenarioTributario c, decimal receita, decimal encargos)
    {
        var receitaPisCofins = receita * c.PercentualReceitaTributadaPisCofins / 100m;
        var baseIrpj = c.ReceitaMercadorias * 8m / 100m + c.ReceitaServicos * 32m / 100m;
        var baseCsll = c.ReceitaMercadorias * 12m / 100m + c.ReceitaServicos * 32m / 100m;
        var debitoIcms = CalcularDebitoIcms(c);
        var tributos = new List<TributoEstimado>
        {
            new("IRPJ", baseIrpj * 15m / 100m),
            new("Adicional de IRPJ", Math.Max(0m, baseIrpj - 240_000m) * 10m / 100m,
                "Projeção anual uniforme; o cálculo efetivo observa o período de apuração"),
            new("CSLL", baseCsll * 9m / 100m),
            new("PIS cumulativo", receitaPisCofins * 0.65m / 100m),
            new("Cofins cumulativa", receitaPisCofins * 3m / 100m),
            new("ICMS líquido", Math.Max(0m, debitoIcms - c.CreditoIcms),
                $"Débito {Moeda(debitoIcms)} − crédito {Moeda(Math.Min(debitoIcms, c.CreditoIcms))}"),
            new("ISS", c.ReceitaServicos * c.AliquotaIss / 100m),
            new("Encargos patronais", encargos)
        };

        var elegivel = !c.ImpedimentoPresumido && receita <= LimitePresumido;
        var situacao = c.ImpedimentoPresumido
            ? "Impedimento informado no cenário"
            : receita > LimitePresumido
                ? "Receita acima do limite de R$ 78 milhões"
                : "Bases presumidas de 8%/12% para mercadorias e 32% para serviços";

        return CriarResultado("Lucro Presumido", elegivel, situacao, tributos, receita,
            Math.Min(debitoIcms, c.CreditoIcms));
    }

    private static ResultadoRegimeTributario CalcularReal(
        CenarioTributario c, decimal receita, decimal encargos, decimal lucroAntesTributos)
    {
        var baseLucro = Math.Max(0m, lucroAntesTributos);
        var receitaPisCofins = receita * c.PercentualReceitaTributadaPisCofins / 100m;
        var debitoPis = receitaPisCofins * 1.65m / 100m;
        var debitoCofins = receitaPisCofins * 7.6m / 100m;
        var creditoPis = Math.Min(debitoPis, c.BaseCreditoPisCofins * 1.65m / 100m);
        var creditoCofins = Math.Min(debitoCofins, c.BaseCreditoPisCofins * 7.6m / 100m);
        var debitoIcms = CalcularDebitoIcms(c);
        var creditoIcms = Math.Min(debitoIcms, c.CreditoIcms);

        var tributos = new List<TributoEstimado>
        {
            new("IRPJ", baseLucro * 15m / 100m),
            new("Adicional de IRPJ", Math.Max(0m, baseLucro - 240_000m) * 10m / 100m,
                "Projeção anual uniforme; o cálculo efetivo observa o período de apuração"),
            new("CSLL", baseLucro * 9m / 100m),
            new("PIS não cumulativo", Math.Max(0m, debitoPis - creditoPis),
                $"Débito {Moeda(debitoPis)} − crédito {Moeda(creditoPis)}"),
            new("Cofins não cumulativa", Math.Max(0m, debitoCofins - creditoCofins),
                $"Débito {Moeda(debitoCofins)} − crédito {Moeda(creditoCofins)}"),
            new("ICMS líquido", Math.Max(0m, debitoIcms - creditoIcms),
                $"Débito {Moeda(debitoIcms)} − crédito {Moeda(creditoIcms)}"),
            new("ISS", c.ReceitaServicos * c.AliquotaIss / 100m),
            new("Encargos patronais", encargos)
        };

        var situacao = lucroAntesTributos <= 0
            ? "Sem IRPJ/CSLL na projeção por ausência de lucro tributável"
            : $"Lucro tributável estimado em {Moeda(lucroAntesTributos)}";

        return CriarResultado("Lucro Real", true, situacao, tributos, receita,
            creditoPis + creditoCofins + creditoIcms);
    }

    private static ResultadoRegimeTributario CriarResultado(
        string regime, bool elegivel, string situacao, IReadOnlyList<TributoEstimado> tributos,
        decimal receita, decimal creditos)
    {
        var total = tributos.Sum(x => x.Valor);
        return new ResultadoRegimeTributario
        {
            Regime = regime,
            Elegivel = elegivel,
            Situacao = situacao,
            Tributos = tributos,
            CreditosAproveitados = creditos,
            AliquotaEfetiva = receita > 0 ? total / receita : 0m
        };
    }

    private static IReadOnlyList<string> MontarFatos(
        CenarioTributario c, decimal receita, decimal rbt12, decimal fatorR, decimal lucro,
        IReadOnlyList<ResultadoRegimeTributario> regimes)
    {
        var fatos = new List<string>();
        if (c.ReceitaServicos > 0 && c.EnquadramentoServico == EnquadramentoServicoSimples.FatorR)
            fatos.Add(fatorR >= 0.28m
                ? $"Fator R de {fatorR:P2}: os serviços informados foram simulados no Anexo III."
                : $"Fator R de {fatorR:P2}: os serviços informados foram simulados no Anexo V; atingir 28% pode alterar o resultado.");

        fatos.Add(rbt12 <= LimiteSimples
            ? $"RBT12 de {Moeda(rbt12)} está dentro do limite geral do Simples Nacional."
            : $"RBT12 de {Moeda(rbt12)} supera o limite geral do Simples Nacional.");

        if (receita > 0)
        {
            fatos.Add($"Margem contábil estimada antes de IRPJ/CSLL: {lucro / receita:P2} ({Moeda(lucro)})." );
            fatos.Add($"Compras/insumos representam {c.CustoMercadoriasInsumos / receita:P2} da receita; a base informada para créditos de PIS/Cofins é {Moeda(c.BaseCreditoPisCofins)}.");
        }

        var real = regimes.First(x => x.Regime == "Lucro Real");
        fatos.Add($"Créditos aproveitados na projeção do Lucro Real: {Moeda(real.CreditosAproveitados)} entre PIS, Cofins e ICMS.");

        if (c.TributosForaDas > 0)
            fatos.Add($"Foram adicionados {Moeda(c.TributosForaDas)} ao Simples por tributos não abrangidos pelo DAS.");
        var difalIcmsSimples = CalcularDifalIcmsSimples(c);
        if (difalIcmsSimples > 0m)
            fatos.Add($"No Simples, o DIFAL estimado das entradas interestaduais acrescenta {Moeda(difalIcmsSimples)} fora do DAS.");
        if (rbt12 > 3_600_000m && rbt12 <= LimiteSimples)
            fatos.Add("A receita supera R$ 3,6 milhões; sublimites de ICMS/ISS precisam ser avaliados separadamente.");
        if (c.PercentualReceitaTributadaPisCofins < 100m)
            fatos.Add(c.AplicarTratamentoSeridoComercio
                ? $"Seridó Comércio: apenas {c.PercentualReceitaTributadaPisCofins:N2}% da receita foi tratada como tributada por PIS/Cofins no Lucro Presumido e no Lucro Real; no Simples Nacional, PIS/Cofins permanecem integralmente na partilha do DAS."
                : $"Apenas {c.PercentualReceitaTributadaPisCofins:N2}% da receita foi tratada como tributada por PIS/Cofins.");
        if (c.AplicarTratamentoSeridoComercio)
            fatos.Add($"Seridó Comércio: {c.PercentualReceitaTributadaIcms:N2}% da receita de mercadorias foi tratada como tributada por ICMS; a parcela imune foi excluída também da partilha de ICMS do DAS.");

        return fatos;
    }

    private static (FaixaSimples[] Tabela, string Nome) ObterAnexoServico(
        EnquadramentoServicoSimples enquadramento, decimal fatorR) => enquadramento switch
    {
        EnquadramentoServicoSimples.AnexoIII => (AnexoIII, "Anexo III"),
        EnquadramentoServicoSimples.AnexoIV => (AnexoIV, "Anexo IV"),
        EnquadramentoServicoSimples.AnexoV => (AnexoV, "Anexo V"),
        _ when fatorR >= 0.28m => (AnexoIII, "Anexo III pelo fator R"),
        _ => (AnexoV, "Anexo V pelo fator R")
    };

    private static FaixaSimples ObterFaixaSimples(decimal rbt12, FaixaSimples[] tabela)
        => tabela.FirstOrDefault(x => rbt12 <= x.Limite) ?? tabela[^1];

    private static decimal AliquotaEfetivaSimples(decimal rbt12, FaixaSimples faixa)
    {
        if (rbt12 <= 0) return faixa.AliquotaNominal / 100m;
        return Math.Max(0m, (rbt12 * faixa.AliquotaNominal / 100m - faixa.ParcelaDeduzir) / rbt12);
    }

    private static ReparticaoValores RatearDas(decimal receita, decimal valorDas, FaixaSimples faixa)
    {
        var p = faixa.Reparticao;
        var valores = new ReparticaoValores(
            valorDas * p.Irpj / 100m,
            valorDas * p.Csll / 100m,
            valorDas * p.Pis / 100m,
            valorDas * p.Cofins / 100m,
            valorDas * p.Cpp / 100m,
            valorDas * p.Icms / 100m,
            valorDas * p.Ipi / 100m,
            valorDas * p.Iss / 100m);

        var limiteIss = receita * 5m / 100m;
        if (valores.Iss <= limiteIss || valores.Iss <= 0m) return valores;

        var excedente = valores.Iss - limiteIss;
        var federais = valores.Irpj + valores.Csll + valores.Pis + valores.Cofins + valores.Cpp + valores.Ipi;
        if (federais <= 0m) return valores with { Iss = limiteIss };

        return valores with
        {
            Irpj = valores.Irpj + excedente * valores.Irpj / federais,
            Csll = valores.Csll + excedente * valores.Csll / federais,
            Pis = valores.Pis + excedente * valores.Pis / federais,
            Cofins = valores.Cofins + excedente * valores.Cofins / federais,
            Cpp = valores.Cpp + excedente * valores.Cpp / federais,
            Ipi = valores.Ipi + excedente * valores.Ipi / federais,
            Iss = limiteIss
        };
    }

    private static FaixaSimples Faixa(
        decimal limite, decimal aliquota, decimal deducao,
        decimal irpj, decimal csll, decimal cofins, decimal pis,
        decimal cpp = 0m, decimal ipi = 0m, decimal icms = 0m, decimal iss = 0m)
        => new(limite, aliquota, deducao, new(irpj, csll, pis, cofins, cpp, icms, ipi, iss));

    private static CenarioTributario Normalizar(CenarioTributario c) => new()
    {
        ReceitaMercadorias = NaoNegativo(c.ReceitaMercadorias),
        ReceitaServicos = NaoNegativo(c.ReceitaServicos),
        ReceitaBruta12Meses = NaoNegativo(c.ReceitaBruta12Meses),
        Folha12Meses = NaoNegativo(c.Folha12Meses),
        FolhaFatorR12Meses = NaoNegativo(c.FolhaFatorR12Meses),
        CustoMercadoriasInsumos = NaoNegativo(c.CustoMercadoriasInsumos),
        OutrasDespesasDedutiveis = NaoNegativo(c.OutrasDespesasDedutiveis),
        AjustesLucroReal = c.AjustesLucroReal,
        BaseCreditoPisCofins = NaoNegativo(c.BaseCreditoPisCofins),
        CreditoIcms = NaoNegativo(c.CreditoIcms),
        AliquotaIcmsDebito = Percentual(c.AliquotaIcmsDebito),
        AliquotaIcmsCreditoPotencial = Percentual(c.AliquotaIcmsCreditoPotencial),
        EntradasIcmsInternas = NaoNegativo(c.EntradasIcmsInternas),
        EntradasIcmsNordeste = NaoNegativo(c.EntradasIcmsNordeste),
        EntradasIcmsSulSudeste = NaoNegativo(c.EntradasIcmsSulSudeste),
        EntradasIcmsOutrasInterestaduais = NaoNegativo(c.EntradasIcmsOutrasInterestaduais),
        EntradasIcmsSemRegra = NaoNegativo(c.EntradasIcmsSemRegra),
        AjustesCreditoIcms = c.AjustesCreditoIcms,
        AliquotaIss = Percentual(c.AliquotaIss),
        AliquotaEncargosPatronais = Percentual(c.AliquotaEncargosPatronais),
        PercentualReceitaTributadaIcms = Percentual(c.PercentualReceitaTributadaIcms),
        PercentualReceitaTributadaPisCofins = Percentual(c.PercentualReceitaTributadaPisCofins),
        TributosForaDas = NaoNegativo(c.TributosForaDas),
        TipoMercadoria = c.TipoMercadoria,
        EnquadramentoServico = c.EnquadramentoServico,
        ImpedimentoSimples = c.ImpedimentoSimples,
        ImpedimentoPresumido = c.ImpedimentoPresumido,
        AplicarTratamentoSeridoComercio = c.AplicarTratamentoSeridoComercio
    };

    private static decimal NaoNegativo(decimal valor) => Math.Max(0m, valor);
    private static decimal Percentual(decimal valor) => Math.Clamp(valor, 0m, 100m);
    private static decimal CalcularDifalIcmsSimples(CenarioTributario c)
        => c.EntradasIcmsNordeste * Math.Max(0m, c.AliquotaIcmsDebito - 12m) / 100m
           + c.EntradasIcmsSulSudeste * Math.Max(0m, c.AliquotaIcmsDebito - 7m) / 100m
           + c.EntradasIcmsOutrasInterestaduais * Math.Max(0m, c.AliquotaIcmsDebito - 12m) / 100m;
    private static decimal CalcularDebitoIcms(CenarioTributario c)
        => c.ReceitaMercadorias * c.AliquotaIcmsDebito / 100m
           * c.PercentualReceitaTributadaIcms / 100m;
    private static string Moeda(decimal valor) => valor.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    private sealed record FaixaSimples(
        decimal Limite,
        decimal AliquotaNominal,
        decimal ParcelaDeduzir,
        ReparticaoSimples Reparticao);

    private sealed record ReparticaoSimples(
        decimal Irpj,
        decimal Csll,
        decimal Pis,
        decimal Cofins,
        decimal Cpp,
        decimal Icms,
        decimal Ipi,
        decimal Iss);

    private readonly record struct ReparticaoValores(
        decimal Irpj,
        decimal Csll,
        decimal Pis,
        decimal Cofins,
        decimal Cpp,
        decimal Icms,
        decimal Ipi,
        decimal Iss)
    {
        public static ReparticaoValores operator +(ReparticaoValores esquerda, ReparticaoValores direita)
            => new(
                esquerda.Irpj + direita.Irpj,
                esquerda.Csll + direita.Csll,
                esquerda.Pis + direita.Pis,
                esquerda.Cofins + direita.Cofins,
                esquerda.Cpp + direita.Cpp,
                esquerda.Icms + direita.Icms,
                esquerda.Ipi + direita.Ipi,
                esquerda.Iss + direita.Iss);
    }
}
