using System.IO.Compression;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using N3.AnalisadorFiscal.Data.Repositories;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Web.Services;

public sealed class NfseNacionalOptions
{
    public const string SectionName = "NfseNacional";
    public string BaseUrl { get; set; } = "https://adn.producaorestrita.nfse.gov.br/contribuintes/";
    public string? CertificadoPath { get; set; }
    public string? CertificadoSenha { get; set; }
    public int MaxDocumentosPorSincronizacao { get; set; } = 10;
}

public sealed record NfseSincronizacaoResultado(
    int DocumentosImportados,
    long UltimoNsu,
    string Mensagem,
    int DocumentosRecebidos = 0,
    int EventosRecebidos = 0,
    int DocumentosNaoReconhecidos = 0);

public interface INfseNacionalService
{
    string Ambiente { get; }
    Task<NfseSincronizacaoResultado> SincronizarAsync(int empresaId, string cnpj, CancellationToken cancellationToken = default);
    Task<NfseSincronizacaoResultado> ReprocessarAsync(int empresaId, string cnpj, CancellationToken cancellationToken = default);
}

public sealed class NfseNacionalService : INfseNacionalService
{
    private readonly NfseNacionalOptions _options;
    private readonly INfseRepository _repository;
    private readonly IEmpresaRepository _empresaRepository;

    public NfseNacionalService(IOptions<NfseNacionalOptions> options, INfseRepository repository, IEmpresaRepository empresaRepository)
    {
        _options = options.Value;
        _repository = repository;
        _empresaRepository = empresaRepository;
    }

    public string Ambiente => _options.BaseUrl.Contains("producaorestrita", StringComparison.OrdinalIgnoreCase)
        ? "Homologação" : "Produção";

    public async Task<NfseSincronizacaoResultado> ReprocessarAsync(int empresaId, string cnpj, CancellationToken cancellationToken = default)
    {
        await _repository.ReiniciarNsuAsync(empresaId, cancellationToken);
        return await SincronizarAsync(empresaId, cnpj, cancellationToken);
    }

    public async Task<NfseSincronizacaoResultado> SincronizarAsync(int empresaId, string cnpj, CancellationToken cancellationToken = default)
    {
        using var certificado = await CarregarCertificadoAsync(empresaId, cancellationToken);
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificado);
        handler.ServerCertificateCustomValidationCallback = null;
        using var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var cnpjConsulta = SomenteDigitos(cnpj);
        var nsu = await _repository.GetUltimoNsuAsync(empresaId, cancellationToken);
        var importados = 0;
        var recebidos = 0;
        var eventos = 0;
        var naoReconhecidos = 0;
        string? limiteMensagem = null;

        for (var i = 0; i < Math.Max(1, _options.MaxDocumentosPorSincronizacao); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nsuConsultado = nsu;
            var url = $"DFe/{nsuConsultado}?cnpjConsulta={Uri.EscapeDataString(cnpjConsulta)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limiteMensagem = MensagemLimite(response);
                break;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) break;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException("O Portal Nacional recusou o certificado ou o CNPJ consultado.");
            response.EnsureSuccessStatusCode();

            var conteudo = await response.Content.ReadAsStringAsync(cancellationToken);
            var documentos = ExtrairDocumentos(conteudo, nsuConsultado).ToArray();
            if (documentos.Length == 0) break;
            foreach (var (nsuDocumento, xml) in documentos)
            {
                recebidos++;
                nsu = Math.Max(nsu, nsuDocumento);
                var nota = NfseXmlParser.Parse(xml, empresaId, nsuDocumento);
                if (nota is not null)
                {
                    await _repository.SalvarAsync(nota, cancellationToken);
                    importados++;
                }
                else if (NfseXmlParser.ObterChaveCancelada(xml) is { Length: > 0 } chaveCancelada)
                {
                    eventos++;
                    await _repository.MarcarCanceladaAsync(empresaId, chaveCancelada, cancellationToken);
                }
                else
                {
                    naoReconhecidos++;
                }
            }
            await _repository.AtualizarUltimoNsuAsync(empresaId, nsu, cancellationToken);
            if (nsu <= nsuConsultado) break;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var mensagem = limiteMensagem ?? (importados == 0
            ? recebidos == 0 ? "Nenhum documento novo localizado no ADN." : "O ADN retornou documentos, mas nenhuma NFS-e foi reconhecida."
            : $"{importados} documento(s) sincronizado(s).");
        return new NfseSincronizacaoResultado(importados, nsu, mensagem, recebidos, eventos, naoReconhecidos);
    }

    private static string MensagemLimite(HttpResponseMessage response)
    {
        var espera = response.Headers.RetryAfter?.Delta;
        if (espera.HasValue)
            return $"O Portal Nacional limitou temporariamente as consultas. Tente novamente em aproximadamente {Math.Max(1, (int)Math.Ceiling(espera.Value.TotalMinutes))} minuto(s).";
        return "O Portal Nacional limitou temporariamente as consultas. Aguarde alguns minutos antes de sincronizar novamente.";
    }

    private static IEnumerable<(long Nsu, string Xml)> ExtrairDocumentos(string conteudo, long nsuPadrao)
    {
        if (conteudo.TrimStart().StartsWith('<'))
        {
            var resposta = XDocument.Parse(conteudo);
            if (resposta.Descendants().Any(e => e.Name.LocalName.Equals("infNFSe", StringComparison.OrdinalIgnoreCase)))
            {
                yield return (nsuPadrao, conteudo);
                yield break;
            }

            foreach (var lote in resposta.Descendants().Where(e => e.Name.LocalName.Equals("LoteDFe", StringComparison.OrdinalIgnoreCase)))
            {
                var nsuTexto = lote.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("NSU", StringComparison.OrdinalIgnoreCase))?.Value;
                var nsu = long.TryParse(nsuTexto, out var numero) ? numero : nsuPadrao;
                var arquivo = lote.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ArquivoXml", StringComparison.OrdinalIgnoreCase));
                if (arquivo is null) continue;
                var xml = arquivo.Elements().FirstOrDefault() is { } estruturado
                    ? estruturado.ToString(SaveOptions.DisableFormatting)
                    : DecodificarXml(arquivo.Value.Trim());
                if (xml.TrimStart().StartsWith('<')) yield return (nsu, xml);
            }
            yield break;
        }
        using var json = JsonDocument.Parse(conteudo);
        foreach (var item in Percorrer(json.RootElement))
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var nsu = ObterLong(item, "nsu", "NSU") ?? nsuPadrao;
            var valor = ObterTexto(item, "arquivoXml", "ArquivoXml", "xml", "Xml", "documento", "Documento");
            if (string.IsNullOrWhiteSpace(valor)) continue;
            var xml = DecodificarXml(valor);
            if (xml.TrimStart().StartsWith('<')) yield return (nsu, xml);
        }
    }

    private static IEnumerable<JsonElement> Percorrer(JsonElement elemento)
    {
        yield return elemento;
        if (elemento.ValueKind == JsonValueKind.Array)
            foreach (var filho in elemento.EnumerateArray()) foreach (var item in Percorrer(filho)) yield return item;
        else if (elemento.ValueKind == JsonValueKind.Object)
            foreach (var propriedade in elemento.EnumerateObject()) foreach (var item in Percorrer(propriedade.Value)) yield return item;
    }

    private static string? ObterTexto(JsonElement item, params string[] nomes)
    {
        foreach (var nome in nomes)
            if (item.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String) return valor.GetString();
        return null;
    }

    private static long? ObterLong(JsonElement item, params string[] nomes)
    {
        foreach (var nome in nomes)
            if (item.TryGetProperty(nome, out var valor) && (valor.TryGetInt64(out var numero) || long.TryParse(valor.ToString(), out numero))) return numero;
        return null;
    }

    private static string DecodificarXml(string valor)
    {
        if (valor.TrimStart().StartsWith('<')) return valor;
        try
        {
            var bytes = Convert.FromBase64String(valor);
            if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var entrada = new MemoryStream(bytes);
                using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
                using var leitor = new StreamReader(gzip, Encoding.UTF8);
                return leitor.ReadToEnd();
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException) { return valor; }
    }

    private static string SomenteDigitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private async Task<X509Certificate2> CarregarCertificadoAsync(int empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _empresaRepository.GetByIdAsync(empresaId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(empresa?.CertificadoThumbprint))
        {
            var local = Enum.TryParse<StoreLocation>(empresa.CertificadoStoreLocation, true, out var configurado)
                ? configurado : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, local);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var certificado = store.Certificates
                .Find(X509FindType.FindByThumbprint, empresa.CertificadoThumbprint, validOnly: false)
                .OfType<X509Certificate2>()
                .FirstOrDefault();
            if (certificado is null)
                throw new InvalidOperationException("O certificado selecionado para a empresa não foi encontrado no repositório do Windows.");
            if (!certificado.HasPrivateKey)
                throw new InvalidOperationException("O certificado selecionado não possui chave privada acessível pela aplicação.");
            if (certificado.NotAfter < DateTime.Now || certificado.NotBefore > DateTime.Now)
                throw new InvalidOperationException("O certificado selecionado está fora do período de validade.");
            return new X509Certificate2(certificado);
        }

        if (string.IsNullOrWhiteSpace(_options.CertificadoPath))
            throw new InvalidOperationException("Selecione um certificado no cadastro da empresa.");
        if (!File.Exists(_options.CertificadoPath))
            throw new InvalidOperationException("O certificado configurado para a NFS-e não foi encontrado.");
        return new X509Certificate2(
            _options.CertificadoPath, _options.CertificadoSenha,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet);
    }
}

internal static class NfseXmlParser
{
    public static string? ObterChaveCancelada(string xml)
    {
        var doc = XDocument.Parse(xml);
        var cancelamento = doc.Descendants().Any(e => e.Name.LocalName is "e101101" or "e105102");
        if (!cancelamento) return null;
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("chNFSe", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    }

    public static NfseDocumento? Parse(string xml, int empresaId, long nsu)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (!doc.Descendants().Any(e => e.Name.LocalName.Equals("infNFSe", StringComparison.OrdinalIgnoreCase)))
            return null;
        string? V(params string[] nomes) => doc.Descendants().FirstOrDefault(e => nomes.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value;
        decimal D(params string[] nomes) => decimal.TryParse(V(nomes), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        DateTime Data(params string[] nomes) => DateTime.TryParse(V(nomes), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var v) ? v : DateTime.MinValue;
        var infNfse = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("infNFSe", StringComparison.OrdinalIgnoreCase));
        var chave = V("chNFSe", "chaveAcesso")
            ?? infNfse?.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("Id", StringComparison.OrdinalIgnoreCase))?.Value;
        chave = chave?.Replace("NFS", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(chave)) return null;
        var emissao = Data("dhProc", "dhEmi", "dEmi");
        var competencia = Data("dCompet", "competencia");
        if (emissao == DateTime.MinValue) emissao = DateTime.UtcNow;
        if (competencia == DateTime.MinValue) competencia = emissao.Date;
        var aliquota = D("pAliq", "aliquota");
        if (aliquota > 0 && aliquota < 1) aliquota *= 100;
        var tipoRetencaoPisCofins = V("tpRetPisCofins", "tipoRetencaoPisCofins");
        var pisCofinsRetidos = string.IsNullOrWhiteSpace(tipoRetencaoPisCofins) || tipoRetencaoPisCofins == "1";
        return new NfseDocumento
        {
            EmpresaId = empresaId, Nsu = nsu, ChaveAcesso = chave,
            NumeroNota = ObterNumeroNota(V("nNFSe", "numeroNFSe", "numeroNota"), chave),
            DataEmissao = emissao,
            Competencia = competencia.Date,
            CnpjPrestador = ObterCnpjPorGrupo(doc, "prest", "emit"),
            NomePrestador = ObterTextoPorGrupo(doc, ["prest", "emit"], ["xNome", "nome", "razaoSocial"]),
            CnpjTomador = ObterCnpjPorGrupo(doc, "toma", "tomador"),
            NomeTomador = ObterTextoPorGrupo(doc, ["toma", "tomador"], ["xNome", "nome", "razaoSocial"]),
            CodigoServico = V("cTribNac", "cTribMun", "codigoServico"),
            MunicipioIncidencia = V("cLocIncid", "cMunIncidencia"),
            ValorServicos = D("vServ", "vServicos"), BaseCalculo = D("vBC", "vBaseCalculo"),
            Aliquota = aliquota, ValorIss = D("vISSQN", "vISS", "valorIss"),
            ValorPisRetido = pisCofinsRetidos ? D("vPis", "valorPis") : 0,
            ValorCofinsRetido = pisCofinsRetidos ? D("vCofins", "valorCofins") : 0,
            ValorIrrfRetido = D("vRetIRRF", "vIRRF", "vIR", "valorIr"),
            ValorCsllRetido = D("vRetCSLL", "vCSLL", "valorCsll"),
            ValorInssRetido = D("vRetCP", "vINSS", "vCP", "valorInss"),
            IssRetido = V("tpRetISSQN", "issRetido") is "1" or "true" or "True",
            Cancelada = false,
            Xml = doc.ToString(SaveOptions.DisableFormatting)
        };
    }

    private static string? ObterNumeroNota(string? numeroXml, string chave)
    {
        if (!string.IsNullOrWhiteSpace(numeroXml)) return numeroXml.Trim();
        if (chave.Length != 50 || !chave.All(char.IsDigit)) return null;
        var numero = chave.Substring(22, 13).TrimStart('0');
        return numero.Length == 0 ? "0" : numero;
    }

    private static string ObterCnpjPorGrupo(XDocument doc, params string[] grupos)
    {
        var grupo = doc.Descendants().FirstOrDefault(e =>
            grupos.Any(g => e.Name.LocalName.Equals(g, StringComparison.OrdinalIgnoreCase)) &&
            e.Descendants().Any(d => d.Name.LocalName.Equals("CNPJ", StringComparison.OrdinalIgnoreCase)))
            ?? doc.Descendants().FirstOrDefault(e =>
                grupos.Any(g => e.Name.LocalName.Contains(g, StringComparison.OrdinalIgnoreCase)) &&
                e.Descendants().Any(d => d.Name.LocalName.Equals("CNPJ", StringComparison.OrdinalIgnoreCase)));
        return grupo?.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("CNPJ", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private static string? ObterTextoPorGrupo(XDocument doc, string[] grupos, string[] campos)
    {
        bool PossuiCampo(XElement elemento) => elemento.Descendants().Any(e => campos.Any(c => e.Name.LocalName.Equals(c, StringComparison.OrdinalIgnoreCase)));
        var grupo = doc.Descendants().FirstOrDefault(e =>
            grupos.Any(g => e.Name.LocalName.Equals(g, StringComparison.OrdinalIgnoreCase)) && PossuiCampo(e))
            ?? doc.Descendants().FirstOrDefault(e =>
                grupos.Any(g => e.Name.LocalName.Contains(g, StringComparison.OrdinalIgnoreCase)) && PossuiCampo(e));
        return grupo?.Descendants().FirstOrDefault(e => campos.Any(c => e.Name.LocalName.Equals(c, StringComparison.OrdinalIgnoreCase)))?.Value.Trim();
    }
}
