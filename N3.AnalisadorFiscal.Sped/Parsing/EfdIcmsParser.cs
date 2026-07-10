using System.Globalization;
using System.Text;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Sped.Parsing;

public sealed class EfdIcmsParser
{
    private static readonly CultureInfo BrazilianCulture = new("pt-BR");

    public EfdIcmsParseResult Parse(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public async Task<EfdIcmsParseResult> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await ParseAsync(reader, cancellationToken);
    }

    public EfdIcmsParseResult Parse(TextReader reader)
    {
        var result = new EfdIcmsParseResult();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            ParseLine(line, result);
        }

        return result;
    }

    public async Task<EfdIcmsParseResult> ParseAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var result = new EfdIcmsParseResult();
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            ParseLine(line, result);
        }

        return result;
    }

    private void ParseLine(string line, EfdIcmsParseResult result)
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
                AddRecord(result, "0150", result.Participantes, Parse0150(fields));
                break;
            case "0190":
                AddRecord(result, "0190", result.UnidadesMedida, Parse0190(fields));
                break;
            case "0200":
                AddRecord(result, "0200", result.Produtos, Parse0200(fields));
                break;
            case "C100":
                AddRecord(result, "C100", result.C100, ParseC100(fields));
                break;
            case "C170":
                AddRecord(result, "C170", result.C170, ParseC170(fields));
                break;
            case "C190":
                AddRecord(result, "C190", result.C190, ParseC190(fields));
                break;
            case "E110":
                AddRecord(result, "E110", result.E110, ParseE110(fields));
                break;
            case "E111":
                AddRecord(result, "E111", result.E111, ParseE111(fields));
                break;
        }
    }

    private static void AddRecord<T>(EfdIcmsParseResult result, string tipo, List<T> registros, T entidade)
    {
        registros.Add(entidade);
        result.Registros.Add(new EfdIcmsParsedRecord(tipo, entidade!));
    }

    private static (Empresa Empresa, SpedArquivo Arquivo) Parse0000(string[] fields)
    {
        var empresa = new Empresa
        {
            RazaoSocial = Get(fields, 6),
            Cnpj = Get(fields, 7),
            Uf = Get(fields, 9),
            InscricaoEstadual = Get(fields, 10),
            CodigoMunicipio = Get(fields, 11)
        };

        var arquivo = new SpedArquivo
        {
            VersaoLeiaute = Get(fields, 2),
            FinalidadeArquivo = Get(fields, 3),
            PeriodoInicial = ParseDate(Get(fields, 4)) ?? default,
            PeriodoFinal = ParseDate(Get(fields, 5)) ?? default
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

    private static SpedC100 ParseC100(string[] fields)
    {
        return new SpedC100
        {
            IndicadorOperacao = Get(fields, 2),
            IndicadorEmitente = Get(fields, 3),
            CodigoParticipante = Get(fields, 4),
            CodigoModelo = Get(fields, 5),
            CodigoSituacao = Get(fields, 6),
            Serie = Get(fields, 7),
            NumeroDocumento = Get(fields, 8),
            ChaveNfe = Get(fields, 9),
            DataDocumento = ParseDate(Get(fields, 10)),
            DataEntradaSaida = ParseDate(Get(fields, 11)),
            ValorDocumento = ParseDecimal(Get(fields, 12)) ?? 0m,
            ValorDesconto = ParseDecimal(Get(fields, 14)) ?? 0m,
            ValorMercadoria = ParseDecimal(Get(fields, 16)) ?? 0m,
            ValorFrete = ParseDecimal(Get(fields, 18)) ?? 0m,
            ValorSeguro = ParseDecimal(Get(fields, 19)) ?? 0m,
            ValorOutrasDespesas = ParseDecimal(Get(fields, 20)) ?? 0m,
            ValorIcms = ParseDecimal(Get(fields, 22)) ?? 0m,
            ValorIcmsSt = ParseDecimal(Get(fields, 24)) ?? 0m,
            ValorIpi = ParseDecimal(Get(fields, 25)) ?? 0m,
            ValorPis = ParseDecimal(Get(fields, 26)) ?? 0m,
            ValorCofins = ParseDecimal(Get(fields, 27)) ?? 0m
        };
    }

    private static SpedC170 ParseC170(string[] fields)
    {
        return new SpedC170
        {
            NumeroItem = Get(fields, 2),
            CodigoItem = Get(fields, 3),
            DescricaoComplementar = Get(fields, 4),
            Quantidade = ParseDecimal(Get(fields, 5)) ?? 0m,
            Unidade = Get(fields, 6),
            ValorItem = ParseDecimal(Get(fields, 7)) ?? 0m,
            ValorDesconto = ParseDecimal(Get(fields, 8)) ?? 0m,
            CstIcms = Get(fields, 10),
            Cfop = Get(fields, 11),
            NaturezaBcIcms = Get(fields, 12),
            ValorBcIcms = ParseDecimal(Get(fields, 13)) ?? 0m,
            AliquotaIcms = ParseDecimal(Get(fields, 14)) ?? 0m,
            ValorIcms = ParseDecimal(Get(fields, 15)) ?? 0m,
            ValorBcIcmsSt = ParseDecimal(Get(fields, 16)) ?? 0m,
            AliquotaIcmsSt = ParseDecimal(Get(fields, 17)) ?? 0m,
            ValorIcmsSt = ParseDecimal(Get(fields, 18)) ?? 0m,
            CstIpi = Get(fields, 20),
            ValorBcIpi = ParseDecimal(Get(fields, 22)) ?? 0m,
            AliquotaIpi = ParseDecimal(Get(fields, 23)) ?? 0m,
            ValorIpi = ParseDecimal(Get(fields, 24)) ?? 0m
        };
    }

    private static SpedC190 ParseC190(string[] fields)
    {
        return new SpedC190
        {
            CstIcms = Get(fields, 2),
            Cfop = Get(fields, 3),
            AliquotaIcms = ParseDecimal(Get(fields, 4)) ?? 0m,
            ValorOperacao = ParseDecimal(Get(fields, 5)) ?? 0m,
            ValorBcIcms = ParseDecimal(Get(fields, 6)) ?? 0m,
            ValorIcms = ParseDecimal(Get(fields, 7)) ?? 0m,
            ValorBcIcmsSt = ParseDecimal(Get(fields, 8)) ?? 0m,
            ValorIcmsSt = ParseDecimal(Get(fields, 9)) ?? 0m,
            ValorReducaoBcIcms = ParseDecimal(Get(fields, 10)) ?? 0m,
            ValorIpi = ParseDecimal(Get(fields, 11)) ?? 0m,
            CodigoObservacao = Get(fields, 12)
        };
    }

    private static SpedE110 ParseE110(string[] fields)
    {
        return new SpedE110
        {
            ValorTotalDebitos = ParseDecimal(Get(fields, 2)) ?? 0m,
            ValorAjustesDebitos = ParseDecimal(Get(fields, 3)) ?? 0m,
            ValorTotalAjustesDebitos = ParseDecimal(Get(fields, 4)) ?? 0m,
            ValorEstornosCreditos = ParseDecimal(Get(fields, 5)) ?? 0m,
            ValorTotalCreditos = ParseDecimal(Get(fields, 6)) ?? 0m,
            ValorAjustesCreditos = ParseDecimal(Get(fields, 7)) ?? 0m,
            ValorTotalAjustesCreditos = ParseDecimal(Get(fields, 8)) ?? 0m,
            ValorEstornosDebitos = ParseDecimal(Get(fields, 9)) ?? 0m,
            ValorSaldoCredorAnterior = ParseDecimal(Get(fields, 10)) ?? 0m,
            ValorSaldoDevedor = ParseDecimal(Get(fields, 11)) ?? 0m,
            ValorDeducoes = ParseDecimal(Get(fields, 12)) ?? 0m,
            ValorIcmsRecolher = ParseDecimal(Get(fields, 13)) ?? 0m,
            ValorSaldoCredorTransportar = ParseDecimal(Get(fields, 14)) ?? 0m,
            ValorExtraApuracao = ParseDecimal(Get(fields, 15)) ?? 0m
        };
    }

    private static SpedE111 ParseE111(string[] fields)
    {
        return new SpedE111
        {
            CodigoAjusteApuracao = Get(fields, 2),
            DescricaoComplementar = Get(fields, 3),
            ValorAjuste = ParseDecimal(Get(fields, 4)) ?? 0m
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

        return decimal.TryParse(value, NumberStyles.Number, BrazilianCulture, out var number)
            ? number
            : null;
    }
}

