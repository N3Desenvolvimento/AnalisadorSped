using System.Globalization;
using System.Text;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Sped.Parsing;

public sealed class EfdContribuicoesParser
{
    private static readonly CultureInfo CulturaBrasil = new("pt-BR");

    public EfdContribuicoesParseResult Parse(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public EfdContribuicoesParseResult Parse(TextReader reader)
    {
        var result = new EfdContribuicoesParseResult();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            ParseLine(line, result);
        }

        return result;
    }

    private static void ParseLine(string line, EfdContribuicoesParseResult result)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var fields = line.Split('|');
        var register = Get(fields, 1);

        switch (register)
        {
            case "0000":
                var (empresa, arquivo) = Parse0000(fields);
                result.Empresa = empresa;
                result.Arquivo = arquivo;
                break;
            case "0150":
                Add(result, result.Participantes, Parse0150(fields));
                break;
            case "0190":
                Add(result, result.UnidadesMedida, Parse0190(fields));
                break;
            case "0200":
                Add(result, result.Produtos, Parse0200(fields));
                break;
            case "C100":
            case "C170":
            case "C175":
            case "A100":
            case "A170":
            case "M100":
            case "M200":
            case "M500":
            case "M600":
                var registroContribuicoes = new EfdContribuicoesRegistro { Codigo = register, Campos = fields };
                result.RegistrosContribuicoes.Add(registroContribuicoes);
                result.Registros.Add(registroContribuicoes);
                break;
        }
    }

    private static void Add<T>(EfdContribuicoesParseResult result, List<T> list, T item)
    {
        list.Add(item);
        result.Registros.Add(item!);
    }

    private static (Empresa Empresa, SpedArquivo Arquivo) Parse0000(string[] fields)
    {
        var empresa = new Empresa
        {
            RazaoSocial = Get(fields, 8),
            Cnpj = Get(fields, 9),
            Uf = Get(fields, 10),
            CodigoMunicipio = Get(fields, 11),
            InscricaoEstadual = string.Empty
        };

        var arquivo = new SpedArquivo
        {
            VersaoLeiaute = Get(fields, 2),
            FinalidadeArquivo = Get(fields, 3),
            PeriodoInicial = ParseDate(Get(fields, 6)) ?? default,
            PeriodoFinal = ParseDate(Get(fields, 7)) ?? default
        };

        return (empresa, arquivo);
    }

    private static Participante Parse0150(string[] fields)
    {
        return new Participante
        {
            Codigo = Get(fields, 2),
            Nome = Get(fields, 3),
            CodigoPais = Get(fields, 4),
            Cnpj = Get(fields, 5),
            Cpf = Get(fields, 6),
            InscricaoEstadual = Get(fields, 7),
            CodigoMunicipio = Get(fields, 8),
            Suframa = Get(fields, 9),
            Endereco = Get(fields, 10),
            Numero = Get(fields, 11),
            Complemento = Get(fields, 12),
            Bairro = Get(fields, 13)
        };
    }

    private static UnidadeMedida Parse0190(string[] fields)
    {
        return new UnidadeMedida
        {
            Codigo = Get(fields, 2),
            Descricao = Get(fields, 3)
        };
    }

    private static Produto Parse0200(string[] fields)
    {
        return new Produto
        {
            Codigo = Get(fields, 2),
            Descricao = Get(fields, 3),
            CodigoBarra = Get(fields, 4),
            CodigoAnterior = Get(fields, 5),
            Unidade = Get(fields, 6),
            TipoItem = Get(fields, 7),
            CodigoNcm = Get(fields, 8),
            ExIpi = Get(fields, 9),
            CodigoGenero = Get(fields, 10),
            CodigoLst = Get(fields, 11),
            AliquotaIcms = ParseDecimal(Get(fields, 12))
        };
    }

    private static string Get(string[] fields, int index)
    {
        return index < fields.Length ? fields[index].Trim() : string.Empty;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CulturaBrasil, out var number)
            ? number
            : null;
    }
}
