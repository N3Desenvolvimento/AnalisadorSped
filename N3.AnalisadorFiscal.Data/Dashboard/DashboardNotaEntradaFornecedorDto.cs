namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardNotaEntradaFornecedorDto
{
    public int SpedC100Id { get; set; }
    public string CodigoParticipante { get; set; } = string.Empty;
    public string NomeParticipante { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public string CodigoModelo { get; set; } = string.Empty;
    public string CodigoSituacao { get; set; } = string.Empty;
    public string Serie { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string? ChaveNfe { get; set; }
    public DateTime? DataDocumento { get; set; }
    public DateTime? DataEntradaSaida { get; set; }
    public decimal ValorDocumento { get; set; }
    public decimal ValorMercadoria { get; set; }
    public decimal ValorBaseIcms { get; set; }
    public decimal ValorIcmsCredito { get; set; }
    public decimal AliquotaEfetivaCredito { get; set; }
}
