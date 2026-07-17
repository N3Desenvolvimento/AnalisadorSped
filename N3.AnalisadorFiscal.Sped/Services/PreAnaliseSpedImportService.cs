using System.Security.Cryptography;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Domain.Entities;
using N3.AnalisadorFiscal.Sped.Parsing;

namespace N3.AnalisadorFiscal.Sped.Services;

public interface IPreAnaliseSpedImportService
{
    Task<PreAnaliseSpedImportacaoResultado> ImportarAsync(string caminhoArquivo,
        CancellationToken cancellationToken = default);
}

public sealed class PreAnaliseSpedImportService : IPreAnaliseSpedImportService
{
    private readonly EfdIcmsParser _parser;
    private readonly IPreAnaliseSpedRepository _repository;
    private readonly IPreAnaliseItemRuleService _ruleService;

    public PreAnaliseSpedImportService(EfdIcmsParser parser, IPreAnaliseSpedRepository repository,
        IPreAnaliseItemRuleService ruleService)
    {
        _parser = parser;
        _repository = repository;
        _ruleService = ruleService;
    }

    public async Task<PreAnaliseSpedImportacaoResultado> ImportarAsync(string caminhoArquivo,
        CancellationToken cancellationToken = default)
    {
        var parse = _parser.Parse(caminhoArquivo);
        if (parse.Empresa is null || parse.Arquivo is null)
            throw new InvalidOperationException("Registro 0000 não encontrado no arquivo SPED Fiscal.");

        var participantes = parse.Participantes
            .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var produtos = parse.Produtos
            .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        var dados = new PreAnaliseSpedDados
        {
            NomeArquivo = Path.GetFileName(caminhoArquivo),
            HashArquivo = await CalcularHashAsync(caminhoArquivo, cancellationToken),
            Empresa = parse.Empresa,
            Arquivo = parse.Arquivo
        };

        PreAnaliseSpedNotaDados? notaAtual = null;
        foreach (var registro in parse.Registros)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (registro.Entidade)
            {
                case SpedC100 nota:
                    notaAtual = null;
                    if (nota.IndicadorOperacao != "0") break;
                    participantes.TryGetValue(nota.CodigoParticipante, out var participante);
                    notaAtual = new PreAnaliseSpedNotaDados
                    {
                        Nota = nota,
                        NomeParticipante = participante?.Nome ?? "(Participante não informado)",
                        CnpjParticipante = participante?.Cnpj ?? participante?.Cpf ?? string.Empty
                    };
                    dados.Notas.Add(notaAtual);
                    break;
                case SpedC170 item when notaAtual is not null:
                    produtos.TryGetValue(item.CodigoItem, out var produto);
                    notaAtual.Itens.Add(new PreAnaliseSpedItemDados
                    {
                        Item = item,
                        Descricao = produto?.Descricao ?? item.DescricaoComplementar ?? item.CodigoItem,
                        Ncm = produto?.CodigoNcm
                    });
                    break;
                case SpedC190 analitico when notaAtual is not null:
                    notaAtual.ValorBaseIcms += analitico.ValorBcIcms;
                    notaAtual.ValorIcmsCredito += analitico.ValorIcms;
                    break;
            }
        }

        var regras = await _repository.GetRegrasAtivasAsync(
            dados.Empresa.Uf, dados.Arquivo.PeriodoFinal, cancellationToken);
        _ruleService.Aplicar(dados, regras);

        var id = await _repository.SalvarAsync(dados, cancellationToken);
        return new PreAnaliseSpedImportacaoResultado
        {
            PreAnaliseSpedId = id,
            NomeArquivo = dados.NomeArquivo,
            RazaoSocial = dados.Empresa.RazaoSocial,
            Cnpj = dados.Empresa.Cnpj,
            PeriodoInicial = dados.Arquivo.PeriodoInicial,
            PeriodoFinal = dados.Arquivo.PeriodoFinal,
            TotalNotas = dados.Notas.Count,
            TotalItens = dados.Notas.Sum(x => x.Itens.Count)
        };
    }

    private static async Task<string> CalcularHashAsync(string caminhoArquivo, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(caminhoArquivo);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}

public sealed class PreAnaliseSpedImportacaoResultado
{
    public int PreAnaliseSpedId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public DateTime PeriodoInicial { get; set; }
    public DateTime PeriodoFinal { get; set; }
    public int TotalNotas { get; set; }
    public int TotalItens { get; set; }
}
