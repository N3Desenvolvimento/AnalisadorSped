using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace N3.AnalisadorFiscal.Web.Services;

public sealed record ComparacaoRegimesRelatorio(
    string NomeEmpresa,
    string? Cnpj,
    int AnoBase,
    CenarioTributario Cenario,
    ResultadoComparacaoTributaria Resultado);

public interface IRegimeTributarioPdfService
{
    byte[] Gerar(ComparacaoRegimesRelatorio relatorio);
}

public sealed class RegimeTributarioPdfService : IRegimeTributarioPdfService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly XColor Azul = XColor.FromArgb(21, 93, 168);
    private static readonly XColor AzulEscuro = XColor.FromArgb(15, 61, 104);
    private static readonly XColor Verde = XColor.FromArgb(22, 163, 74);
    private static readonly XColor CinzaTexto = XColor.FromArgb(71, 85, 105);
    private static readonly XColor CinzaLinha = XColor.FromArgb(226, 232, 240);
    private static readonly XColor FundoClaro = XColor.FromArgb(248, 250, 252);

    public byte[] Gerar(ComparacaoRegimesRelatorio relatorio)
    {
        if (OperatingSystem.IsWindows())
            PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        using var documento = new PdfDocument();
        documento.Info.Title = $"Comparação de regimes tributários - {relatorio.NomeEmpresa}";
        documento.Info.Subject = $"Simulação anual para {relatorio.AnoBase}";
        documento.Info.Author = "N3 Analisador Fiscal";

        var pdf = new DocumentoComparacao(documento, relatorio);
        pdf.AdicionarResumoExecutivo();
        pdf.AdicionarComparacaoDetalhada();
        pdf.AdicionarPremissasEMetodologia();
        pdf.Finalizar();

        using var memoria = new MemoryStream();
        documento.Save(memoria, false);
        return memoria.ToArray();
    }

    private sealed class DocumentoComparacao
    {
        private const double Margem = 40;
        private const double Rodape = 34;
        private readonly PdfDocument _documento;
        private readonly ComparacaoRegimesRelatorio _relatorio;
        private readonly XFont _fonte7 = new("Arial", 7);
        private readonly XFont _fonte8 = new("Arial", 8);
        private readonly XFont _fonte9 = new("Arial", 9);
        private readonly XFont _fonte9b = new("Arial", 9, XFontStyleEx.Bold);
        private readonly XFont _fonte11b = new("Arial", 11, XFontStyleEx.Bold);
        private readonly XFont _fonte15b = new("Arial", 15, XFontStyleEx.Bold);
        private readonly XFont _fonte22b = new("Arial", 22, XFontStyleEx.Bold);
        private PdfPage _pagina = null!;
        private XGraphics _gfx = null!;
        private double _y;
        private double Largura => _pagina.Width.Point - Margem * 2;
        private double LimiteY => _pagina.Height.Point - Rodape - 12;

        public DocumentoComparacao(PdfDocument documento, ComparacaoRegimesRelatorio relatorio)
        {
            _documento = documento;
            _relatorio = relatorio;
        }

        public void AdicionarResumoExecutivo()
        {
            NovaPagina("RELATÓRIO GERENCIAL", mostrarEmpresa: false);
            _gfx.DrawString("Comparação de regimes", _fonte22b, new XSolidBrush(AzulEscuro), Margem, _y + 22);
            _y += 34;
            _gfx.DrawString("tributários", _fonte22b, new XSolidBrush(AzulEscuro), Margem, _y + 22);
            _y += 38;

            Texto(_relatorio.NomeEmpresa, _fonte15b, Azul, 22);
            Texto($"Ano-base {_relatorio.AnoBase}  |  CNPJ {FormatarCnpj(_relatorio.Cnpj)}", _fonte9, CinzaTexto, 18);

            if (_relatorio.Resultado.TratamentoSeridoComercioAplicado)
                CaixaDestaque(
                    "TRATAMENTO ESPECÍFICO - SERIDÓ COMÉRCIO",
                    $"ICMS calculado sobre {_relatorio.Resultado.PercentualReceitaTributadaIcms:N2}% da receita de mercadorias. " +
                    $"PIS/Cofins calculados sobre {_relatorio.Resultado.PercentualReceitaTributadaPisCofins:N2}% da receita somente no Lucro Presumido e no Lucro Real; no Simples, permanecem na partilha do DAS.",
                    XColor.FromArgb(239, 246, 255), AzulEscuro);

            var melhor = _relatorio.Resultado.MenorCarga;
            var cards = new[]
            {
                ("RECEITA ANUAL", Moeda(_relatorio.Resultado.ReceitaTotal), CinzaTexto),
                ("MENOR CARGA ELEGÍVEL", melhor?.Regime ?? "Sem regime elegível", Verde),
                ("TOTAL ESTIMADO", melhor is null ? "-" : Moeda(melhor.Total), Verde),
                ("ALÍQUOTA EFETIVA", melhor is null ? "-" : Percentual(melhor.AliquotaEfetiva), Verde)
            };
            DesenharCards(cards);

            TituloSecao("Carga tributária anual por regime");
            GraficoBarras(
                _relatorio.Resultado.Regimes.Select(x => (x.Regime, x.Total, x == melhor, x.Elegivel)).ToArray(),
                valor => Moeda(valor));

            TituloSecao("Alíquota efetiva estimada");
            GraficoBarras(
                _relatorio.Resultado.Regimes.Select(x => (x.Regime, x.AliquotaEfetiva * 100m, x == melhor, x.Elegivel)).ToArray(),
                valor => $"{valor:N2}%");

            TituloSecao("Leitura executiva");
            if (melhor is not null)
            {
                TextoQuebrado(
                    $"No cenário informado, {melhor.Regime} apresenta a menor carga entre os regimes elegíveis, " +
                    $"com total anual estimado de {Moeda(melhor.Total)} e alíquota efetiva de {Percentual(melhor.AliquotaEfetiva)}. " +
                    "A conclusão depende das premissas exibidas neste relatório e deve ser validada antes da opção tributária.",
                    _fonte9, CinzaTexto, Largura, 13);
            }
        }

        public void AdicionarComparacaoDetalhada()
        {
            NovaPagina("COMPARAÇÃO DETALHADA");
            Texto("Composição da carga por tributo", _fonte15b, AzulEscuro, 26);
            TextoQuebrado("Valores anuais lado a lado. PIS, Cofins e ICMS são apresentados líquidos dos créditos considerados.", _fonte9, CinzaTexto, Largura, 13);
            _y += 8;

            var colTributo = 150d;
            var colRegime = (Largura - colTributo) / 3d;
            var x = Margem;
            TabelaCelula("TRIBUTO", x, _y, colTributo, 30, _fonte8, true, false);
            x += colTributo;
            foreach (var regime in _relatorio.Resultado.Regimes)
            {
                TabelaCelula(regime.Regime.ToUpperInvariant(), x, _y, colRegime, 30, _fonte8, true, true);
                x += colRegime;
            }
            _y += 30;

            foreach (var categoria in Categorias)
            {
                GarantirEspaco(25);
                x = Margem;
                TabelaCelula(categoria.Nome, x, _y, colTributo, 25, _fonte8, false, false);
                x += colTributo;
                foreach (var regime in _relatorio.Resultado.Regimes)
                {
                    var valor = ValorTributo(regime, categoria.Codigo);
                    TabelaCelula(Moeda(valor), x, _y, colRegime, 25, _fonte8, false, true);
                    x += colRegime;
                }
                _y += 25;
            }

            x = Margem;
            TabelaCelula("TOTAL ANUAL", x, _y, colTributo, 31, _fonte9b, true, false, XColor.FromArgb(239, 246, 255));
            x += colTributo;
            foreach (var regime in _relatorio.Resultado.Regimes)
            {
                TabelaCelula(Moeda(regime.Total), x, _y, colRegime, 31, _fonte9b, true, true, XColor.FromArgb(239, 246, 255));
                x += colRegime;
            }
            _y += 43;

            TituloSecao("Créditos e diferenças");
            foreach (var regime in _relatorio.Resultado.Regimes)
            {
                GarantirEspaco(36);
                _gfx.DrawString(regime.Regime, _fonte9b, XBrushes.Black, Margem, _y + 10);
                _gfx.DrawString($"Créditos: {Moeda(regime.CreditosAproveitados)}", _fonte8, new XSolidBrush(CinzaTexto), Margem + 150, _y + 10);
                var diferenca = _relatorio.Resultado.MenorCarga is { } menor && regime.Elegivel
                    ? regime.Total - menor.Total
                    : 0m;
                var textoDiferenca = !regime.Elegivel ? "Não elegível no cenário"
                    : diferenca <= 0 ? "Referência de menor carga"
                    : $"{Moeda(diferenca)} acima do menor cenário";
                _gfx.DrawString(textoDiferenca, _fonte8, new XSolidBrush(regime.Elegivel ? CinzaTexto : XColors.Firebrick),
                    new XRect(Margem + 315, _y, Largura - 315, 16), XStringFormats.CenterRight);
                _y += 18;
                TextoQuebrado(regime.Situacao, _fonte8, CinzaTexto, Largura, 11);
                _gfx.DrawLine(new XPen(CinzaLinha, .7), Margem, _y + 3, Margem + Largura, _y + 3);
                _y += 10;
            }

            TituloSecao("Fatos que mais influenciam a decisão");
            for (var i = 0; i < _relatorio.Resultado.FatosRelevantes.Count; i++)
            {
                GarantirEspaco(28);
                _gfx.DrawEllipse(new XSolidBrush(Azul), Margem, _y + 1, 16, 16);
                _gfx.DrawString((i + 1).ToString(PtBr), _fonte8, XBrushes.White,
                    new XRect(Margem, _y + 1, 16, 16), XStringFormats.Center);
                TextoQuebrado(_relatorio.Resultado.FatosRelevantes[i], _fonte8, CinzaTexto, Largura - 25, 11, Margem + 25);
                _y += 5;
            }
        }

        public void AdicionarPremissasEMetodologia()
        {
            NovaPagina("PREMISSAS E METODOLOGIA");
            Texto("Dados do cenário", _fonte15b, AzulEscuro, 28);
            var c = _relatorio.Cenario;
            var premissas = new (string Nome, string Valor)[]
            {
                ("Receita de mercadorias", Moeda(c.ReceitaMercadorias)),
                ("Receita de serviços", Moeda(c.ReceitaServicos)),
                ("RBT12", Moeda(c.ReceitaBruta12Meses > 0 ? c.ReceitaBruta12Meses : _relatorio.Resultado.ReceitaTotal)),
                ("Folha bruta + pró-labore", Moeda(c.Folha12Meses)),
                ("Folha para o fator R", Moeda(c.FolhaFatorR12Meses)),
                ("Compras, mercadorias e insumos", Moeda(c.CustoMercadoriasInsumos)),
                ("Outras despesas dedutíveis", Moeda(c.OutrasDespesasDedutiveis)),
                ("Base de crédito de PIS/Cofins", Moeda(c.BaseCreditoPisCofins)),
                ("Crédito de ICMS considerado", Moeda(c.CreditoIcms)),
                ("Receita tributada por ICMS", $"{c.PercentualReceitaTributadaIcms:N2}%"),
                ("Receita tributada por PIS/Cofins", $"{c.PercentualReceitaTributadaPisCofins:N2}%"),
                ("Alíquota nominal de ICMS nas saídas", $"{c.AliquotaIcmsDebito:N2}%"),
                ("Alíquota de ISS", $"{c.AliquotaIss:N2}%"),
                ("Encargos patronais", $"{c.AliquotaEncargosPatronais:N2}%"),
                ("Resultado antes de IRPJ/CSLL", Moeda(_relatorio.Resultado.LucroRealEstimado)),
                ("Fator R", Percentual(_relatorio.Resultado.FatorR))
            };
            TabelaDuasColunas(premissas);

            TituloSecao("Critérios aplicados");
            var criterios = new List<string>
            {
                "Simples Nacional: alíquota efetiva calculada pela faixa do anexo selecionado e pela RBT12, com a partilha estimada entre os tributos do DAS.",
                "Lucro Presumido: bases de 8%/12% para mercadorias e 32% para serviços, além de PIS/Cofins cumulativos, ICMS líquido, ISS e encargos.",
                "Lucro Real: IRPJ e CSLL sobre o lucro estimado, PIS/Cofins não cumulativos líquidos dos créditos informados, ICMS líquido, ISS e encargos.",
                "No Simples, o DIFAL das entradas interestaduais é somado fora do DAS. Nos demais regimes, o ICMS corresponde ao débito tributável menos o crédito aproveitável."
            };
            if (_relatorio.Resultado.TratamentoSeridoComercioAplicado)
                criterios.Add("Exclusivamente para a Seridó Comércio, a imunidade de ICMS reduz a parcela de ICMS em todos os regimes. A desoneração de PIS/Cofins é aplicada somente no Lucro Presumido e no Lucro Real, nunca no Simples Nacional.");
            foreach (var criterio in criterios)
            {
                GarantirEspaco(26);
                _gfx.DrawRectangle(new XSolidBrush(Azul), Margem, _y + 4, 5, 5);
                TextoQuebrado(criterio, _fonte8, CinzaTexto, Largura - 16, 11, Margem + 16);
                _y += 4;
            }

            GarantirEspaco(105);
            TituloSecao("Aviso profissional");
            CaixaDestaque(
                "SIMULAÇÃO GERENCIAL",
                "Este relatório é uma estimativa para apoio à decisão. Não substitui a validação fiscal, contábil, societária e legal. " +
                "Antes da opção pelo regime, confirme NCMs, documentos de suporte às imunidades, segregações no PGDAS, benefícios estaduais, retenções, sublimites e demais particularidades do período.",
                XColor.FromArgb(255, 247, 237), XColor.FromArgb(154, 52, 18));
        }

        public void Finalizar()
        {
            _gfx.Dispose();
            for (var i = 0; i < _documento.PageCount; i++)
            {
                var pagina = _documento.Pages[i];
                using var gfx = XGraphics.FromPdfPage(pagina, XGraphicsPdfPageOptions.Append);
                gfx.DrawLine(new XPen(CinzaLinha, .7), Margem, pagina.Height.Point - Rodape, pagina.Width.Point - Margem, pagina.Height.Point - Rodape);
                gfx.DrawString($"N3 Analisador Fiscal  |  Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}", _fonte7,
                    new XSolidBrush(CinzaTexto), Margem, pagina.Height.Point - 20);
                gfx.DrawString($"Página {i + 1} de {_documento.PageCount}", _fonte7, new XSolidBrush(CinzaTexto),
                    new XRect(Margem, pagina.Height.Point - 28, pagina.Width.Point - Margem * 2, 14), XStringFormats.CenterRight);
            }
        }

        private void NovaPagina(string secao, bool mostrarEmpresa = true)
        {
            if (_pagina is not null) _gfx.Dispose();
            _pagina = _documento.AddPage();
            _pagina.Size = PdfSharp.PageSize.A4;
            _gfx = XGraphics.FromPdfPage(_pagina);
            _gfx.DrawRectangle(new XSolidBrush(AzulEscuro), 0, 0, _pagina.Width.Point, 25);
            _gfx.DrawString(secao, _fonte8, XBrushes.White, Margem, 17);
            if (mostrarEmpresa)
                _gfx.DrawString(_relatorio.NomeEmpresa, _fonte8, XBrushes.White,
                    new XRect(Margem, 5, Largura, 15), XStringFormats.CenterRight);
            _y = 46;
        }

        private void GarantirEspaco(double altura)
        {
            if (_y + altura <= LimiteY) return;
            NovaPagina("CONTINUAÇÃO");
        }

        private void TituloSecao(string texto)
        {
            GarantirEspaco(35);
            _y += 12;
            _gfx.DrawRectangle(new XSolidBrush(Azul), Margem, _y, 4, 17);
            _gfx.DrawString(texto, _fonte11b, new XSolidBrush(AzulEscuro), Margem + 12, _y + 13);
            _y += 27;
        }

        private void Texto(string texto, XFont fonte, XColor cor, double avancar)
        {
            GarantirEspaco(avancar);
            _gfx.DrawString(texto, fonte, new XSolidBrush(cor), Margem, _y + fonte.Size);
            _y += avancar;
        }

        private void TextoQuebrado(string texto, XFont fonte, XColor cor, double largura, double alturaLinha, double? x = null)
        {
            var inicioX = x ?? Margem;
            foreach (var linha in QuebrarLinhas(texto, fonte, largura))
            {
                GarantirEspaco(alturaLinha + 2);
                _gfx.DrawString(linha, fonte, new XSolidBrush(cor), inicioX, _y + fonte.Size);
                _y += alturaLinha;
            }
        }

        private IReadOnlyList<string> QuebrarLinhas(string texto, XFont fonte, double largura)
        {
            var linhas = new List<string>();
            var atual = string.Empty;
            foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidata = atual.Length == 0 ? palavra : $"{atual} {palavra}";
                if (atual.Length > 0 && _gfx.MeasureString(candidata, fonte).Width > largura)
                {
                    linhas.Add(atual);
                    atual = palavra;
                }
                else atual = candidata;
            }
            if (atual.Length > 0) linhas.Add(atual);
            return linhas;
        }

        private void CaixaDestaque(string titulo, string texto, XColor fundo, XColor cor)
        {
            var linhas = QuebrarLinhas(texto, _fonte8, Largura - 24);
            var altura = 33 + linhas.Count * 11;
            GarantirEspaco(altura + 8);
            _gfx.DrawRoundedRectangle(new XPen(cor, .8), new XSolidBrush(fundo), Margem, _y, Largura, altura, 7, 7);
            _gfx.DrawString(titulo, _fonte9b, new XSolidBrush(cor), Margem + 12, _y + 17);
            var linhaY = _y + 30;
            foreach (var linha in linhas)
            {
                _gfx.DrawString(linha, _fonte8, new XSolidBrush(CinzaTexto), Margem + 12, linhaY);
                linhaY += 11;
            }
            _y += altura + 8;
        }

        private void DesenharCards((string Rotulo, string Valor, XColor Cor)[] cards)
        {
            GarantirEspaco(78);
            var gap = 8d;
            var larguraCard = (Largura - gap * (cards.Length - 1)) / cards.Length;
            for (var i = 0; i < cards.Length; i++)
            {
                var x = Margem + i * (larguraCard + gap);
                _gfx.DrawRoundedRectangle(new XPen(CinzaLinha, .8), new XSolidBrush(FundoClaro), x, _y, larguraCard, 64, 6, 6);
                _gfx.DrawString(cards[i].Rotulo, _fonte7, new XSolidBrush(CinzaTexto), x + 9, _y + 15);
                var valor = AjustarTexto(cards[i].Valor, _fonte11b, larguraCard - 18);
                _gfx.DrawString(valor, _fonte11b, new XSolidBrush(cards[i].Cor), x + 9, _y + 40);
            }
            _y += 68;
        }

        private void GraficoBarras((string Rotulo, decimal Valor, bool Melhor, bool Elegivel)[] itens, Func<decimal, string> formatar)
        {
            var altura = itens.Length * 32 + 6;
            GarantirEspaco(altura);
            var maximo = Math.Max(1m, itens.Max(x => x.Valor));
            const double rotuloLargura = 115;
            const double valorLargura = 85;
            var larguraBarra = Largura - rotuloLargura - valorLargura;
            foreach (var item in itens)
            {
                _gfx.DrawString(item.Rotulo, _fonte8, new XSolidBrush(item.Elegivel ? CinzaTexto : XColors.Gray), Margem, _y + 15);
                var barra = larguraBarra * (double)(item.Valor / maximo);
                _gfx.DrawRoundedRectangle(new XSolidBrush(XColor.FromArgb(226, 232, 240)), Margem + rotuloLargura, _y + 4, larguraBarra, 15, 4, 4);
                _gfx.DrawRoundedRectangle(new XSolidBrush(item.Melhor ? Verde : item.Elegivel ? Azul : XColors.Gray),
                    Margem + rotuloLargura, _y + 4, Math.Max(2, barra), 15, 4, 4);
                _gfx.DrawString(formatar(item.Valor), _fonte8, new XSolidBrush(item.Melhor ? Verde : CinzaTexto),
                    new XRect(Margem + rotuloLargura + larguraBarra + 6, _y + 2, valorLargura - 6, 18), XStringFormats.CenterRight);
                _y += 32;
            }
        }

        private void TabelaDuasColunas(IReadOnlyList<(string Nome, string Valor)> linhas)
        {
            var col1 = Largura * .68;
            foreach (var linha in linhas)
            {
                GarantirEspaco(24);
                TabelaCelula(linha.Nome, Margem, _y, col1, 24, _fonte8, false, false);
                TabelaCelula(linha.Valor, Margem + col1, _y, Largura - col1, 24, _fonte8, false, true);
                _y += 24;
            }
        }

        private void TabelaCelula(string texto, double x, double y, double largura, double altura,
            XFont fonte, bool forte, bool direita, XColor? fundo = null)
        {
            _gfx.DrawRectangle(new XPen(CinzaLinha, .7), new XSolidBrush(fundo ?? XColors.White), x, y, largura, altura);
            var fonteUsada = forte && fonte.Size <= 8 ? _fonte9b : fonte;
            var limitado = AjustarTexto(texto, fonteUsada, largura - 12);
            _gfx.DrawString(limitado, fonteUsada, new XSolidBrush(forte ? AzulEscuro : CinzaTexto),
                new XRect(x + 6, y + 4, largura - 12, altura - 8), direita ? XStringFormats.CenterRight : XStringFormats.CenterLeft);
        }

        private string AjustarTexto(string texto, XFont fonte, double largura)
        {
            if (_gfx.MeasureString(texto, fonte).Width <= largura) return texto;
            var reduzido = texto;
            while (reduzido.Length > 2 && _gfx.MeasureString($"{reduzido}…", fonte).Width > largura)
                reduzido = reduzido[..^1];
            return $"{reduzido}…";
        }

        private static decimal ValorTributo(ResultadoRegimeTributario regime, string categoria)
            => regime.Tributos.Where(x => CategoriaTributo(x.Nome) == categoria).Sum(x => x.Valor);

        private static string CategoriaTributo(string nome)
        {
            if (nome.Contains("IRPJ", StringComparison.OrdinalIgnoreCase)) return "IRPJ";
            if (nome.Contains("CSLL", StringComparison.OrdinalIgnoreCase)) return "CSLL";
            if (nome.StartsWith("PIS", StringComparison.OrdinalIgnoreCase)) return "PIS";
            if (nome.StartsWith("Cofins", StringComparison.OrdinalIgnoreCase)) return "COFINS";
            if (nome.Contains("patronais", StringComparison.OrdinalIgnoreCase) || nome.StartsWith("CPP", StringComparison.OrdinalIgnoreCase)) return "CPP";
            if (nome.StartsWith("ICMS", StringComparison.OrdinalIgnoreCase)) return "ICMS";
            if (nome.StartsWith("IPI", StringComparison.OrdinalIgnoreCase)) return "IPI";
            if (nome.StartsWith("ISS", StringComparison.OrdinalIgnoreCase)) return "ISS";
            if (nome.Contains("fora do DAS", StringComparison.OrdinalIgnoreCase)) return "OUTROS";
            return nome;
        }

        private static readonly (string Codigo, string Nome)[] Categorias =
        [
            ("IRPJ", "IRPJ"), ("CSLL", "CSLL"), ("PIS", "PIS/Pasep"), ("COFINS", "Cofins"),
            ("CPP", "CPP / encargos"), ("ICMS", "ICMS"), ("IPI", "IPI"), ("ISS", "ISS"),
            ("OUTROS", "Outros fora do DAS")
        ];
    }

    private static string Moeda(decimal valor) => valor.ToString("C2", PtBr);
    private static string Percentual(decimal valor) => valor.ToString("P2", PtBr);
    private static string FormatarCnpj(string? valor)
    {
        var d = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length == 14 ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..14]}" : valor ?? "Não informado";
    }
}
