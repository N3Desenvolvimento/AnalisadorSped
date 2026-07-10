using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace N3.AnalisadorFiscal.Web.Services;

public interface ICfopExcelService
{
    byte[] Gerar(string nomeAba, IReadOnlyList<string> colunas, IReadOnlyList<IReadOnlyList<object?>> linhas);
}

public sealed class CfopExcelService : ICfopExcelService
{
    public byte[] Gerar(string nomeAba, IReadOnlyList<string> colunas, IReadOnlyList<IReadOnlyList<object?>> linhas)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AdicionarEntrada(archive, "[Content_Types].xml", ContentTypesXml());
            AdicionarEntrada(archive, "_rels/.rels", RootRelsXml());
            AdicionarEntrada(archive, "xl/workbook.xml", WorkbookXml(nomeAba));
            AdicionarEntrada(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AdicionarEntrada(archive, "xl/styles.xml", StylesXml());
            AdicionarEntrada(archive, "xl/worksheets/sheet1.xml", WorksheetXml(colunas, linhas));
        }

        return stream.ToArray();
    }

    private static string WorksheetXml(IReadOnlyList<string> colunas, IReadOnlyList<IReadOnlyList<object?>> linhas)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cols>
            """);

        for (var i = 1; i <= colunas.Count; i++)
        {
            var largura = i == 1 ? 26 : 18;
            sb.Append(CultureInfo.InvariantCulture, $"<col min=\"{i}\" max=\"{i}\" width=\"{largura}\" customWidth=\"1\"/>");
        }

        sb.Append("""
              </cols>
              <sheetData>
            """);

        sb.Append("<row r=\"1\">");
        for (var i = 0; i < colunas.Count; i++)
        {
            AdicionarCelulaTexto(sb, NomeColuna(i + 1), 1, colunas[i], 1);
        }
        sb.Append("</row>");

        var linhaNumero = 2;
        foreach (var linha in linhas)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{linhaNumero}\">");
            for (var i = 0; i < colunas.Count; i++)
            {
                var coluna = NomeColuna(i + 1);
                var valor = i < linha.Count ? linha[i] : null;
                AdicionarCelula(sb, coluna, linhaNumero, valor);
            }
            sb.Append("</row>");
            linhaNumero++;
        }

        var ultimaColuna = NomeColuna(colunas.Count);
        sb.Append(CultureInfo.InvariantCulture, $"""
              </sheetData>
              <autoFilter ref="A1:{ultimaColuna}1"/>
              <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
            </worksheet>
            """);

        return sb.ToString();
    }

    private static void AdicionarCelula(StringBuilder sb, string coluna, int linha, object? valor)
    {
        switch (valor)
        {
            case decimal decimalValor:
                AdicionarCelulaNumero(sb, coluna, linha, decimalValor, 2);
                break;
            case int intValor:
                AdicionarCelulaNumero(sb, coluna, linha, intValor, 0);
                break;
            default:
                AdicionarCelulaTexto(sb, coluna, linha, valor?.ToString() ?? string.Empty);
                break;
        }
    }

    private static void AdicionarCelulaTexto(StringBuilder sb, string coluna, int linha, string valor, int estilo = 0)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{coluna}{linha}\" t=\"inlineStr\" s=\"{estilo}\"><is><t>{EscaparXml(valor)}</t></is></c>");
    }

    private static void AdicionarCelulaNumero(StringBuilder sb, string coluna, int linha, decimal valor, int estilo)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{coluna}{linha}\" s=\"{estilo}\"><v>{valor.ToString(CultureInfo.InvariantCulture)}</v></c>");
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

    private static string WorkbookXml(string nomeAba) => $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="{{EscaparXml(NormalizarNomeAba(nomeAba))}}" sheetId="1" r:id="rId1"/>
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
          <numFmts count="1"><numFmt numFmtId="164" formatCode="&quot;R$&quot; #,##0.00"/></numFmts>
          <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs>
        </styleSheet>
        """;

    private static string NormalizarNomeAba(string nome)
    {
        var invalido = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var limpo = new string((nome.Length == 0 ? "Planilha" : nome)
            .Select(caractere => invalido.Contains(caractere) ? ' ' : caractere)
            .ToArray())
            .Trim();

        return limpo.Length > 31 ? limpo[..31] : limpo;
    }
}
