using System.Globalization;
using System.IO.Compression;
using System.Text;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Web.Services;

public interface INotasEntradaExcelService
{
    byte[] Gerar(IReadOnlyList<DashboardNotaEntradaFornecedorDto> notas);
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
            "Data", "Fornecedor", "Nome", "CNPJ", "Modelo", "Serie", "Numero", "Situacao", "Chave NFe", "Valor nota", "Valor mercadoria", "Base ICMS", "ICMS credito", "Aliquota efetiva %");

        var linha = 2;
        foreach (var nota in notas)
        {
            sb.Append($"<row r=\"{linha}\">");
            AdicionarCelulaTexto(sb, "A", linha, FormatarData(nota.DataDocumento));
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

    private static string WorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Notas de Entrada" sheetId="1" r:id="rId1"/>
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
