namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class PreAnaliseRegraItemDto
{
    public int PreAnaliseRegraItemId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? UfDestino { get; set; }
    public string? CnpjFornecedor { get; set; }
    public string? NcmPrefixo { get; set; }
    public string? CestPrefixo { get; set; }
    public string? NcmExcecoes { get; set; }
    public string? TermosDescricao { get; set; }
    public string? CstsAplicaveis { get; set; }
    public string? CfopsAplicaveis { get; set; }
    public string Resultado { get; set; } = string.Empty;
    public decimal? PercentualCredito { get; set; }
    public int Prioridade { get; set; }
    public string? FundamentoLegal { get; set; }
    public DateTime VigenciaInicial { get; set; }
    public DateTime? VigenciaFinal { get; set; }
    public bool Ativa { get; set; }
}
