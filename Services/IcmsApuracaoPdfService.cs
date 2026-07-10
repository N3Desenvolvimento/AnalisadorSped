using System.Globalization;
using System.Text;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IIcmsApuracaoPdfService
{
    byte[] GerarDemonstrativo(
        EmpresaOpcaoDto? empresa,
        DateTime competencia,
        DashboardFiscalDto dashboard,
        IReadOnlyList<ApuracaoMensalPdfDto> ultimosSeisMeses);

    byte[] GerarAnaliseTresMeses(EmpresaOpcaoDto? empresa, IReadOnlyList<ApuracaoMensalPdfDto> apuracoes);
}

public sealed class IcmsApuracaoPdfService : IIcmsApuracaoPdfService
{
    private static readonly CultureInfo CulturaBrasil = new("pt-BR");
    private readonly byte[]? _logoJpeg;
    private readonly TamanhoImagem? _logoTamanho;

    public IcmsApuracaoPdfService(IWebHostEnvironment environment)
    {
        var logoPath = Path.Combine(environment.WebRootPath, "assets", "LogoN3.jpg");
        if (!File.Exists(logoPath))
        {
            return;
        }

        _logoJpeg = File.ReadAllBytes(logoPath);
        _logoTamanho = LerTamanhoJpeg(_logoJpeg);
    }

    public byte[] GerarDemonstrativo(
        EmpresaOpcaoDto? empresa,
        DateTime competencia,
        DashboardFiscalDto dashboard,
        IReadOnlyList<ApuracaoMensalPdfDto> ultimosSeisMeses)
    {
        var resumo = CriarResumoRelatorio(dashboard);
        var doc = CriarDocumento($"Demonstrativo de apuracao de ICMS - {competencia:MM/yyyy}");
        AdicionarCabecalho(doc, empresa, competencia);

        doc.AdicionarSubtitulo("Resumo da apuracao");
        doc.AdicionarTabela(
            ["Indicador", "Valor"],
            [
                ["Entradas", FormatarMoeda(resumo.ValorTotalEntradas)],
                ["Base ICMS entradas", FormatarMoeda(resumo.BaseIcmsEntradas)],
                ["Saidas", FormatarMoeda(resumo.ValorTotalSaidas)],
                ["Base ICMS saidas", FormatarMoeda(resumo.BaseIcmsSaidas)],
                ["Transferencia entrada", FormatarMoeda(resumo.ValorTransferenciaEntrada)],
                ["Transferencia saida", FormatarMoeda(resumo.ValorTransferenciaSaida)],
                ["ICMS debitado", FormatarMoeda(resumo.IcmsDebitado)],
                ["ICMS creditado", FormatarMoeda(resumo.IcmsCreditado)],
                ["ICMS antecipado", FormatarMoeda(resumo.IcmsAntecipado)],
                ["ICMS a recolher", FormatarMoeda(resumo.IcmsARecolher)]
            ],
            [260, 180]);

        AdicionarGraficosSeisMeses(doc, ultimosSeisMeses);
        AdicionarResumoOperacoes(doc, dashboard.ResumoPorCfopCst);
        AdicionarResumoCfop(doc, "Entradas por CFOP", dashboard.ResumoPorCfopCst, "0");
        AdicionarResumoCfop(doc, "Saidas por CFOP", dashboard.ResumoPorCfopCst, "1");
        AdicionarResumoCfop(doc, "Transferencia entrada por CFOP", dashboard.ResumoPorCfopCst, "TE");
        AdicionarResumoCfop(doc, "Transferencia saida por CFOP", dashboard.ResumoPorCfopCst, "TS");

        return doc.Gerar();
    }

    public byte[] GerarAnaliseTresMeses(EmpresaOpcaoDto? empresa, IReadOnlyList<ApuracaoMensalPdfDto> apuracoes)
    {
        var primeiraCompetencia = apuracoes.FirstOrDefault()?.Competencia;
        var ultimaCompetencia = apuracoes.LastOrDefault()?.Competencia;
        var tituloPeriodo = primeiraCompetencia is null || ultimaCompetencia is null
            ? "Analise dos ultimos 3 meses"
            : $"Analise dos ultimos 3 meses - {primeiraCompetencia:MM/yyyy} a {ultimaCompetencia:MM/yyyy}";

        var doc = CriarDocumento(tituloPeriodo);
        AdicionarCabecalho(doc, empresa, ultimaCompetencia ?? DateTime.Today);

        doc.AdicionarSubtitulo("Comparativo mensal");
        doc.AdicionarTabela(
            ["Mes", "Entradas", "Saidas", "Transf. ent.", "Transf. sai.", "Antecipado", "A recolher"],
            apuracoes.Select(item =>
            {
                var resumo = CriarResumoRelatorio(item.Dashboard);
                return new[]
                {
                    item.Competencia.ToString("MM/yyyy", CulturaBrasil),
                    FormatarMoeda(resumo.ValorTotalEntradas),
                    FormatarMoeda(resumo.ValorTotalSaidas),
                    FormatarMoeda(resumo.ValorTransferenciaEntrada),
                    FormatarMoeda(resumo.ValorTransferenciaSaida),
                    FormatarMoeda(resumo.IcmsAntecipado),
                    FormatarMoeda(resumo.IcmsARecolher)
                };
            }).ToArray(),
            [50, 82, 82, 82, 82, 82, 82]);

        doc.AdicionarSubtitulo("Leitura rapida");
        foreach (var item in apuracoes)
        {
            var resumo = CriarResumoRelatorio(item.Dashboard);
            doc.AdicionarLinha($"{item.Competencia:MM/yyyy}: debitos {FormatarMoeda(resumo.IcmsDebitado)}, creditos {FormatarMoeda(resumo.IcmsCreditado)}, antecipado {FormatarMoeda(resumo.IcmsAntecipado)}, a recolher {FormatarMoeda(resumo.IcmsARecolher)}.");
        }

        var totalRecolher = apuracoes.Sum(item => CriarResumoRelatorio(item.Dashboard).IcmsARecolher);
        doc.AdicionarLinhaEmBranco();
        doc.AdicionarLinhaForte($"Total a recolher no periodo: {FormatarMoeda(totalRecolher)}");

        return doc.Gerar();
    }

    private static void AdicionarCabecalho(PdfTextoSimples doc, EmpresaOpcaoDto? empresa, DateTime competencia)
    {
        doc.AdicionarLinhaForte(empresa?.RazaoSocial ?? "Empresa nao informada");
        doc.AdicionarLinha($"CNPJ: {FormatarCnpj(empresa?.Cnpj)}");
        doc.AdicionarLinha($"Competencia base: {competencia:MM/yyyy}");
        doc.AdicionarLinha($"Emitido em: {DateTime.Now:dd/MM/yyyy HH:mm}");
        doc.AdicionarLinhaEmBranco();
    }

    private static void AdicionarResumoOperacoes(PdfTextoSimples doc, IReadOnlyList<DashboardFiscalCfopCstDto> itens)
    {
        var linhas = TiposOperacao()
            .Select(tipo =>
            {
                var itensTipo = itens
                    .Where(item => PertenceAoTipoOperacao(item, tipo.Codigo))
                    .ToArray();

                return new[]
                {
                    tipo.Nome,
                    FormatarMoeda(itensTipo.Sum(item => item.ValorOperacao)),
                    FormatarMoeda(itensTipo.Sum(item => item.BaseIcms)),
                    FormatarMoeda(itensTipo.Sum(item => item.ValorIcms)),
                    itensTipo.Select(item => item.Cfop).Distinct().Count().ToString(CulturaBrasil)
                };
            })
            .ToArray();

        doc.AdicionarSubtitulo("Resumo por operacao");
        doc.AdicionarTabela(
            ["Operacao", "Valor operacao", "Base ICMS", "Valor ICMS", "CFOPs"],
            linhas,
            [130, 110, 110, 110, 55]);
    }

    private static void AdicionarGraficosSeisMeses(PdfTextoSimples doc, IReadOnlyList<ApuracaoMensalPdfDto> apuracoes)
    {
        if (apuracoes.Count == 0)
        {
            return;
        }

        doc.AdicionarSubtitulo("Graficos dos ultimos 6 meses");
        doc.AdicionarGraficoLinha("Entradas", apuracoes, item => CriarResumoRelatorio(item.Dashboard).ValorTotalEntradas);
        doc.AdicionarGraficoLinha("Saidas", apuracoes, item => CriarResumoRelatorio(item.Dashboard).ValorTotalSaidas);
        doc.AdicionarGraficoLinha("ICMS a recolher", apuracoes, item => item.Dashboard.Resumo.IcmsARecolher);
        doc.AdicionarGraficoLinha("ICMS antecipado", apuracoes, item => item.Dashboard.Resumo.IcmsAntecipado);
    }

    private static void AdicionarResumoCfop(PdfTextoSimples doc, string titulo, IReadOnlyList<DashboardFiscalCfopCstDto> itens, string tipoOperacao)
    {
        var linhas = itens
            .Where(item => PertenceAoTipoOperacao(item, tipoOperacao))
            .GroupBy(item => item.Cfop)
            .Select(grupo => new
            {
                Cfop = grupo.Key,
                ValorOperacao = grupo.Sum(item => item.ValorOperacao),
                BaseIcms = grupo.Sum(item => item.BaseIcms),
                ValorIcms = grupo.Sum(item => item.ValorIcms)
            })
            .OrderBy(item => item.Cfop)
            .ToArray();

        doc.AdicionarSubtitulo(titulo);
        if (linhas.Length == 0)
        {
            doc.AdicionarLinha("Nenhum CFOP encontrado.");
            doc.AdicionarLinhaEmBranco();
            return;
        }

        doc.AdicionarTabela(
            ["CFOP", "Valor operacao", "Base ICMS", "Valor ICMS"],
            linhas.Select(item => new[]
            {
                item.Cfop,
                FormatarMoeda(item.ValorOperacao),
                FormatarMoeda(item.BaseIcms),
                FormatarMoeda(item.ValorIcms)
            }).ToArray(),
            [70, 145, 145, 145]);
    }

    private PdfTextoSimples CriarDocumento(string titulo)
    {
        return new PdfTextoSimples(titulo, _logoJpeg, _logoTamanho);
    }

    private static ResumoRelatorio CriarResumoRelatorio(DashboardFiscalDto dashboard)
    {
        var itens = dashboard.ResumoPorCfopCst.ToArray();

        var totalEntradas = itens
            .Where(item => PertenceAoTipoOperacao(item, "0"))
            .Sum(item => item.ValorOperacao);

        var totalSaidas = itens
            .Where(item => PertenceAoTipoOperacao(item, "1"))
            .Sum(item => item.ValorOperacao);

        var totalTransferenciaEntrada = itens
            .Where(item => PertenceAoTipoOperacao(item, "TE"))
            .Sum(item => item.ValorOperacao);

        var totalTransferenciaSaida = itens
            .Where(item => PertenceAoTipoOperacao(item, "TS"))
            .Sum(item => item.ValorOperacao);

        var icmsCreditado = itens
            .Where(item => PertenceAoTipoOperacao(item, "0"))
            .Sum(item => item.ValorIcms);

        var icmsDebitado = itens
            .Where(item => PertenceAoTipoOperacao(item, "1"))
            .Sum(item => item.ValorIcms);

        var baseIcmsEntradas = itens
            .Where(item => PertenceAoTipoOperacao(item, "0"))
            .Sum(item => item.BaseIcms);

        var baseIcmsSaidas = itens
            .Where(item => PertenceAoTipoOperacao(item, "1"))
            .Sum(item => item.BaseIcms);

        return new ResumoRelatorio
        {
            ValorTotalEntradas = totalEntradas,
            ValorTotalSaidas = totalSaidas,
            ValorTransferenciaEntrada = totalTransferenciaEntrada,
            ValorTransferenciaSaida = totalTransferenciaSaida,
            BaseIcmsEntradas = baseIcmsEntradas,
            BaseIcmsSaidas = baseIcmsSaidas,
            IcmsDebitado = icmsDebitado,
            IcmsCreditado = icmsCreditado,
            IcmsAntecipado = dashboard.Resumo.IcmsAntecipado,
            IcmsARecolher = Math.Max(0, icmsDebitado - icmsCreditado - dashboard.Resumo.IcmsAntecipado)
        };
    }

    private static bool PertenceAoTipoOperacao(DashboardFiscalCfopCstDto item, string tipoOperacao)
    {
        var transferencia = CfopTransferencia(item.Cfop);

        return tipoOperacao switch
        {
            "0" => item.IndicadorOperacao == "0" && !transferencia,
            "1" => item.IndicadorOperacao == "1" && !transferencia,
            "TE" => item.IndicadorOperacao == "0" && transferencia,
            "TS" => item.IndicadorOperacao == "1" && transferencia,
            _ => false
        };
    }

    private static bool CfopTransferencia(string cfop)
    {
        cfop = cfop.Trim();

        return cfop is "1151" or "1152" or "1408"
            or "2151" or "2152" or "2408" or "2409"
            or "5151" or "5152" or "5408" or "5409"
            or "6151" or "6152" or "6408" or "6409";
    }

    private static (string Codigo, string Nome)[] TiposOperacao()
    {
        return
        [
            ("0", "Entradas"),
            ("1", "Saidas"),
            ("TE", "Transferencia entrada"),
            ("TS", "Transferencia saida")
        ];
    }

    private static string FormatarMoeda(decimal valor)
    {
        return valor.ToString("C", CulturaBrasil);
    }

    private static string FormatarCnpj(string? cnpj)
    {
        var digitos = string.IsNullOrWhiteSpace(cnpj) ? string.Empty : new string(cnpj.Where(char.IsDigit).ToArray());
        if (digitos.Length != 14)
        {
            return cnpj ?? string.Empty;
        }

        return $"{digitos[..2]}.{digitos.Substring(2, 3)}.{digitos.Substring(5, 3)}/{digitos.Substring(8, 4)}-{digitos.Substring(12, 2)}";
    }

    private static TamanhoImagem? LerTamanhoJpeg(byte[] bytes)
    {
        var index = 2;
        while (index + 9 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            var length = bytes[index + 2] * 256 + bytes[index + 3];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                var altura = bytes[index + 5] * 256 + bytes[index + 6];
                var largura = bytes[index + 7] * 256 + bytes[index + 8];
                return new TamanhoImagem(largura, altura);
            }

            index += 2 + length;
        }

        return null;
    }

    private sealed class PdfTextoSimples
    {
        private const double LarguraPagina = 595;
        private const double AlturaPagina = 842;
        private const double MargemEsquerda = 42;
        private const double MargemSuperior = 48;
        private const double AlturaLinha = 15;
        private readonly byte[]? _logoJpeg;
        private readonly TamanhoImagem? _logoTamanho;
        private readonly List<List<ElementoPdf>> _paginas = [[]];
        private double _y = AlturaPagina - MargemSuperior;

        public PdfTextoSimples(string titulo, byte[]? logoJpeg, TamanhoImagem? logoTamanho)
        {
            _logoJpeg = logoJpeg;
            _logoTamanho = logoTamanho;
            AdicionarLogo();
            AdicionarTexto(titulo, 16, true);
            AdicionarLinhaEmBranco();
        }

        public void AdicionarSubtitulo(string texto)
        {
            AdicionarLinhaEmBranco();
            AdicionarTexto(texto, 12, true);
        }

        public void AdicionarLinha(string texto)
        {
            AdicionarTexto(texto, 10, false);
        }

        public void AdicionarLinhaForte(string texto)
        {
            AdicionarTexto(texto, 10, true);
        }

        public void AdicionarLinhaEmBranco()
        {
            _y -= AlturaLinha;
            GarantirEspaco();
        }

        public void AdicionarTabela(string[] cabecalhos, IReadOnlyList<string[]> linhas, double[] larguras)
        {
            var colunasNumericas = IdentificarColunasNumericas(cabecalhos, linhas);

            AdicionarLinhaTabela(cabecalhos, larguras, colunasNumericas, true);
            foreach (var linha in linhas)
            {
                AdicionarLinhaTabela(linha, larguras, colunasNumericas, false);
            }

            AdicionarLinhaEmBranco();
        }

        public void AdicionarGraficoLinha(string titulo, IReadOnlyList<ApuracaoMensalPdfDto> apuracoes, Func<ApuracaoMensalPdfDto, decimal> seletorValor)
        {
            GarantirEspacoMinimo(130);
            AdicionarTexto(titulo, 10, true);

            var origemX = MargemEsquerda;
            var origemY = _y - 90;
            var largura = 500d;
            var altura = 80d;
            var valores = apuracoes.Select(seletorValor).ToArray();
            var maiorValor = valores.DefaultIfEmpty(0).Max();
            var passoX = apuracoes.Count <= 1 ? largura : largura / (apuracoes.Count - 1);
            var pontos = new List<(double X, double Y, decimal Valor, string Label)>();

            for (var i = 0; i < apuracoes.Count; i++)
            {
                var valor = valores[i];
                var x = origemX + (i * passoX);
                var y = maiorValor <= 0 || valor <= 0
                    ? origemY
                    : origemY + ((double)(valor / maiorValor) * altura);
                pontos.Add((x, y, valor, apuracoes[i].Competencia.ToString("MM/yy", CultureInfo.InvariantCulture)));
            }

            _paginas[^1].Add(ElementoPdf.Linha(origemX, origemY, origemX + largura, origemY, "#CBD5E1", 0.8));
            _paginas[^1].Add(ElementoPdf.Linha(origemX, origemY, origemX, origemY + altura + 8, "#CBD5E1", 0.8));

            for (var i = 1; i < pontos.Count; i++)
            {
                _paginas[^1].Add(ElementoPdf.Linha(pontos[i - 1].X, pontos[i - 1].Y, pontos[i].X, pontos[i].Y, "#155DA8", 1.8));
            }

            foreach (var ponto in pontos)
            {
                _paginas[^1].Add(ElementoPdf.Circulo(ponto.X, ponto.Y, 2.5, "#155DA8"));
                _paginas[^1].Add(ElementoPdf.CriarTexto(FormatarMoedaCompactaPdf(ponto.Valor), ponto.X - 16, ponto.Y + 8, 7, false));
                _paginas[^1].Add(ElementoPdf.CriarTexto(ponto.Label, ponto.X - 10, origemY - 14, 7, false));
            }

            _y = origemY - 28;
            GarantirEspaco();
        }

        public byte[] Gerar()
        {
            var objetos = new List<string>();
            var paginasIds = new List<int>();

            foreach (var pagina in _paginas)
            {
                var conteudo = MontarConteudoPagina(pagina);
                var conteudoId = objetos.Count + 1;
                objetos.Add($"<< /Length {Encoding.ASCII.GetByteCount(conteudo)} >>\nstream\n{conteudo}\nendstream");
                var paginaId = objetos.Count + 1;
                objetos.Add($"<< /Type /Page /Parent 0 0 R /MediaBox [0 0 {LarguraPagina.ToString(CultureInfo.InvariantCulture)} {AlturaPagina.ToString(CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 0 0 R /F2 0 0 R >> /XObject << /Im1 0 0 R >> >> /Contents {conteudoId} 0 R >>");
                paginasIds.Add(paginaId);
            }

            var imagemId = 0;
            if (_logoJpeg is not null && _logoTamanho is not null)
            {
                imagemId = objetos.Count + 1;
                var largura = _logoTamanho.Value.Largura;
                var altura = _logoTamanho.Value.Altura;
                var dadosImagem = Encoding.Latin1.GetString(_logoJpeg);
                objetos.Add($"<< /Type /XObject /Subtype /Image /Width {largura} /Height {altura} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {_logoJpeg.Length} >>\nstream\n{dadosImagem}\nendstream");
            }

            var fonteNormalId = objetos.Count + 1;
            objetos.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var fonteForteId = objetos.Count + 1;
            objetos.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
            var paginasId = objetos.Count + 1;
            var kids = string.Join(" ", paginasIds.Select(id => $"{id} 0 R"));
            objetos.Add($"<< /Type /Pages /Kids [{kids}] /Count {paginasIds.Count} >>");
            var catalogoId = objetos.Count + 1;
            objetos.Add($"<< /Type /Catalog /Pages {paginasId} 0 R >>");

            for (var i = 0; i < objetos.Count; i++)
            {
                objetos[i] = objetos[i]
                    .Replace("/Parent 0 0 R", $"/Parent {paginasId} 0 R", StringComparison.Ordinal)
                    .Replace("/F1 0 0 R", $"/F1 {fonteNormalId} 0 R", StringComparison.Ordinal)
                    .Replace("/F2 0 0 R", $"/F2 {fonteForteId} 0 R", StringComparison.Ordinal)
                    .Replace("/Im1 0 0 R", imagemId == 0 ? string.Empty : $"/Im1 {imagemId} 0 R", StringComparison.Ordinal);
            }

            return MontarPdf(objetos, catalogoId);
        }

        private void AdicionarLinhaTabela(string[] valores, double[] larguras, bool[] colunasNumericas, bool cabecalho)
        {
            GarantirEspaco();
            var x = MargemEsquerda;
            for (var i = 0; i < valores.Length; i++)
            {
                var texto = LimitarTexto(valores[i], Math.Max(8, (int)(larguras[i] / 6)));
                var textoX = x;
                if (i < colunasNumericas.Length && colunasNumericas[i])
                {
                    textoX = x + larguras[i] - EstimarLarguraTexto(texto, 9, cabecalho) - 4;
                }

                _paginas[^1].Add(ElementoPdf.CriarTexto(texto, Math.Max(x, textoX), _y, 9, cabecalho));
                x += larguras[i];
            }

            _y -= AlturaLinha;
        }

        private static bool[] IdentificarColunasNumericas(string[] cabecalhos, IReadOnlyList<string[]> linhas)
        {
            var resultado = new bool[cabecalhos.Length];
            for (var i = 0; i < cabecalhos.Length; i++)
            {
                var cabecalho = NormalizarTexto(cabecalhos[i]).ToUpperInvariant();
                if (cabecalho is "CFOP" or "FORNECEDOR" or "MES")
                {
                    continue;
                }

                resultado[i] = cabecalho.Contains("VALOR", StringComparison.Ordinal)
                    || cabecalho.Contains("ICMS", StringComparison.Ordinal)
                    || cabecalho.Contains("BASE", StringComparison.Ordinal)
                    || cabecalho.Contains("ENTRAD", StringComparison.Ordinal)
                    || cabecalho.Contains("SAID", StringComparison.Ordinal)
                    || cabecalho.Contains("TRANSF", StringComparison.Ordinal)
                    || cabecalho.Contains("ANTECIP", StringComparison.Ordinal)
                    || cabecalho.Contains("RECOLHER", StringComparison.Ordinal)
                    || cabecalho.Contains("CFOPS", StringComparison.Ordinal)
                    || cabecalho.Contains("NOTAS", StringComparison.Ordinal)
                    || linhas.Any(linha => i < linha.Length && PareceNumero(linha[i]));
            }

            return resultado;
        }

        private static bool PareceNumero(string valor)
        {
            var texto = NormalizarTexto(valor).Trim();
            if (texto.StartsWith("R$ ", StringComparison.Ordinal))
            {
                return true;
            }

            return decimal.TryParse(texto, NumberStyles.Number, CulturaBrasil, out _);
        }

        private static double EstimarLarguraTexto(string texto, int tamanho, bool forte)
        {
            var fator = forte ? 0.58 : 0.53;
            return NormalizarTexto(texto).Length * tamanho * fator;
        }

        private void AdicionarTexto(string texto, int tamanho, bool forte)
        {
            foreach (var linha in QuebrarLinha(texto, tamanho == 16 ? 74 : 95))
            {
                GarantirEspaco();
                _paginas[^1].Add(ElementoPdf.CriarTexto(linha, MargemEsquerda, _y, tamanho, forte));
                _y -= AlturaLinha;
            }
        }

        private void AdicionarLogo()
        {
            if (_logoJpeg is null || _logoTamanho is null)
            {
                return;
            }

            const double largura = 118;
            var altura = largura * _logoTamanho.Value.Altura / _logoTamanho.Value.Largura;
            var y = AlturaPagina - MargemSuperior - altura;
            _paginas[^1].Add(ElementoPdf.CriarImagem(MargemEsquerda, y, largura, altura));
            _y = y - 22;
        }

        private void GarantirEspaco()
        {
            if (_y >= 48)
            {
                return;
            }

            _paginas.Add([]);
            _y = AlturaPagina - MargemSuperior;
        }

        private void GarantirEspacoMinimo(double alturaNecessaria)
        {
            if (_y - alturaNecessaria >= 48)
            {
                return;
            }

            _paginas.Add([]);
            _y = AlturaPagina - MargemSuperior;
        }

        private static IEnumerable<string> QuebrarLinha(string texto, int tamanhoMaximo)
        {
            var textoSeguro = NormalizarTexto(texto);
            while (textoSeguro.Length > tamanhoMaximo)
            {
                var corte = textoSeguro.LastIndexOf(' ', Math.Min(tamanhoMaximo, textoSeguro.Length - 1));
                if (corte <= 0)
                {
                    corte = tamanhoMaximo;
                }

                yield return textoSeguro[..corte];
                textoSeguro = textoSeguro[corte..].TrimStart();
            }

            yield return textoSeguro;
        }

        private static string MontarConteudoPagina(IEnumerable<ElementoPdf> elementos)
        {
            var sb = new StringBuilder();
            foreach (var elemento in elementos)
            {
                if (elemento.Tipo == TipoElementoPdf.Imagem)
                {
                    sb.Append("q ");
                    sb.Append(elemento.Largura.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" 0 0 ");
                    sb.Append(elemento.Altura.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.X.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine(" cm /Im1 Do Q");
                    continue;
                }

                if (elemento.Tipo == TipoElementoPdf.Linha)
                {
                    sb.AppendLine("q");
                    AplicarCorStroke(sb, elemento.Cor);
                    sb.Append(elemento.LarguraLinha.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" w ");
                    sb.Append(elemento.X.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" m ");
                    sb.Append(elemento.X2.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y2.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine(" l S");
                    sb.AppendLine("Q");
                    continue;
                }

                if (elemento.Tipo == TipoElementoPdf.Circulo)
                {
                    sb.AppendLine("q");
                    AplicarCorFill(sb, elemento.Cor);
                    var c = 0.55228475 * elemento.Raio;
                    sb.Append((elemento.X + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" m ");
                    sb.Append((elemento.X + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y + c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X + c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.X.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" c ");
                    sb.Append((elemento.X - c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y + c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" c ");
                    sb.Append((elemento.X - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y - c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X - c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.X.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(" c ");
                    sb.Append((elemento.X + c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y - elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.Y - c).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append((elemento.X + elemento.Raio).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine(" c f");
                    sb.AppendLine("Q");
                    continue;
                }

                sb.Append("BT ");
                sb.Append(elemento.Forte ? "/F2 " : "/F1 ");
                sb.Append(elemento.Tamanho.ToString(CultureInfo.InvariantCulture));
                sb.Append(" Tf ");
                sb.Append(elemento.X.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(elemento.Y.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(" Td (");
                sb.Append(EscaparPdf(elemento.Texto));
                sb.AppendLine(") Tj ET");
            }

            return sb.ToString();
        }

        private static void AplicarCorStroke(StringBuilder sb, string cor)
        {
            var (r, g, b) = ConverterCor(cor);
            sb.Append(r).Append(' ').Append(g).Append(' ').Append(b).AppendLine(" RG");
        }

        private static void AplicarCorFill(StringBuilder sb, string cor)
        {
            var (r, g, b) = ConverterCor(cor);
            sb.Append(r).Append(' ').Append(g).Append(' ').Append(b).AppendLine(" rg");
        }

        private static (string R, string G, string B) ConverterCor(string cor)
        {
            var hex = cor.TrimStart('#');
            var r = Convert.ToInt32(hex[..2], 16) / 255d;
            var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255d;
            var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255d;
            return (
                r.ToString("0.###", CultureInfo.InvariantCulture),
                g.ToString("0.###", CultureInfo.InvariantCulture),
                b.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static byte[] MontarPdf(IReadOnlyList<string> objetos, int catalogoId)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.Latin1, leaveOpen: true);
            var offsets = new List<long> { 0 };

            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            for (var i = 0; i < objetos.Count; i++)
            {
                offsets.Add(stream.Position);
                writer.WriteLine($"{i + 1} 0 obj");
                writer.WriteLine(objetos[i]);
                writer.WriteLine("endobj");
                writer.Flush();
            }

            var xref = stream.Position;
            writer.WriteLine("xref");
            writer.WriteLine($"0 {objetos.Count + 1}");
            writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }

            writer.WriteLine("trailer");
            writer.WriteLine($"<< /Size {objetos.Count + 1} /Root {catalogoId} 0 R >>");
            writer.WriteLine("startxref");
            writer.WriteLine(xref);
            writer.WriteLine("%%EOF");
            writer.Flush();

            return stream.ToArray();
        }

        private static string LimitarTexto(string texto, int tamanhoMaximo)
        {
            var textoSeguro = NormalizarTexto(texto);
            if (textoSeguro.Length <= tamanhoMaximo)
            {
                return textoSeguro;
            }

            return textoSeguro[..Math.Max(0, tamanhoMaximo - 3)] + "...";
        }

        private static string NormalizarTexto(string texto)
        {
            return texto
                .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
                .Replace("é", "e").Replace("ê", "e")
                .Replace("í", "i")
                .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
                .Replace("ú", "u")
                .Replace("ç", "c")
                .Replace("Á", "A").Replace("À", "A").Replace("Ã", "A").Replace("Â", "A")
                .Replace("É", "E").Replace("Ê", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O").Replace("Ô", "O").Replace("Õ", "O")
                .Replace("Ú", "U")
                .Replace("Ç", "C");
        }

        private static string EscaparPdf(string texto)
        {
            return texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        private static string FormatarMoedaCompactaPdf(decimal valor)
        {
            if (valor >= 1_000_000)
            {
                return $"R$ {(valor / 1_000_000).ToString("N1", CulturaBrasil)} mi";
            }

            if (valor >= 1_000)
            {
                return $"R$ {(valor / 1_000).ToString("N0", CulturaBrasil)} mil";
            }

            return valor.ToString("C0", CulturaBrasil);
        }

        private sealed record ElementoPdf(
            TipoElementoPdf Tipo,
            string Texto,
            double X,
            double Y,
            int Tamanho,
            bool Forte,
            double Largura,
            double Altura,
            double X2,
            double Y2,
            string Cor,
            double LarguraLinha,
            double Raio)
        {
            public static ElementoPdf CriarTexto(string texto, double x, double y, int tamanho, bool forte)
            {
                return new ElementoPdf(TipoElementoPdf.Texto, texto, x, y, tamanho, forte, 0, 0, 0, 0, "#000000", 0, 0);
            }

            public static ElementoPdf CriarImagem(double x, double y, double largura, double altura)
            {
                return new ElementoPdf(TipoElementoPdf.Imagem, string.Empty, x, y, 0, false, largura, altura, 0, 0, "#000000", 0, 0);
            }

            public static ElementoPdf Linha(double x1, double y1, double x2, double y2, string cor, double larguraLinha)
            {
                return new ElementoPdf(TipoElementoPdf.Linha, string.Empty, x1, y1, 0, false, 0, 0, x2, y2, cor, larguraLinha, 0);
            }

            public static ElementoPdf Circulo(double x, double y, double raio, string cor)
            {
                return new ElementoPdf(TipoElementoPdf.Circulo, string.Empty, x, y, 0, false, 0, 0, 0, 0, cor, 0, raio);
            }
        }

        private enum TipoElementoPdf
        {
            Texto,
            Imagem,
            Linha,
            Circulo
        }
    }

    private sealed class ResumoRelatorio
    {
        public decimal ValorTotalEntradas { get; set; }
        public decimal ValorTotalSaidas { get; set; }
        public decimal ValorTransferenciaEntrada { get; set; }
        public decimal ValorTransferenciaSaida { get; set; }
        public decimal BaseIcmsEntradas { get; set; }
        public decimal BaseIcmsSaidas { get; set; }
        public decimal IcmsDebitado { get; set; }
        public decimal IcmsCreditado { get; set; }
        public decimal IcmsAntecipado { get; set; }
        public decimal IcmsARecolher { get; set; }
    }

    private readonly record struct TamanhoImagem(int Largura, int Altura);
}

public sealed class ApuracaoMensalPdfDto
{
    public DateTime Competencia { get; set; }
    public DashboardFiscalDto Dashboard { get; set; } = new();
}
