namespace N3.AnalisadorFiscal.Sped.Services;

public interface IEfdContribuicoesImportService
{
    Task<ImportacaoResultado> ImportarAsync(
        string caminhoArquivo,
        IProgress<ImportacaoProgresso>? progresso = null,
        CancellationToken cancellationToken = default);
}
