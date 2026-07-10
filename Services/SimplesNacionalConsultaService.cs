using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using N3.AnalisadorFiscal.Data.Dashboard;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Web.Services;

public interface ISimplesNacionalConsultaService
{
    Task<SimplesNacionalConsultaResumo> ConsultarPendentesAsync(int empresaId, IEnumerable<DashboardEntradaParticipanteDto> participantes, CancellationToken cancellationToken = default);
}

public sealed class SimplesNacionalConsultaService : ISimplesNacionalConsultaService
{
    private readonly HttpClient _httpClient;
    private readonly IParticipanteRepository _participanteRepository;

    public SimplesNacionalConsultaService(HttpClient httpClient, IParticipanteRepository participanteRepository)
    {
        _httpClient = httpClient;
        _participanteRepository = participanteRepository;
    }

    public async Task<SimplesNacionalConsultaResumo> ConsultarPendentesAsync(int empresaId, IEnumerable<DashboardEntradaParticipanteDto> participantes, CancellationToken cancellationToken = default)
    {
        await _participanteRepository.EnsureSimplesNacionalColumnsAsync(cancellationToken);

        var resumo = new SimplesNacionalConsultaResumo();
        var pendentes = participantes
            .Where(p => p.SimplesNacionalConsultadoEm is null)
            .Select(p => new { Cnpj = SomenteDigitos(p.Cnpj), p.NomeParticipante })
            .Where(p => p.Cnpj.Length == 14)
            .GroupBy(p => p.Cnpj)
            .Select(g => g.First())
            .ToList();

        resumo.Pendentes = pendentes.Count;

        foreach (var participante in pendentes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultado = await ConsultarCnpjAsync(participante.Cnpj, cancellationToken);
            await _participanteRepository.AtualizarSimplesNacionalAsync(empresaId, participante.Cnpj, resultado.SimplesNacional, resultado.Mensagem, cancellationToken);

            resumo.Consultados++;
            if (resultado.SimplesNacional == true)
            {
                resumo.Optantes++;
            }
            else if (resultado.SimplesNacional == false)
            {
                resumo.NaoOptantes++;
            }
            else
            {
                resumo.SemResposta++;
            }
        }

        return resumo;
    }

    private async Task<SimplesNacionalConsultaResultado> ConsultarCnpjAsync(string cnpj, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/cnpj/v1/{cnpj}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new SimplesNacionalConsultaResultado(null, "Limite da API publica atingido. Tente novamente mais tarde.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SimplesNacionalConsultaResultado(null, $"Consulta nao retornou dados: HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dados = await JsonSerializer.DeserializeAsync<BrasilApiCnpjResponse>(stream, cancellationToken: cancellationToken);
            if (dados is null)
            {
                return new SimplesNacionalConsultaResultado(null, "Resposta da API vazia.");
            }

            if (dados.OpcaoPeloSimples is null)
            {
                return new SimplesNacionalConsultaResultado(null, "API nao informou opcao pelo Simples Nacional.");
            }

            var mensagem = dados.OpcaoPeloSimples.Value
                ? "Optante pelo Simples Nacional."
                : "Nao optante pelo Simples Nacional.";

            return new SimplesNacionalConsultaResultado(dados.OpcaoPeloSimples.Value, mensagem);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SimplesNacionalConsultaResultado(null, "Tempo limite da consulta excedido.");
        }
        catch (HttpRequestException ex)
        {
            return new SimplesNacionalConsultaResultado(null, $"Falha na consulta publica: {ex.Message}");
        }
        catch (JsonException)
        {
            return new SimplesNacionalConsultaResultado(null, "Resposta da API em formato inesperado.");
        }
    }

    private static string SomenteDigitos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        return new string(valor.Where(char.IsDigit).ToArray());
    }

    private sealed record SimplesNacionalConsultaResultado(bool? SimplesNacional, string Mensagem);

    private sealed class BrasilApiCnpjResponse
    {
        [JsonPropertyName("opcao_pelo_simples")]
        public bool? OpcaoPeloSimples { get; set; }
    }
}

public sealed class SimplesNacionalConsultaResumo
{
    public int Pendentes { get; set; }
    public int Consultados { get; set; }
    public int Optantes { get; set; }
    public int NaoOptantes { get; set; }
    public int SemResposta { get; set; }
}
