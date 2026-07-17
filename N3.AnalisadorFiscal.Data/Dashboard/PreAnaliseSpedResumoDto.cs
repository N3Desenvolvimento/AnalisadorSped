namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class PreAnaliseSpedResumoDto
{
    public int PreAnaliseSpedId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public DateTime PeriodoInicial { get; set; }
    public DateTime PeriodoFinal { get; set; }
    public DateTime ImportadoEm { get; set; }
    public int TotalNotas { get; set; }
    public int TotalItens { get; set; }
    public decimal ValorTotalNotas { get; set; }
    public decimal BaseIcmsTotal { get; set; }
    public decimal IcmsCreditoTotal { get; set; }
}
