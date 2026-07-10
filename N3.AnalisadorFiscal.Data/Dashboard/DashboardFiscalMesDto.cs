namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardFiscalMesDto
{
    public int Mes { get; set; }
    public string NomeMes { get; set; } = string.Empty;
    public bool ArquivoImportado { get; set; }
    public string? NomeArquivo { get; set; }
    public DateTime? DataImportacao { get; set; }
    public decimal ValorTotalEntradas { get; set; }
    public decimal ValorTotalSaidas { get; set; }
    public decimal BaseIcms { get; set; }
    public decimal IcmsDebitado { get; set; }
    public decimal IcmsCreditado { get; set; }
    public decimal IcmsARecolher { get; set; }
    public decimal? ValorPrincipalGuiaIcms { get; set; }
    public string? SituacaoGuiaIcms { get; set; }
}
