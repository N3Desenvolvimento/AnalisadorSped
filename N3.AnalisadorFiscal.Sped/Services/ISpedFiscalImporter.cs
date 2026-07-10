namespace N3.AnalisadorFiscal.Sped.Services;

public interface ISpedFiscalImporter
{
    Task ImportAsync(Stream spedFiscalStream, string nomeArquivo, CancellationToken cancellationToken = default);
}
