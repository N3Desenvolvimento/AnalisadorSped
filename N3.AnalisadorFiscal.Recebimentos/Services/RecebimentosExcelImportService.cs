using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Recebimentos.Services;

public interface IRecebimentosExcelImportService
{
    Task<RecebimentosImportacaoResultado> ImportarAsync(
        int empresaId,
        int anoArquivo,
        int mesArquivo,
        string caminhoArquivo,
        CancellationToken cancellationToken = default);
}

public sealed class RecebimentosExcelImportService(
    RecebimentosExcelParser parser,
    IRecebimentoRepository repository,
    IEmpresaRepository empresas) : IRecebimentosExcelImportService
{
    public async Task<RecebimentosImportacaoResultado> ImportarAsync(
        int empresaId,
        int anoArquivo,
        int mesArquivo,
        string caminhoArquivo,
        CancellationToken cancellationToken = default)
    {
        _ = await empresas.GetByIdAsync(empresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");
        if (mesArquivo is < 1 or > 12)
            throw new InvalidOperationException("A competência do arquivo é inválida.");
        if (!string.Equals(Path.GetExtension(caminhoArquivo), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Envie uma planilha no formato .xlsx.");

        var resultado = await parser.LerAsync(
            empresaId,
            new DateTime(anoArquivo, mesArquivo, 1),
            caminhoArquivo,
            cancellationToken);
        resultado.Importacao.Id = await repository.SubstituirImportacaoAsync(resultado.Importacao, cancellationToken);

        return new RecebimentosImportacaoResultado(
            resultado.Importacao.Id,
            resultado.Importacao.NomeArquivo,
            resultado.Importacao.NomeAba,
            resultado.Importacao.CompetenciaArquivo,
            resultado.Importacao.LinhasLidas,
            resultado.Importacao.Documentos.Count,
            resultado.Importacao.Movimentos.Count,
            resultado.Importacao.TotalFaturadoArquivo,
            resultado.Importacao.TotalRecebidoArquivo,
            resultado.Importacao.TotalRetencoesArquivo,
            resultado.Competencias,
            resultado.Mensagens);
    }
}

public sealed record RecebimentosImportacaoResultado(
    int ImportacaoId,
    string NomeArquivo,
    string NomeAba,
    DateTime CompetenciaArquivo,
    int LinhasLidas,
    int Documentos,
    int Recebimentos,
    decimal TotalFaturadoArquivo,
    decimal TotalRecebidoArquivo,
    decimal TotalRetencoesArquivo,
    IReadOnlyList<RecebimentoCompetenciaImportada> Competencias,
    IReadOnlyList<string> Mensagens);

public sealed record RecebimentoCompetenciaImportada(int Ano, int Mes, int Quantidade, decimal ValorRecebido);
