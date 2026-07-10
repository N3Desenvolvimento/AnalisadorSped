using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace N3.AnalisadorFiscal.Web.Services;

public interface IGuiaIcmsPdfLeituraService
{
    decimal? LerValorPrincipal(byte[] pdf);
}

public sealed class GuiaIcmsPdfLeituraService : IGuiaIcmsPdfLeituraService
{
    private static readonly CultureInfo CulturaBrasil = new("pt-BR");

    public decimal? LerValorPrincipal(byte[] pdf)
    {
        var texto = ExtrairTexto(pdf);
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var normalizado = Regex.Replace(texto, @"\s+", " ");
        var padroes = new[]
        {
            @"valor\s*principal.{0,80}?((?:R\$\s*)?\d{1,3}(?:\.\d{3})*,\d{2})",
            @"principal.{0,80}?((?:R\$\s*)?\d{1,3}(?:\.\d{3})*,\d{2})",
            @"valor\s*do\s*documento.{0,160}?((?:R\$\s*)?\d{1,3}(?:\.\d{3})*,\d{2})",
            @"totalizadores.{0,80}?((?:R\$\s*)?\d{1,3}(?:\.\d{3})*,\d{2})"
        };

        foreach (var padrao in padroes)
        {
            var match = Regex.Match(normalizado, padrao, RegexOptions.IgnoreCase);
            if (match.Success && TryParseMoeda(match.Groups[1].Value, out var valor))
            {
                return valor;
            }
        }

        return null;
    }

    private static string ExtrairTexto(byte[] pdf)
    {
        var textoPdf = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        var posicao = 0;

        while (true)
        {
            var inicio = textoPdf.IndexOf("stream", posicao, StringComparison.Ordinal);
            if (inicio < 0)
            {
                break;
            }

            inicio += "stream".Length;
            if (inicio < textoPdf.Length && textoPdf[inicio] == '\r')
            {
                inicio++;
            }
            if (inicio < textoPdf.Length && textoPdf[inicio] == '\n')
            {
                inicio++;
            }

            var fim = textoPdf.IndexOf("endstream", inicio, StringComparison.Ordinal);
            if (fim < 0)
            {
                break;
            }

            var streamBytes = pdf.AsSpan(inicio, fim - inicio).ToArray();
            var trechoObjeto = textoPdf[Math.Max(0, posicao)..inicio];
            var conteudo = trechoObjeto.Contains("/FlateDecode", StringComparison.Ordinal)
                ? Descomprimir(streamBytes)
                : Encoding.Latin1.GetString(streamBytes);

            sb.Append(' ').Append(ExtrairTextoDoStream(conteudo));
            posicao = fim + "endstream".Length;
        }

        return sb.ToString();
    }

    private static string Descomprimir(byte[] dados)
    {
        try
        {
            using var origem = new MemoryStream(dados);
            using var zlib = new ZLibStream(origem, CompressionMode.Decompress);
            using var destino = new MemoryStream();
            zlib.CopyTo(destino);
            return Encoding.Latin1.GetString(destino.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtrairTextoDoStream(string conteudo)
    {
        var sb = new StringBuilder();

        foreach (Match match in Regex.Matches(conteudo, @"\((?:\\.|[^\\)])*\)"))
        {
            sb.Append(' ').Append(DecodificarTextoPdf(match.Value[1..^1]));
        }

        foreach (Match match in Regex.Matches(conteudo, @"<([0-9A-Fa-f]{4,})>"))
        {
            sb.Append(' ').Append(DecodificarHex(match.Groups[1].Value));
        }

        return sb.ToString();
    }

    private static string DecodificarTextoPdf(string texto)
    {
        return texto
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal);
    }

    private static string DecodificarHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        if (bytes.Length > 1 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static bool TryParseMoeda(string texto, out decimal valor)
    {
        texto = texto.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(texto, NumberStyles.Number, CulturaBrasil, out valor);
    }
}
