using System.Security.Cryptography;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Domain.Entities;
using N3.AnalisadorFiscal.Sped.Parsing;

namespace N3.AnalisadorFiscal.Sped.Services;

public sealed class EfdContribuicoesImportService : IEfdContribuicoesImportService
{
    private const string TipoArquivo = "EFD_CONTRIBUICOES";

    private readonly EfdContribuicoesParser _parser;
    private readonly IEmpresaRepository _empresaRepository;
    private readonly ISpedArquivoRepository _spedArquivoRepository;
    private readonly IParticipanteRepository _participanteRepository;
    private readonly IUnidadeMedidaRepository _unidadeMedidaRepository;
    private readonly IProdutoRepository _produtoRepository;
    private readonly IEfdContribuicoesRepository _contribuicoesRepository;

    public EfdContribuicoesImportService(
        EfdContribuicoesParser parser,
        IEmpresaRepository empresaRepository,
        ISpedArquivoRepository spedArquivoRepository,
        IParticipanteRepository participanteRepository,
        IUnidadeMedidaRepository unidadeMedidaRepository,
        IProdutoRepository produtoRepository,
        IEfdContribuicoesRepository contribuicoesRepository)
    {
        _parser = parser;
        _empresaRepository = empresaRepository;
        _spedArquivoRepository = spedArquivoRepository;
        _participanteRepository = participanteRepository;
        _unidadeMedidaRepository = unidadeMedidaRepository;
        _produtoRepository = produtoRepository;
        _contribuicoesRepository = contribuicoesRepository;
    }

    public async Task<ImportacaoResultado> ImportarAsync(
        string caminhoArquivo,
        IProgress<ImportacaoProgresso>? progresso = null,
        CancellationToken cancellationToken = default)
    {
        var resultado = new ImportacaoResultado
        {
            NomeArquivo = Path.GetFileName(caminhoArquivo)
        };

        progresso?.Report(new ImportacaoProgresso { Percentual = 10, Mensagem = "Validando arquivo EFD Contribuicoes..." });

        if (!File.Exists(caminhoArquivo))
        {
            resultado.Mensagens.Add($"Arquivo nao encontrado: {caminhoArquivo}");
            return resultado;
        }

        resultado.HashArquivo = await CalcularHashSha256Async(caminhoArquivo, cancellationToken);
        if (await _spedArquivoRepository.ExistsByHashAsync(resultado.HashArquivo, cancellationToken))
        {
            resultado.ArquivoJaImportado = true;
            resultado.Mensagens.Add("Aviso: este arquivo ja foi importado anteriormente. A importacao foi cancelada.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Arquivo ja importado." });
            return resultado;
        }

        progresso?.Report(new ImportacaoProgresso { Percentual = 20, Mensagem = "Lendo cadastros do EFD Contribuicoes..." });
        var dados = _parser.Parse(caminhoArquivo);
        if (!ValidarArquivo(dados, resultado))
        {
            resultado.Mensagens.Add("Arquivo invalido: selecione um arquivo EFD Contribuicoes valido.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Arquivo invalido." });
            return resultado;
        }

        var empresa = dados.Empresa!;
        var arquivo = dados.Arquivo!;
        PreencherResumo(resultado, empresa, arquivo);

        if (await _spedArquivoRepository.ExistsByCnpjCompetenciaTipoAsync(empresa.Cnpj, arquivo.PeriodoInicial.Year, arquivo.PeriodoInicial.Month, TipoArquivo, cancellationToken))
        {
            resultado.EmpresaPeriodoJaImportado = true;
            resultado.Mensagens.Add($"Aviso: ja existe EFD Contribuicoes importado para o CNPJ {empresa.Cnpj} na competencia {arquivo.PeriodoInicial:MM/yyyy}.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Competencia ja importada." });
            return resultado;
        }

        progresso?.Report(new ImportacaoProgresso { Percentual = 35, Mensagem = "Gravando empresa e arquivo..." });
        empresa.Id = await ObterOuCriarEmpresaAsync(empresa, cancellationToken);

        arquivo.EmpresaId = empresa.Id;
        arquivo.NomeArquivo = resultado.NomeArquivo;
        arquivo.HashArquivo = resultado.HashArquivo;
        arquivo.ImportadoEm = DateTime.UtcNow;
        arquivo.Id = await _spedArquivoRepository.InsertAsync(arquivo, TipoArquivo, cancellationToken);
        resultado.SpedArquivoId = arquivo.Id;

        await ImportarCadastrosAsync(dados, arquivo.Id, resultado, progresso, cancellationToken);

        resultado.Sucesso = true;
        resultado.Mensagens.Add("Importacao EFD Contribuicoes concluida com sucesso.");
        resultado.Mensagens.Add("Documentos e apuracao de PIS/COFINS gravados para o dashboard de contribuicoes.");
        progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Importacao concluida." });

        return resultado;
    }

    private static bool ValidarArquivo(EfdContribuicoesParseResult dados, ImportacaoResultado resultado)
    {
        if (dados.Empresa is null || dados.Arquivo is null)
        {
            resultado.Mensagens.Add("Registro 0000 nao encontrado.");
            return false;
        }

        PreencherResumo(resultado, dados.Empresa, dados.Arquivo);

        if (string.IsNullOrWhiteSpace(dados.Empresa.Cnpj) || dados.Empresa.Cnpj.Length != 14 || !dados.Empresa.Cnpj.All(char.IsDigit))
        {
            resultado.Mensagens.Add("Registro 0000 sem CNPJ valido.");
            return false;
        }

        if (dados.Arquivo.PeriodoInicial == default || dados.Arquivo.PeriodoFinal == default || dados.Arquivo.PeriodoInicial > dados.Arquivo.PeriodoFinal)
        {
            resultado.Mensagens.Add("Registro 0000 com periodo invalido.");
            return false;
        }

        resultado.ArquivoValido = true;
        return true;
    }

    private static void PreencherResumo(ImportacaoResultado resultado, Empresa empresa, SpedArquivo arquivo)
    {
        resultado.Cnpj = empresa.Cnpj;
        resultado.Uf = empresa.Uf;
        resultado.InscricaoEstadual = empresa.InscricaoEstadual;
        resultado.PeriodoInicial = arquivo.PeriodoInicial;
        resultado.PeriodoFinal = arquivo.PeriodoFinal;
    }

    private async Task<int> ObterOuCriarEmpresaAsync(Empresa empresa, CancellationToken cancellationToken)
    {
        var empresaExistente = await _empresaRepository.GetByCnpjAsync(empresa.Cnpj, cancellationToken);
        return empresaExistente?.Id ?? await _empresaRepository.InsertAsync(empresa, cancellationToken);
    }

    private async Task ImportarCadastrosAsync(
        EfdContribuicoesParseResult dados,
        int spedArquivoId,
        ImportacaoResultado resultado,
        IProgress<ImportacaoProgresso>? progresso,
        CancellationToken cancellationToken)
    {
        var unidadesPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var participantesPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var produtosPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int? c100Atual = null;
        int? a100Atual = null;
        var total = Math.Max(dados.Registros.Count, 1);
        var atual = 0;

        foreach (var registro in dados.Registros)
        {
            cancellationToken.ThrowIfCancellationRequested();
            atual++;

            switch (registro)
            {
                case Participante participante:
                    participante.SpedArquivoId = spedArquivoId;
                    participante.EmpresaId = dados.Arquivo!.EmpresaId;
                    var participanteExistenteId = await _participanteRepository.GetIdExistenteAsync(participante, cancellationToken);
                    if (participanteExistenteId is null)
                    {
                        participanteExistenteId = await _participanteRepository.InsertAsync(participante, cancellationToken);
                        resultado.ParticipantesImportados++;
                    }
                    participantesPorCodigo[participante.Codigo] = participanteExistenteId.Value;
                    break;

                case UnidadeMedida unidade:
                    unidade.SpedArquivoId = spedArquivoId;
                    unidade.EmpresaId = dados.Arquivo!.EmpresaId;
                    unidade.Id = await _unidadeMedidaRepository.InsertAsync(unidade, cancellationToken);
                    unidadesPorCodigo[unidade.Codigo] = unidade.Id;
                    resultado.UnidadesMedidaImportadas++;
                    break;

                case Produto produto:
                    produto.SpedArquivoId = spedArquivoId;
                    produto.EmpresaId = dados.Arquivo!.EmpresaId;
                    if (!string.IsNullOrWhiteSpace(produto.Unidade) && unidadesPorCodigo.TryGetValue(produto.Unidade, out var unidadeId))
                    {
                        produto.UnidadeMedidaId = unidadeId;
                    }

                    var produtoExistenteId = await _produtoRepository.GetIdExistenteAsync(produto, cancellationToken);
                    if (produtoExistenteId is null)
                    {
                        produtoExistenteId = await _produtoRepository.InsertAsync(produto, cancellationToken);
                        resultado.ProdutosImportados++;
                    }
                    produtosPorCodigo[produto.Codigo] = produtoExistenteId.Value;
                    break;

                case EfdContribuicoesRegistro contribuicao:
                    var campos = contribuicao.Campos;
                    var participanteId = ObterId(participantesPorCodigo, Campo(campos, 4));
                    var produtoId = ObterId(produtosPorCodigo, Campo(campos, 3));
                    var paiId = contribuicao.Codigo is "C170" or "C175" ? c100Atual : contribuicao.Codigo == "A170" ? a100Atual : null;

                    if ((contribuicao.Codigo is "C170" or "C175" or "A170") && paiId is null)
                    {
                        resultado.Mensagens.Add($"Aviso: registro {contribuicao.Codigo} ignorado por nao possuir documento pai.");
                        break;
                    }

                    var id = await _contribuicoesRepository.InsertAsync(contribuicao, spedArquivoId,
                        dados.Arquivo!.EmpresaId, paiId, participanteId, produtoId, cancellationToken);

                    switch (contribuicao.Codigo)
                    {
                        case "C100": c100Atual = id; resultado.C100Importados++; break;
                        case "C170": resultado.C170Importados++; break;
                        case "C175": resultado.C175Importados++; break;
                        case "A100": a100Atual = id; resultado.A100Importados++; break;
                        case "A170": resultado.A170Importados++; break;
                        case "M100": resultado.M100Importados++; break;
                        case "M200": resultado.M200Importados++; break;
                        case "M500": resultado.M500Importados++; break;
                        case "M600": resultado.M600Importados++; break;
                    }
                    break;
            }

            progresso?.Report(new ImportacaoProgresso
            {
                Percentual = Math.Min(95, 35 + (int)Math.Round(atual * 60.0 / total)),
                Mensagem = $"Importando cadastros... {atual}/{total}"
            });
        }
    }

    private static string Campo(string[] campos, int indice) => indice < campos.Length ? campos[indice].Trim() : string.Empty;

    private static int? ObterId(Dictionary<string, int> ids, string codigo)
        => !string.IsNullOrWhiteSpace(codigo) && ids.TryGetValue(codigo, out var id) ? id : null;

    private static async Task<string> CalcularHashSha256Async(string caminhoArquivo, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(caminhoArquivo);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
