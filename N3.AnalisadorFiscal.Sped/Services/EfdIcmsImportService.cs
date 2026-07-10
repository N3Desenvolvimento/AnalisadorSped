using System.Security.Cryptography;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Domain.Entities;
using N3.AnalisadorFiscal.Sped.Parsing;

namespace N3.AnalisadorFiscal.Sped.Services;

public sealed class EfdIcmsImportService : IEfdIcmsImportService
{
    private readonly EfdIcmsParser _parser;
    private readonly IEmpresaRepository _empresaRepository;
    private readonly ISpedArquivoRepository _spedArquivoRepository;
    private readonly IParticipanteRepository _participanteRepository;
    private readonly IUnidadeMedidaRepository _unidadeMedidaRepository;
    private readonly IProdutoRepository _produtoRepository;
    private readonly ISpedC100Repository _spedC100Repository;
    private readonly ISpedC170Repository _spedC170Repository;
    private readonly ISpedC190Repository _spedC190Repository;
    private readonly ISpedE110Repository _spedE110Repository;
    private readonly ISpedE111Repository _spedE111Repository;

    public EfdIcmsImportService(
        EfdIcmsParser parser,
        IEmpresaRepository empresaRepository,
        ISpedArquivoRepository spedArquivoRepository,
        IParticipanteRepository participanteRepository,
        IUnidadeMedidaRepository unidadeMedidaRepository,
        IProdutoRepository produtoRepository,
        ISpedC100Repository spedC100Repository,
        ISpedC170Repository spedC170Repository,
        ISpedC190Repository spedC190Repository,
        ISpedE110Repository spedE110Repository,
        ISpedE111Repository spedE111Repository)
    {
        _parser = parser;
        _empresaRepository = empresaRepository;
        _spedArquivoRepository = spedArquivoRepository;
        _participanteRepository = participanteRepository;
        _unidadeMedidaRepository = unidadeMedidaRepository;
        _produtoRepository = produtoRepository;
        _spedC100Repository = spedC100Repository;
        _spedC170Repository = spedC170Repository;
        _spedC190Repository = spedC190Repository;
        _spedE110Repository = spedE110Repository;
        _spedE111Repository = spedE111Repository;
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

        progresso?.Report(new ImportacaoProgresso { Percentual = 11, Mensagem = "Validando arquivo..." });

        if (!File.Exists(caminhoArquivo))
        {
            resultado.Mensagens.Add($"Arquivo nao encontrado: {caminhoArquivo}");
            return resultado;
        }

        progresso?.Report(new ImportacaoProgresso { Percentual = 15, Mensagem = "Calculando hash do arquivo..." });
        resultado.HashArquivo = await CalcularHashSha256Async(caminhoArquivo, cancellationToken);

        progresso?.Report(new ImportacaoProgresso { Percentual = 20, Mensagem = "Verificando se o arquivo ja foi importado..." });
        if (await _spedArquivoRepository.ExistsByHashAsync(resultado.HashArquivo, cancellationToken))
        {
            resultado.ArquivoJaImportado = true;
            resultado.Mensagens.Add("Aviso: este arquivo ja foi importado anteriormente. A importacao foi cancelada.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Arquivo ja importado." });
            return resultado;
        }

        progresso?.Report(new ImportacaoProgresso { Percentual = 25, Mensagem = "Lendo registros do SPED..." });
        var dados = _parser.Parse(caminhoArquivo);
        if (!ValidarArquivoSpedIcmsIpi(dados, resultado))
        {
            resultado.Mensagens.Add("Arquivo invalido: selecione um arquivo SPED Fiscal ICMS/IPI valido.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Arquivo invalido." });
            return resultado;
        }

        var empresa = dados.Empresa!;
        var spedArquivo = dados.Arquivo!;
        PreencherResumoArquivo(resultado, empresa, spedArquivo);

        progresso?.Report(new ImportacaoProgresso { Percentual = 30, Mensagem = "Verificando competencia da empresa..." });
        if (await _spedArquivoRepository.ExistsByCnpjCompetenciaAsync(empresa.Cnpj, spedArquivo.PeriodoInicial.Year, spedArquivo.PeriodoInicial.Month, cancellationToken))
        {
            resultado.EmpresaPeriodoJaImportado = true;
            resultado.Mensagens.Add($"Aviso: ja existe arquivo importado para o CNPJ {empresa.Cnpj} na competencia {spedArquivo.PeriodoInicial:MM/yyyy}. A importacao foi cancelada.");
            progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Competencia ja importada." });
            return resultado;
        }

        progresso?.Report(new ImportacaoProgresso { Percentual = 35, Mensagem = "Gravando empresa e arquivo..." });
        empresa.Id = await ObterOuCriarEmpresaAsync(empresa, cancellationToken);

        spedArquivo.EmpresaId = empresa.Id;
        spedArquivo.NomeArquivo = resultado.NomeArquivo;
        spedArquivo.HashArquivo = resultado.HashArquivo;
        spedArquivo.ImportadoEm = DateTime.UtcNow;
        spedArquivo.Id = await _spedArquivoRepository.InsertAsync(spedArquivo, cancellationToken);
        resultado.SpedArquivoId = spedArquivo.Id;

        await ImportarRegistrosAsync(dados, spedArquivo.Id, resultado, progresso, cancellationToken);

        resultado.Sucesso = true;
        resultado.Mensagens.Add("Importacao concluida com sucesso.");
        resultado.Mensagens.Add($"Resumo: {resultado.TotalRegistrosImportados} registros importados para o arquivo SPED {resultado.SpedArquivoId}.");
        progresso?.Report(new ImportacaoProgresso { Percentual = 100, Mensagem = "Importacao concluida." });
        return resultado;
    }

    private static bool ValidarArquivoSpedIcmsIpi(EfdIcmsParseResult dados, ImportacaoResultado resultado)
    {
        if (dados.Empresa is null || dados.Arquivo is null)
        {
            resultado.Mensagens.Add("Registro 0000 nao encontrado.");
            return false;
        }

        var empresa = dados.Empresa;
        var arquivo = dados.Arquivo;
        PreencherResumoArquivo(resultado, empresa, arquivo);

        if (string.IsNullOrWhiteSpace(arquivo.VersaoLeiaute))
        {
            resultado.Mensagens.Add("Registro 0000 sem versao do leiaute.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(empresa.Cnpj) || empresa.Cnpj.Length != 14 || !empresa.Cnpj.All(char.IsDigit))
        {
            resultado.Mensagens.Add("Registro 0000 sem CNPJ valido.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(empresa.Uf) || empresa.Uf.Length != 2)
        {
            resultado.Mensagens.Add("Registro 0000 sem UF valida.");
            return false;
        }

        if (arquivo.PeriodoInicial == default || arquivo.PeriodoFinal == default || arquivo.PeriodoInicial > arquivo.PeriodoFinal)
        {
            resultado.Mensagens.Add("Registro 0000 com periodo invalido.");
            return false;
        }

        if (dados.Registros.Count == 0)
        {
            resultado.Mensagens.Add("Nenhum registro fiscal reconhecido foi encontrado no arquivo.");
            return false;
        }

        resultado.ArquivoValido = true;
        return true;
    }

    private static void PreencherResumoArquivo(ImportacaoResultado resultado, Empresa empresa, SpedArquivo spedArquivo)
    {
        resultado.Cnpj = empresa.Cnpj;
        resultado.Uf = empresa.Uf;
        resultado.InscricaoEstadual = empresa.InscricaoEstadual;
        resultado.PeriodoInicial = spedArquivo.PeriodoInicial;
        resultado.PeriodoFinal = spedArquivo.PeriodoFinal;
    }

    private async Task<int> ObterOuCriarEmpresaAsync(Empresa empresa, CancellationToken cancellationToken)
    {
        var empresaExistente = await _empresaRepository.GetByCnpjAsync(empresa.Cnpj, cancellationToken);
        if (empresaExistente is not null)
        {
            return empresaExistente.Id;
        }

        return await _empresaRepository.InsertAsync(empresa, cancellationToken);
    }

    private async Task ImportarRegistrosAsync(
        EfdIcmsParseResult dados,
        int spedArquivoId,
        ImportacaoResultado resultado,
        IProgress<ImportacaoProgresso>? progresso,
        CancellationToken cancellationToken)
    {
        var participantesPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unidadesPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var produtosPorCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spedC100IdAtual = 0;
        var spedE110IdAtual = 0;
        var totalRegistros = Math.Max(dados.Registros.Count, 1);
        var registrosProcessados = 0;

        foreach (var registro in dados.Registros)
        {
            cancellationToken.ThrowIfCancellationRequested();
            registrosProcessados++;

            switch (registro.Entidade)
            {
                case Participante participante:
                    participante.SpedArquivoId = spedArquivoId;
                    participante.EmpresaId = dados.Arquivo!.EmpresaId;

                    var participanteExistenteId = await _participanteRepository.GetIdExistenteAsync(participante, cancellationToken);
                    if (participanteExistenteId is not null)
                    {
                        participante.Id = participanteExistenteId.Value;
                    }
                    else
                    {
                        participante.Id = await _participanteRepository.InsertAsync(participante, cancellationToken);
                        resultado.ParticipantesImportados++;
                    }

                    participantesPorCodigo[participante.Codigo] = participante.Id;
                    break;

                case UnidadeMedida unidadeMedida:
                    unidadeMedida.SpedArquivoId = spedArquivoId;
                    unidadeMedida.EmpresaId = dados.Arquivo!.EmpresaId;
                    unidadeMedida.Id = await _unidadeMedidaRepository.InsertAsync(unidadeMedida, cancellationToken);
                    unidadesPorCodigo[unidadeMedida.Codigo] = unidadeMedida.Id;
                    resultado.UnidadesMedidaImportadas++;
                    break;

                case Produto produto:
                    produto.SpedArquivoId = spedArquivoId;
                    produto.EmpresaId = dados.Arquivo!.EmpresaId;
                    if (!string.IsNullOrWhiteSpace(produto.Unidade) && unidadesPorCodigo.TryGetValue(produto.Unidade, out var unidadeMedidaId))
                    {
                        produto.UnidadeMedidaId = unidadeMedidaId;
                    }

                    var produtoExistenteId = await _produtoRepository.GetIdExistenteAsync(produto, cancellationToken);
                    if (produtoExistenteId is not null)
                    {
                        produto.Id = produtoExistenteId.Value;
                    }
                    else
                    {
                        produto.Id = await _produtoRepository.InsertAsync(produto, cancellationToken);
                        resultado.ProdutosImportados++;
                    }

                    produtosPorCodigo[produto.Codigo] = produto.Id;
                    break;

                case SpedC100 spedC100:
                    spedC100.SpedArquivoId = spedArquivoId;
                    spedC100.EmpresaId = dados.Arquivo!.EmpresaId;
                    if (!string.IsNullOrWhiteSpace(spedC100.CodigoParticipante) && participantesPorCodigo.TryGetValue(spedC100.CodigoParticipante, out var participanteId))
                    {
                        spedC100.ParticipanteId = participanteId;
                    }

                    spedC100.Id = await _spedC100Repository.InsertAsync(spedC100, cancellationToken);
                    spedC100IdAtual = spedC100.Id;
                    resultado.C100Importados++;
                    break;

                case SpedC170 spedC170:
                    if (spedC100IdAtual == 0)
                    {
                        resultado.Mensagens.Add($"Aviso: registro C170 do item {spedC170.CodigoItem} ignorado porque nao ha C100 anterior.");
                        break;
                    }

                    spedC170.SpedC100Id = spedC100IdAtual;
                    if (!string.IsNullOrWhiteSpace(spedC170.CodigoItem) && produtosPorCodigo.TryGetValue(spedC170.CodigoItem, out var produtoId))
                    {
                        spedC170.ProdutoId = produtoId;
                    }

                    spedC170.Id = await _spedC170Repository.InsertAsync(spedC170, cancellationToken);
                    resultado.C170Importados++;
                    break;

                case SpedC190 spedC190:
                    if (spedC100IdAtual == 0)
                    {
                        resultado.Mensagens.Add($"Aviso: registro C190 CFOP {spedC190.Cfop} CST {spedC190.CstIcms} ignorado porque nao ha C100 anterior.");
                        break;
                    }

                    spedC190.SpedC100Id = spedC100IdAtual;
                    spedC190.Id = await _spedC190Repository.InsertAsync(spedC190, cancellationToken);
                    resultado.C190Importados++;
                    break;

                case SpedE110 spedE110:
                    spedE110.SpedArquivoId = spedArquivoId;
                    spedE110.Id = await _spedE110Repository.InsertAsync(spedE110, cancellationToken);
                    spedE110IdAtual = spedE110.Id;
                    resultado.E110Importados++;
                    break;

                case SpedE111 spedE111:
                    if (spedE110IdAtual == 0)
                    {
                        resultado.Mensagens.Add($"Aviso: registro E111 {spedE111.CodigoAjusteApuracao} ignorado porque nao ha E110 anterior.");
                        break;
                    }

                    spedE111.SpedE110Id = spedE110IdAtual;
                    spedE111.Id = await _spedE111Repository.InsertAsync(spedE111, cancellationToken);
                    resultado.E111Importados++;
                    break;
            }

            ReportarProgressoImportacao(progresso, registrosProcessados, totalRegistros);
        }
    }

    private static void ReportarProgressoImportacao(IProgress<ImportacaoProgresso>? progresso, int registrosProcessados, int totalRegistros)
    {
        if (progresso is null)
        {
            return;
        }

        var percentual = 35 + (int)Math.Round(registrosProcessados * 60.0 / totalRegistros);
        progresso.Report(new ImportacaoProgresso
        {
            Percentual = Math.Min(percentual, 95),
            Mensagem = $"Importando registros... {registrosProcessados}/{totalRegistros}"
        });
    }

    private static async Task<string> CalcularHashSha256Async(string caminhoArquivo, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(caminhoArquivo);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
