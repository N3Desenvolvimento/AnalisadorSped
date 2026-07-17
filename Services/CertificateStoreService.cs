using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace N3.AnalisadorFiscal.Web.Services;

public sealed record CertificadoWindowsDto(
    string Thumbprint,
    string StoreLocation,
    string Nome,
    string EmitidoPor,
    DateTime ValidoAte,
    bool Valido,
    bool TemChavePrivada)
{
    public string Identificador => $"{StoreLocation}|{Thumbprint}";
}

public interface ICertificateStoreService
{
    IReadOnlyList<CertificadoWindowsDto> ListarAsync();
}

public sealed class CertificateStoreService : ICertificateStoreService
{
    public IReadOnlyList<CertificadoWindowsDto> ListarAsync()
    {
        var agora = DateTime.Now;
        var certificados = new List<CertificadoWindowsDto>();
        foreach (var local in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, local);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                certificados.AddRange(store.Certificates
                    .Cast<X509Certificate2>()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Thumbprint))
                    .Select(c => new CertificadoWindowsDto(
                        c.Thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                        local.ToString(),
                        NomeAmigavel(c),
                        c.GetNameInfo(X509NameType.SimpleName, true),
                        c.NotAfter,
                        c.NotBefore <= agora && c.NotAfter >= agora,
                        c.HasPrivateKey)));
            }
            catch (CryptographicException)
            {
                // A conta da aplicação pode não ter acesso a um dos repositórios.
            }
        }

        return certificados
            .GroupBy(c => c.Identificador, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(c => c.TemChavePrivada && c.Valido)
            .ThenBy(c => c.Nome)
            .ToArray();
    }

    private static string NomeAmigavel(X509Certificate2 certificado)
    {
        if (!string.IsNullOrWhiteSpace(certificado.FriendlyName)) return certificado.FriendlyName;
        var nome = certificado.GetNameInfo(X509NameType.SimpleName, false);
        return string.IsNullOrWhiteSpace(nome) ? certificado.Subject : nome;
    }
}
