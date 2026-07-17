using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Web.Services;

public interface ICnpjPublicoConsultaService
{
    Task SincronizarAsync(Empresa empresa, CancellationToken cancellationToken = default);
}

public sealed class CnpjPublicoConsultaService(HttpClient httpClient) : ICnpjPublicoConsultaService
{
    public async Task SincronizarAsync(Empresa empresa, CancellationToken cancellationToken = default)
    {
        var cnpj = new string(empresa.Cnpj.Where(char.IsDigit).ToArray());
        if (cnpj.Length != 14) throw new InvalidOperationException("Informe um CNPJ válido antes de sincronizar.");

        var dados = await ConsultarBrasilApiAsync(cnpj, cancellationToken)
            ?? await ConsultarMinhaReceitaAsync(cnpj, cancellationToken)
            ?? throw new InvalidOperationException("CNPJ não encontrado nas bases públicas disponíveis.");

        empresa.Cnpj = cnpj;
        empresa.RazaoSocial = dados.RazaoSocial?.Trim() ?? empresa.RazaoSocial;
        empresa.NomeFantasia = Limpar(dados.NomeFantasia);
        empresa.DataAbertura = dados.DataInicioAtividade;
        empresa.CnaePrincipalCodigo = dados.CnaeFiscal?.ToString();
        empresa.CnaePrincipalDescricao = Limpar(dados.CnaeFiscalDescricao);
        empresa.CnaesSecundarios = dados.CnaesSecundarios is { Count: > 0 }
            ? string.Join(Environment.NewLine, dados.CnaesSecundarios.Select(c => $"{c.Codigo} - {c.Descricao}")) : null;
        empresa.NaturezaJuridica = Limpar(dados.NaturezaJuridica);
        empresa.Porte = Limpar(dados.Porte);
        empresa.SituacaoCadastral = Limpar(dados.DescricaoSituacaoCadastral);
        empresa.DataSituacaoCadastral = dados.DataSituacaoCadastral;
        empresa.MotivoSituacaoCadastral = Limpar(dados.MotivoSituacaoCadastral);
        empresa.SituacaoEspecial = Limpar(dados.SituacaoEspecial);
        empresa.DataSituacaoEspecial = dados.DataSituacaoEspecial;
        empresa.TipoLogradouro = Limpar(dados.TipoLogradouro);
        empresa.Logradouro = Limpar(dados.Logradouro);
        empresa.Numero = Limpar(dados.Numero);
        empresa.Complemento = Limpar(dados.Complemento);
        empresa.Cep = SomenteDigitosOuNulo(dados.Cep);
        empresa.Bairro = Limpar(dados.Bairro);
        empresa.Municipio = Limpar(dados.Municipio);
        empresa.Uf = Limpar(dados.Uf);
        empresa.CodigoMunicipio = dados.CodigoMunicipioIbge?.ToString();
        empresa.Email = Limpar(dados.Email);
        empresa.Telefone = Limpar(string.Join(" / ", new[] { dados.Telefone1, dados.Telefone2 }.Where(v => !string.IsNullOrWhiteSpace(v))));
        empresa.EnteFederativoResponsavel = Limpar(dados.EnteFederativoResponsavel);
        empresa.SincronizadoEm = DateTime.UtcNow;
    }

    private async Task<CnpjResponse?> ConsultarBrasilApiAsync(string cnpj, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/cnpj/v1/{cnpj}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) return null;
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<CnpjResponse>(stream, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<CnpjResponse?> ConsultarMinhaReceitaAsync(string cnpj, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"https://minhareceita.org/{cnpj}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("As bases públicas atingiram o limite de consultas. Aguarde alguns minutos e tente novamente.");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<CnpjResponse>(stream, cancellationToken: cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("As bases públicas demoraram para responder. Tente novamente.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"As bases públicas estão indisponíveis: {ex.Message}");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("A base pública alternativa retornou dados em formato inesperado.");
        }
    }

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    private static string? SomenteDigitosOuNulo(string? valor) { var resultado = new string((valor ?? "").Where(char.IsDigit).ToArray()); return resultado.Length == 0 ? null : resultado; }

    private sealed class CnpjResponse
    {
        [JsonPropertyName("razao_social")] public string? RazaoSocial { get; set; }
        [JsonPropertyName("nome_fantasia")] public string? NomeFantasia { get; set; }
        [JsonPropertyName("data_inicio_atividade")] public DateTime? DataInicioAtividade { get; set; }
        [JsonPropertyName("cnae_fiscal")] public long? CnaeFiscal { get; set; }
        [JsonPropertyName("cnae_fiscal_descricao")] public string? CnaeFiscalDescricao { get; set; }
        [JsonPropertyName("cnaes_secundarios")] public List<CnaeResponse>? CnaesSecundarios { get; set; }
        [JsonPropertyName("natureza_juridica")] public string? NaturezaJuridica { get; set; }
        [JsonPropertyName("porte")] public string? Porte { get; set; }
        [JsonPropertyName("descricao_situacao_cadastral")] public string? DescricaoSituacaoCadastral { get; set; }
        [JsonPropertyName("data_situacao_cadastral")] public DateTime? DataSituacaoCadastral { get; set; }
        [JsonPropertyName("motivo_situacao_cadastral")] public string? MotivoSituacaoCadastral { get; set; }
        [JsonPropertyName("situacao_especial")] public string? SituacaoEspecial { get; set; }
        [JsonPropertyName("data_situacao_especial")] public DateTime? DataSituacaoEspecial { get; set; }
        [JsonPropertyName("descricao_tipo_de_logradouro")] public string? TipoLogradouro { get; set; }
        [JsonPropertyName("logradouro")] public string? Logradouro { get; set; }
        [JsonPropertyName("numero")] public string? Numero { get; set; }
        [JsonPropertyName("complemento")] public string? Complemento { get; set; }
        [JsonPropertyName("cep")] public string? Cep { get; set; }
        [JsonPropertyName("bairro")] public string? Bairro { get; set; }
        [JsonPropertyName("municipio")] public string? Municipio { get; set; }
        [JsonPropertyName("uf")] public string? Uf { get; set; }
        [JsonPropertyName("codigo_municipio_ibge")] public long? CodigoMunicipioIbge { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("ddd_telefone_1")] public string? Telefone1 { get; set; }
        [JsonPropertyName("ddd_telefone_2")] public string? Telefone2 { get; set; }
        [JsonPropertyName("ente_federativo_responsavel")] public string? EnteFederativoResponsavel { get; set; }
    }
    private sealed class CnaeResponse { [JsonPropertyName("codigo")] public long Codigo { get; set; } [JsonPropertyName("descricao")] public string? Descricao { get; set; } }
}
