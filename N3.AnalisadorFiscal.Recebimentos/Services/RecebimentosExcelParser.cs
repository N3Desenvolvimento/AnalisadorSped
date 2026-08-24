using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Recebimentos.Services;

public sealed class RecebimentosExcelParser
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<RecebimentosParseResult> LerAsync(
        int empresaId,
        DateTime competenciaArquivo,
        string caminhoArquivo,
        CancellationToken cancellationToken = default)
    {
        await using var arquivo = File.OpenRead(caminhoArquivo);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(arquivo, cancellationToken)).ToLowerInvariant();
        arquivo.Position = 0;

        using var zip = new ZipArchive(arquivo, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = LerSharedStrings(zip);
        var planilhas = LerPlanilhas(zip);
        if (planilhas.Count == 0)
            throw new InvalidOperationException("A planilha não possui abas legíveis.");

        AbaLida? abaSelecionada = null;
        MapeamentoColunas? mapeamento = null;
        foreach (var planilha in planilhas)
        {
            var aba = LerAba(zip, planilha, sharedStrings);
            var mapa = LocalizarCabecalho(aba);
            if (mapa is null)
                continue;
            abaSelecionada = aba;
            mapeamento = mapa;
            break;
        }

        if (abaSelecionada is null || mapeamento is null)
            throw new InvalidOperationException(
                "Nenhuma aba contém o modelo de serviços (DATA RECEBIMENTO, CLIENTE, VALOR FATURADO e VALOR RECEBIDO) " +
                "nem o modelo de comércio (NUMERO DA NOTA, VALOR DA PARCELA RECEBIDA e DATA DO RECEBIMENTO).");

        var importacao = new RecebimentoImportacao
        {
            EmpresaId = empresaId,
            CompetenciaArquivo = competenciaArquivo.Date,
            NomeArquivo = Path.GetFileName(caminhoArquivo),
            HashArquivo = hash,
            NomeAba = abaSelecionada.Nome,
            ImportadoEm = DateTime.UtcNow
        };
        var mensagens = new List<string>();
        var linhas = ExtrairLinhasDados(abaSelecionada, mapeamento);
        importacao.LinhasLidas = linhas.Count;

        var alertasPorLinha = new Dictionary<int, List<string>>();
        if (mapeamento.Comercio)
        {
            ProcessarComercio(empresaId, linhas, mapeamento, importacao, mensagens, alertasPorLinha);
        }
        else
        {
            var grupos = AgruparPorCliente(linhas, mapeamento, alertasPorLinha);
            foreach (var grupo in grupos)
                ProcessarGrupo(empresaId, grupo, mapeamento, importacao, mensagens, alertasPorLinha);
        }

        foreach (var linha in linhas)
        {
            alertasPorLinha.TryGetValue(linha.Numero, out var alertas);
            importacao.LinhasOrigem.Add(new RecebimentoLinhaOrigem
            {
                NumeroLinha = linha.Numero,
                ConteudoJson = JsonSerializer.Serialize(linha.Celulas.ToDictionary(
                    item => NomeColuna(item.Key), item => item.Value)),
                Alerta = alertas is null ? null : string.Join(" ", alertas.Distinct())
            });
        }

        importacao.TotalFaturadoArquivo = ArredondarMoeda(importacao.Documentos.Sum(x => x.ValorFaturado));
        importacao.TotalRecebidoArquivo = ArredondarMoeda(importacao.Movimentos.Sum(x => x.ValorRecebido));
        importacao.TotalRetencoesArquivo = Math.Round(importacao.Documentos.Sum(x => x.TotalRetencoes), 4);
        var foraDaCompetencia = importacao.Movimentos.Count(x =>
            x.DataRecebimento.Year != competenciaArquivo.Year || x.DataRecebimento.Month != competenciaArquivo.Month);
        if (foraDaCompetencia > 0)
            mensagens.Insert(0,
                $"{foraDaCompetencia} recebimento(s) pertencem a outras competências e foram classificados pela data efetiva do recebimento.");

        var antecipados = importacao.Movimentos.Count(x => x.RecebidoAntesEmissao);
        if (antecipados > 0)
            mensagens.Add($"{antecipados} recebimento(s) ocorreram antes da emissão do documento e foram mantidos como antecipados.");

        importacao.QuantidadeAlertas = alertasPorLinha.Values.SelectMany(x => x)
            .Concat(mensagens)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var competencias = importacao.Movimentos
            .GroupBy(x => new { x.DataRecebimento.Year, x.DataRecebimento.Month })
            .OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month)
            .Select(x => new RecebimentoCompetenciaImportada(
                x.Key.Year, x.Key.Month, x.Count(), ArredondarMoeda(x.Sum(item => item.ValorRecebido))))
            .ToArray();

        return new RecebimentosParseResult(importacao, competencias, mensagens.Distinct().ToArray());
    }

    private static IReadOnlyList<LinhaAba> ExtrairLinhasDados(AbaLida aba, MapeamentoColunas mapa)
    {
        var resultado = new List<LinhaAba>();
        foreach (var linha in aba.Linhas.Where(x => x.Numero > mapa.LinhaCabecalho).OrderBy(x => x.Numero))
        {
            var marcador = ObterTexto(linha, mapa.Comercio ? mapa.Nota : mapa.DataRecebimento);
            if (Normalizar(marcador) is "TOTAL" or "TOTAIS" or "TOTAISRECEITA")
                break;
            if (linha.Celulas.Values.All(string.IsNullOrWhiteSpace))
                continue;
            if (!mapa.Comercio &&
                string.IsNullOrWhiteSpace(ObterTexto(linha, mapa.Cliente)) &&
                string.IsNullOrWhiteSpace(ObterIdentificador(linha, mapa.Nota)) &&
                ObterData(linha, mapa.Emissao) is null &&
                ObterData(linha, mapa.DataRecebimento) is null)
                continue;
            resultado.Add(linha);
        }
        return resultado;
    }

    private static void ProcessarComercio(
        int empresaId,
        IReadOnlyList<LinhaAba> linhas,
        MapeamentoColunas mapa,
        RecebimentoImportacao importacao,
        List<string> mensagens,
        Dictionary<int, List<string>> alertas)
    {
        const string cliente = "(Notas de comércio)";
        var documentos = new HashSet<string>(StringComparer.Ordinal);
        var ocorrenciasMovimento = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var linha in linhas)
        {
            var numeroNota = ObterIdentificador(linha, mapa.Nota);
            var valorRecebido = ObterDecimal(linha, mapa.ValorRecebido);
            var dataRecebimento = ObterData(linha, mapa.DataRecebimento);
            if (string.IsNullOrWhiteSpace(numeroNota) || valorRecebido is null || dataRecebimento is null)
            {
                AdicionarAlerta(alertas, linha.Numero,
                    "A linha comercial precisa informar número da nota, valor da parcela recebida e data do recebimento.");
                continue;
            }

            var notaNormalizada = Normalizar(numeroNota);
            if (documentos.Add(notaNormalizada))
            {
                importacao.Documentos.Add(new RecebimentoDocumento
                {
                    ChaveNatural = GerarChave($"{empresaId}|COMERCIO|NOTA|{notaNormalizada}"),
                    Cliente = cliente,
                    NumeroNota = numeroNota,
                    ValorFaturado = 0,
                    DescontoInformado = 0,
                    LinhaOrigem = linha.Numero
                });
            }

            var valor = ArredondarMoeda(valorRecebido.Value);
            var baseChave = $"COMERCIO|{notaNormalizada}|{dataRecebimento:yyyyMMdd}|{valor:0.00}";
            ocorrenciasMovimento[baseChave] = ocorrenciasMovimento.GetValueOrDefault(baseChave) + 1;
            importacao.Movimentos.Add(new RecebimentoMovimento
            {
                ChaveNatural = GerarChave($"{empresaId}|{baseChave}|{ocorrenciasMovimento[baseChave]}"),
                Cliente = cliente,
                DataRecebimento = dataRecebimento.Value,
                ValorRecebido = valor,
                NotasVinculadas = numeroNota,
                RecebidoAntesEmissao = false,
                LinhaOrigem = linha.Numero
            });
        }

        mensagens.Add(
            "Modelo de notas comerciais identificado: cada linha foi mantida como uma parcela recebida, sem desconto; " +
            "a emissão, o valor integral e o cliente serão conferidos na nota do Fortes.");
    }

    private static IReadOnlyList<List<LinhaAba>> AgruparPorCliente(
        IReadOnlyList<LinhaAba> linhas,
        MapeamentoColunas mapa,
        Dictionary<int, List<string>> alertas)
    {
        var grupos = new List<List<LinhaAba>>();
        List<LinhaAba>? atual = null;
        foreach (var linha in linhas)
        {
            var cliente = ObterTexto(linha, mapa.Cliente);
            if (!string.IsNullOrWhiteSpace(cliente))
            {
                atual = [];
                grupos.Add(atual);
            }
            else if (atual is null)
            {
                atual = [];
                grupos.Add(atual);
                AdicionarAlerta(alertas, linha.Numero, "Linha sem cliente; será agrupada como cliente não informado.");
            }
            atual.Add(linha);
        }
        return grupos;
    }

    private static void ProcessarGrupo(
        int empresaId,
        IReadOnlyList<LinhaAba> linhas,
        MapeamentoColunas mapa,
        RecebimentoImportacao importacao,
        List<string> mensagens,
        Dictionary<int, List<string>> alertas)
    {
        var cliente = linhas.Select(x => ObterTexto(x, mapa.Cliente)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim()
            ?? "(Cliente não informado)";
        var documentosDoGrupo = new List<RecebimentoDocumento>();
        var ocorrenciasDocumento = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var linha in linhas)
        {
            var numeroNota = ObterIdentificador(linha, mapa.Nota);
            var valorFaturado = ObterDecimal(linha, mapa.Faturado);
            if (string.IsNullOrWhiteSpace(numeroNota) && valorFaturado is null)
                continue;

            var dataEmissao = ObterData(linha, mapa.Emissao);
            var baseChave = !string.IsNullOrWhiteSpace(numeroNota)
                ? $"NOTA|{Normalizar(numeroNota)}"
                : $"SEMNOTA|{Normalizar(cliente)}|{dataEmissao:yyyyMMdd}";
            ocorrenciasDocumento[baseChave] = ocorrenciasDocumento.GetValueOrDefault(baseChave) + 1;
            var documento = new RecebimentoDocumento
            {
                ChaveNatural = GerarChave($"{empresaId}|{baseChave}|{ocorrenciasDocumento[baseChave]}"),
                Cliente = cliente,
                DataEmissao = dataEmissao,
                ValorFaturado = ArredondarMoeda(valorFaturado ?? 0),
                DescontoInformado = ObterDecimal(linha, mapa.Desconto) is { } desconto ? ArredondarMoeda(desconto) : null,
                ValorIss = ObterDecimal(linha, mapa.Iss) ?? 0,
                ValorPis = ObterDecimal(linha, mapa.Pis) ?? 0,
                ValorCofins = ObterDecimal(linha, mapa.Cofins) ?? 0,
                ValorIr = ObterDecimal(linha, mapa.Ir) ?? 0,
                ValorCsll = ObterDecimal(linha, mapa.Csll) ?? 0,
                ValorInss = ObterDecimal(linha, mapa.Inss) ?? 0,
                ValorOutros = ObterDecimal(linha, mapa.Outros) ?? 0,
                CodigoServico = ObterTexto(linha, mapa.CodigoServico),
                NumeroNota = numeroNota,
                IndicadorRetencoesFederais = ObterTexto(linha, mapa.IndicadorFederal),
                IndicadorIss = ObterTexto(linha, mapa.IndicadorIss),
                LinhaOrigem = linha.Numero
            };
            documentosDoGrupo.Add(documento);
            importacao.Documentos.Add(documento);
        }

        var notas = string.Join("; ", documentosDoGrupo
            .Select(x => x.NumeroNota).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        var emissaoReferencia = documentosDoGrupo.Where(x => x.DataEmissao.HasValue)
            .Select(x => x.DataEmissao!.Value).DefaultIfEmpty().Min();
        DateTime? emissao = emissaoReferencia == default ? null : emissaoReferencia;
        var ocorrenciasMovimento = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var linha in linhas)
        {
            var valorRecebido = ObterDecimal(linha, mapa.ValorRecebido);
            if (valorRecebido is null)
                continue;
            var dataRecebimento = ObterData(linha, mapa.DataRecebimento);
            if (dataRecebimento is null)
            {
                AdicionarAlerta(alertas, linha.Numero,
                    "Valor recebido sem data; a linha não pode ser classificada por competência e não foi importada como recebimento.");
                continue;
            }

            var baseChave = $"{Normalizar(cliente)}|{Normalizar(notas)}|{dataRecebimento:yyyyMMdd}";
            ocorrenciasMovimento[baseChave] = ocorrenciasMovimento.GetValueOrDefault(baseChave) + 1;
            importacao.Movimentos.Add(new RecebimentoMovimento
            {
                ChaveNatural = GerarChave($"{empresaId}|{baseChave}|{ocorrenciasMovimento[baseChave]}"),
                Cliente = cliente,
                DataRecebimento = dataRecebimento.Value,
                ValorRecebido = ArredondarMoeda(valorRecebido.Value),
                Parcela = ObterIdentificador(linha, mapa.Parcela),
                NotasVinculadas = string.IsNullOrWhiteSpace(notas) ? null : notas,
                DataEmissaoReferencia = emissao,
                RecebidoAntesEmissao = emissao.HasValue && dataRecebimento.Value.Date < emissao.Value.Date,
                LinhaOrigem = linha.Numero
            });
        }

        if (documentosDoGrupo.Count > 1 && linhas.Count(x => ObterDecimal(x, mapa.ValorRecebido).HasValue) > 1)
        {
            var texto = $"{cliente}: existem vários documentos e vários recebimentos no mesmo grupo; o vínculo deverá ser conferido antes do Fortes.";
            mensagens.Add(texto);
            AdicionarAlerta(alertas, linhas[0].Numero, texto);
        }

        var totalFaturado = documentosDoGrupo.Sum(x => x.ValorFaturado);
        var totalRecebido = linhas.Sum(x => ObterDecimal(x, mapa.ValorRecebido) ?? 0);
        var diferenca = totalFaturado - totalRecebido;
        if (diferenca < -0.02m)
        {
            var texto = $"{cliente}: o valor recebido ({totalRecebido:C2}) supera o valor faturado ({totalFaturado:C2}); o valor recebido foi preservado para conferência.";
            mensagens.Add(texto);
            AdicionarAlerta(alertas, linhas[0].Numero, texto);
        }
    }

    private static MapeamentoColunas? LocalizarCabecalho(AbaLida aba)
    {
        foreach (var linha in aba.Linhas.Take(20))
        {
            var cabecalhos = linha.Celulas.ToDictionary(x => x.Key, x => Normalizar(x.Value));
            var data = Localizar(cabecalhos, "DATARECEBIMENTO", "DATADORECEBIMENTO");
            var cliente = Localizar(cabecalhos, "CLIENTE", "NOMEDOCLIENTE", "NOMECLIENTE");
            var faturado = Localizar(cabecalhos,
                "FATURADO", "VALORFATURADO", "VALORDANF", "VALORNF");
            var recebido = Localizar(cabecalhos, "VALORRECEBIDO");
            if (data >= 0 && cliente >= 0 && faturado >= 0 && recebido >= 0)
            {
                var nota = Localizar(cabecalhos,
                    "NOTA", "NºNOTA", "NNOTA", "NUMERODANOTA", "NUMERONOTA", "NUMERONF");
                return new MapeamentoColunas(
                    linha.Numero,
                    data,
                    cliente,
                    Localizar(cabecalhos, "EMISSAO", "EMISSAODANF", "EMISSAONF"),
                    faturado,
                    Localizar(cabecalhos, "DESCONTO"),
                    recebido,
                    Localizar(cabecalhos, "PARC"),
                    Localizar(cabecalhos, "ISS"),
                    Localizar(cabecalhos, "PIS"),
                    Localizar(cabecalhos, "COFINS"),
                    Localizar(cabecalhos, "IRPJ", "IR"),
                    Localizar(cabecalhos, "CSLL"),
                    Localizar(cabecalhos, "INSS"),
                    Localizar(cabecalhos, "OUTROS", "OUTROIMPOSTO", "OUTROSIMPOSTO", "OUTROSIMPOSTOS"),
                    Localizar(cabecalhos, "CODSERV"),
                    nota,
                    nota < 0 ? -1 : nota + 1,
                    nota < 0 ? -1 : nota + 2,
                    false);
            }

            var notaComercio = Localizar(cabecalhos, "NUMERODANOTA", "NUMERONOTA");
            var parcelaRecebida = Localizar(cabecalhos,
                "VALORDAPARCELARECEBIDA", "VALORPARCELARECEBIDA");
            var dataComercio = Localizar(cabecalhos, "DATADORECEBIMENTO", "DATARECEBIMENTO");
            if (notaComercio >= 0 && parcelaRecebida >= 0 && dataComercio >= 0)
            {
                return new MapeamentoColunas(
                    linha.Numero, dataComercio, -1, -1, -1, -1, parcelaRecebida,
                    -1, -1, -1, -1, -1, -1, -1, -1, -1, notaComercio, -1, -1, true);
            }
        }
        return null;
    }

    private static int Localizar(IReadOnlyDictionary<int, string> cabecalhos, params string[] nomes)
    {
        foreach (var item in cabecalhos)
            if (nomes.Contains(item.Value, StringComparer.OrdinalIgnoreCase))
                return item.Key;
        return -1;
    }

    private static IReadOnlyList<string> LerSharedStrings(ZipArchive zip)
    {
        var entrada = zip.GetEntry("xl/sharedStrings.xml");
        if (entrada is null)
            return [];
        using var stream = entrada.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(texto => texto.Value)))
            .ToArray();
    }

    private static IReadOnlyList<PlanilhaInfo> LerPlanilhas(ZipArchive zip)
    {
        var workbookEntry = zip.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("O arquivo não contém a estrutura workbook.xml.");
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("O arquivo não contém os relacionamentos das abas.");
        using var workbookStream = workbookEntry.Open();
        using var relsStream = relsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var rels = XDocument.Load(relsStream);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
        var destinos = rels.Descendants(packageRels + "Relationship")
            .ToDictionary(x => (string?)x.Attribute("Id") ?? "", x => (string?)x.Attribute("Target") ?? "");

        return workbook.Descendants(main + "sheet").Select(sheet =>
        {
            var nome = (string?)sheet.Attribute("name") ?? "Planilha";
            var relId = (string?)sheet.Attribute(officeRels + "id") ?? "";
            if (!destinos.TryGetValue(relId, out var alvo))
                throw new InvalidOperationException($"Não foi possível localizar a aba {nome}.");
            var caminho = alvo.Replace('\\', '/').TrimStart('/');
            if (!caminho.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                caminho = "xl/" + caminho;
            return new PlanilhaInfo(nome, caminho);
        }).ToArray();
    }

    private static AbaLida LerAba(ZipArchive zip, PlanilhaInfo planilha, IReadOnlyList<string> sharedStrings)
    {
        var entrada = zip.GetEntry(planilha.Caminho)
            ?? throw new InvalidOperationException($"A estrutura da aba {planilha.Nome} não foi encontrada.");
        using var stream = entrada.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var linhas = new List<LinhaAba>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var numero = (int?)row.Attribute("r") ?? linhas.Count + 1;
            var celulas = new Dictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var referencia = (string?)cell.Attribute("r") ?? "";
                var coluna = IndiceColuna(referencia);
                if (coluna < 0)
                    continue;
                var tipo = (string?)cell.Attribute("t");
                string valor;
                if (tipo == "inlineStr")
                    valor = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
                else
                {
                    valor = cell.Element(ns + "v")?.Value ?? "";
                    if (tipo == "s" && int.TryParse(valor, out var indice) && indice >= 0 && indice < sharedStrings.Count)
                        valor = sharedStrings[indice];
                }
                celulas[coluna] = valor;
            }
            linhas.Add(new LinhaAba(numero, celulas));
        }
        return new AbaLida(planilha.Nome, linhas);
    }

    private static int IndiceColuna(string referencia)
    {
        var resultado = 0;
        var encontrou = false;
        foreach (var caractere in referencia)
        {
            if (!char.IsLetter(caractere))
                break;
            encontrou = true;
            resultado = resultado * 26 + (char.ToUpperInvariant(caractere) - 'A' + 1);
        }
        return encontrou ? resultado - 1 : -1;
    }

    private static string NomeColuna(int indice)
    {
        var valor = indice + 1;
        var nome = "";
        while (valor > 0)
        {
            valor--;
            nome = (char)('A' + valor % 26) + nome;
            valor /= 26;
        }
        return nome;
    }

    private static string? ObterTexto(LinhaAba linha, int coluna) =>
        coluna >= 0 && linha.Celulas.TryGetValue(coluna, out var valor) && !string.IsNullOrWhiteSpace(valor)
            ? valor.Trim()
            : null;

    private static string? ObterIdentificador(LinhaAba linha, int coluna)
    {
        var valor = ObterTexto(linha, coluna);
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        if (decimal.TryParse(valor, NumberStyles.Any, Invariant, out var numero))
            return numero.ToString("0.############################", Invariant);
        return valor;
    }

    private static decimal? ObterDecimal(LinhaAba linha, int coluna)
    {
        var valor = ObterTexto(linha, coluna);
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        valor = valor.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (decimal.TryParse(valor, NumberStyles.Any, Invariant, out var numero))
            return numero;
        if (decimal.TryParse(valor, NumberStyles.Any, PtBr, out numero))
            return numero;
        return null;
    }

    private static DateTime? ObterData(LinhaAba linha, int coluna)
    {
        var valor = ObterTexto(linha, coluna);
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        if (double.TryParse(valor, NumberStyles.Any, Invariant, out var serial) && serial is > 1 and < 300000)
            return DateTime.FromOADate(serial).Date;
        if (DateTime.TryParse(valor, PtBr, DateTimeStyles.None, out var data) ||
            DateTime.TryParse(valor, Invariant, DateTimeStyles.None, out data))
            return data.Date;
        return null;
    }

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "";
        var decomposed = valor.Normalize(NormalizationForm.FormD);
        var semAcentos = new string(decomposed.Where(x =>
            CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray());
        return new string(semAcentos.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static string GerarChave(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    private static decimal ArredondarMoeda(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static void AdicionarAlerta(Dictionary<int, List<string>> alertas, int linha, string texto)
    {
        if (!alertas.TryGetValue(linha, out var itens))
        {
            itens = [];
            alertas[linha] = itens;
        }
        itens.Add(texto);
    }

    private sealed record PlanilhaInfo(string Nome, string Caminho);
    private sealed record AbaLida(string Nome, IReadOnlyList<LinhaAba> Linhas);
    private sealed record LinhaAba(int Numero, IReadOnlyDictionary<int, string> Celulas);
    private sealed record MapeamentoColunas(
        int LinhaCabecalho,
        int DataRecebimento,
        int Cliente,
        int Emissao,
        int Faturado,
        int Desconto,
        int ValorRecebido,
        int Parcela,
        int Iss,
        int Pis,
        int Cofins,
        int Ir,
        int Csll,
        int Inss,
        int Outros,
        int CodigoServico,
        int Nota,
        int IndicadorFederal,
        int IndicadorIss,
        bool Comercio);
}

public sealed record RecebimentosParseResult(
    RecebimentoImportacao Importacao,
    IReadOnlyList<RecebimentoCompetenciaImportada> Competencias,
    IReadOnlyList<string> Mensagens);
