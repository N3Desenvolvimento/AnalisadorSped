using System.Globalization;
using System.IO.Compression;
using System.Text;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Web.Services;

public interface INotasEntradaExcelService
{
    byte[] Gerar(IReadOnlyList<DashboardNotaEntradaFornecedorDto> notas);
    byte[] GerarNfse(IReadOnlyList<IssNotaDto> notas);
    byte[] GerarAnaliseProdutos(IReadOnlyList<PreAnaliseProdutoExcelDto> produtos);
}

public sealed class NotasEntradaExcelService : INotasEntradaExcelService
{
    private static readonly CultureInfo CulturaBrasil = new("pt-BR");

    public byte[] Gerar(IReadOnlyList<DashboardNotaEntradaFornecedorDto> notas)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AdicionarEntrada(archive, "[Content_Types].xml", ContentTypesXml());
            AdicionarEntrada(archive, "_rels/.rels", RootRelsXml());
            AdicionarEntrada(archive, "xl/workbook.xml", WorkbookXml());
            AdicionarEntrada(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AdicionarEntrada(archive, "xl/styles.xml", StylesXml());
            AdicionarEntrada(archive, "xl/worksheets/sheet1.xml", WorksheetXml(notas));
        }

        return stream.ToArray();
    }

    public byte[] GerarNfse(IReadOnlyList<IssNotaDto> notas)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AdicionarEntrada(archive, "[Content_Types].xml", ContentTypesXml());
            AdicionarEntrada(archive, "_rels/.rels", RootRelsXml());
            AdicionarEntrada(archive, "xl/workbook.xml", WorkbookXml("NFS-e"));
            AdicionarEntrada(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AdicionarEntrada(archive, "xl/styles.xml", StylesXml());
            AdicionarEntrada(archive, "xl/worksheets/sheet1.xml", WorksheetNfseXml(notas));
        }

        return stream.ToArray();
    }

    public byte[] GerarAnaliseProdutos(IReadOnlyList<PreAnaliseProdutoExcelDto> produtos)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AdicionarEntrada(archive, "[Content_Types].xml", ContentTypesXml());
            AdicionarEntrada(archive, "_rels/.rels", RootRelsXml());
            AdicionarEntrada(archive, "xl/workbook.xml", WorkbookXml("Análise de produtos"));
            AdicionarEntrada(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AdicionarEntrada(archive, "xl/styles.xml", StylesXml());
            AdicionarEntrada(archive, "xl/worksheets/sheet1.xml", WorksheetAnaliseProdutosXml(produtos));
        }

        return stream.ToArray();
    }

    private static string WorksheetAnaliseProdutosXml(IReadOnlyList<PreAnaliseProdutoExcelDto> produtos)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
              <cols>
                <col min="1" max="1" width="52" customWidth="1"/>
                <col min="2" max="2" width="14" customWidth="1"/>
                <col min="3" max="4" width="18" customWidth="1"/>
                <col min="5" max="5" width="22" customWidth="1"/>
                <col min="6" max="6" width="42" customWidth="1"/>
                <col min="7" max="7" width="18" customWidth="1"/>
                <col min="8" max="8" width="48" customWidth="1"/>
              </cols>
              <sheetData>
            """);

        AdicionarLinhaTexto(sb, 1, true,
            "Produto", "NCM", "Valor total", "ICMS creditado", "Situação da análise", "Fornecedor", "Número da nota", "Chave");

        var linha = 2;
        foreach (var produto in produtos)
        {
            sb.Append($"<row r=\"{linha}\">");
            AdicionarCelulaTexto(sb, "A", linha, produto.Produto);
            AdicionarCelulaTexto(sb, "B", linha, produto.Ncm);
            AdicionarCelulaNumero(sb, "C", linha, produto.ValorTotal);
            AdicionarCelulaNumero(sb, "D", linha, produto.IcmsCreditado);
            AdicionarCelulaTexto(sb, "E", linha, FormatarSituacaoAnalise(produto.SituacaoAnalise));
            AdicionarCelulaTexto(sb, "F", linha, produto.Fornecedor);
            AdicionarCelulaTexto(sb, "G", linha, produto.NumeroNota);
            AdicionarCelulaTexto(sb, "H", linha, produto.ChaveNfe);
            sb.Append("</row>");
            linha++;
        }

        sb.Append($"""
              </sheetData>
              <autoFilter ref="A1:H{Math.Max(1, linha - 1)}"/>
              <pageMargins left="0.4" right="0.4" top="0.6" bottom="0.6" header="0.2" footer="0.2"/>
            </worksheet>
            """);
        return sb.ToString();
    }

    private static string FormatarSituacaoAnalise(string? situacao) => situacao switch
    {
        "SEM_CREDITO_ST" => "Sem crédito · ST",
        "SEM_CREDITO" => "Sem crédito",
        "CREDITO_PERMITIDO" => "Crédito permitido",
        "REVISAR" => "Revisar",
        "DIVERGENTE" => "Divergente",
        null or "" => "Pendente",
        _ => situacao.Replace('_', ' ')
    };

    private static string WorksheetNfseXml(IReadOnlyList<IssNotaDto> notas)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cols>
                <col min="1" max="3" width="16" customWidth="1"/>
                <col min="4" max="5" width="38" customWidth="1"/>
                <col min="6" max="6" width="16" customWidth="1"/>
                <col min="7" max="14" width="17" customWidth="1"/>
                <col min="15" max="15" width="54" customWidth="1"/>
              </cols>
              <sheetData>
            """);

        AdicionarLinhaTexto(sb, 1, true, "Número", "Emissão", "Tipo", "Prestador", "Tomador", "Serviço",
            "Valor dos serviços", "ISS retido", "PIS retido", "Cofins retida", "IRRF retido", "CSLL retida",
            "INSS retido", "Total retido", "Chave de acesso");

        var linha = 2;
        foreach (var nota in notas)
        {
            sb.Append($"<row r=\"{linha}\">");
            AdicionarCelulaTexto(sb, "A", linha, nota.NumeroNota ?? string.Empty);
            AdicionarCelulaTexto(sb, "B", linha, FormatarData(nota.DataEmissao));
            AdicionarCelulaTexto(sb, "C", linha, nota.Tipo);
            AdicionarCelulaTexto(sb, "D", linha, nota.NomePrestador ?? nota.CnpjPrestador);
            AdicionarCelulaTexto(sb, "E", linha, nota.NomeTomador ?? nota.CnpjTomador ?? string.Empty);
            AdicionarCelulaTexto(sb, "F", linha, nota.CodigoServico ?? string.Empty);
            AdicionarCelulaNumero(sb, "G", linha, nota.ValorServicos);
            AdicionarCelulaNumero(sb, "H", linha, nota.IssRetido ? nota.ValorIss : 0);
            AdicionarCelulaNumero(sb, "I", linha, nota.ValorPisRetido);
            AdicionarCelulaNumero(sb, "J", linha, nota.ValorCofinsRetido);
            AdicionarCelulaNumero(sb, "K", linha, nota.ValorIrrfRetido);
            AdicionarCelulaNumero(sb, "L", linha, nota.ValorCsllRetido);
            AdicionarCelulaNumero(sb, "M", linha, nota.ValorInssRetido);
            AdicionarCelulaNumero(sb, "N", linha, nota.TotalRetido);
            AdicionarCelulaTexto(sb, "O", linha, nota.ChaveAcesso);
            sb.Append("</row>");
            linha++;
        }

        sb.Append("""
              </sheetData>
              <autoFilter ref="A1:O1"/>
              <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
            </worksheet>
            """);
        return sb.ToString();
    }

    private static string WorksheetXml(IReadOnlyList<DashboardNotaEntradaFornecedorDto> notas)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <cols>
                <col min="1" max="1" width="14" customWidth="1"/>
                <col min="2" max="2" width="18" customWidth="1"/>
                <col min="3" max="3" width="42" customWidth="1"/>
                <col min="4" max="4" width="20" customWidth="1"/>
                <col min="5" max="6" width="12" customWidth="1"/>
                <col min="7" max="7" width="16" customWidth="1"/>
                <col min="8" max="8" width="12" customWidth="1"/>
                <col min="9" max="9" width="48" customWidth="1"/>
                <col min="10" max="14" width="16" customWidth="1"/>
              </cols>
              <sheetData>
            """);

        AdicionarLinhaTexto(sb, 1, true,
            "Data entrada", "Fornecedor", "Nome", "CNPJ", "Modelo", "Serie", "Numero", "Situacao", "Chave NFe", "Valor nota", "Valor mercadoria", "Base ICMS", "ICMS credito", "Aliquota efetiva %");

        var linha = 2;
        foreach (var nota in notas)
        {
            sb.Append($"<row r=\"{linha}\">");
            AdicionarCelulaTexto(sb, "A", linha, FormatarData(nota.DataEntradaSaida ?? nota.DataDocumento));
            AdicionarCelulaTexto(sb, "B", linha, nota.CodigoParticipante);
            AdicionarCelulaTexto(sb, "C", linha, nota.NomeParticipante);
            AdicionarCelulaTexto(sb, "D", linha, FormatarCnpj(nota.Cnpj));
            AdicionarCelulaTexto(sb, "E", linha, nota.CodigoModelo);
            AdicionarCelulaTexto(sb, "F", linha, nota.Serie);
            AdicionarCelulaTexto(sb, "G", linha, nota.NumeroDocumento);
            AdicionarCelulaTexto(sb, "H", linha, nota.CodigoSituacao);
            AdicionarCelulaTexto(sb, "I", linha, nota.ChaveNfe ?? string.Empty);
            AdicionarCelulaNumero(sb, "J", linha, nota.ValorDocumento);
            AdicionarCelulaNumero(sb, "K", linha, nota.ValorMercadoria);
            AdicionarCelulaNumero(sb, "L", linha, nota.ValorBaseIcms);
            AdicionarCelulaNumero(sb, "M", linha, nota.ValorIcmsCredito);
            AdicionarCelulaNumero(sb, "N", linha, nota.AliquotaEfetivaCredito);
            sb.Append("</row>");
            linha++;
        }

        sb.Append("""
              </sheetData>
              <autoFilter ref="A1:N1"/>
              <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
            </worksheet>
            """);

        return sb.ToString();
    }

    private static void AdicionarLinhaTexto(StringBuilder sb, int linha, bool cabecalho, params string[] valores)
    {
        sb.Append($"<row r=\"{linha}\">");
        for (var i = 0; i < valores.Length; i++)
        {
            var coluna = NomeColuna(i + 1);
            AdicionarCelulaTexto(sb, coluna, linha, valores[i], cabecalho ? 1 : 0);
        }
        sb.Append("</row>");
    }

    private static void AdicionarCelulaTexto(StringBuilder sb, string coluna, int linha, string valor, int estilo = 0)
    {
        sb.Append($"<c r=\"{coluna}{linha}\" t=\"inlineStr\" s=\"{estilo}\"><is><t>{EscaparXml(valor)}</t></is></c>");
    }

    private static void AdicionarCelulaNumero(StringBuilder sb, string coluna, int linha, decimal valor)
    {
        sb.Append($"<c r=\"{coluna}{linha}\" s=\"2\"><v>{valor.ToString(CultureInfo.InvariantCulture)}</v></c>");
    }

    private static string NomeColuna(int numero)
    {
        var nome = string.Empty;
        while (numero > 0)
        {
            var modulo = (numero - 1) % 26;
            nome = (char)('A' + modulo) + nome;
            numero = (numero - modulo) / 26;
        }

        return nome;
    }

    private static string FormatarData(DateTime? data)
    {
        return data?.ToString("dd/MM/yyyy", CulturaBrasil) ?? string.Empty;
    }

    private static string FormatarCnpj(string? cnpj)
    {
        var digitos = SomenteDigitos(cnpj);
        if (digitos.Length != 14)
        {
            return string.Empty;
        }

        return $"{digitos[..2]}.{digitos.Substring(2, 3)}.{digitos.Substring(5, 3)}/{digitos.Substring(8, 4)}-{digitos.Substring(12, 2)}";
    }

    private static string SomenteDigitos(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? string.Empty : new string(valor.Where(char.IsDigit).ToArray());
    }

    private static string EscaparXml(string? valor)
    {
        return (valor ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static void AdicionarEntrada(ZipArchive archive, string nome, string conteudo)
    {
        var entry = archive.CreateEntry(nome, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(conteudo.TrimStart());
    }

    private static string ContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private static string RootRelsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml(string nomePlanilha = "Notas de Entrada") => $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="{{EscaparXml(nomePlanilha)}}" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
        </styleSheet>
        """;
}
