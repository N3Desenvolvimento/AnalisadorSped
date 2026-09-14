namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardFiscalResumoDto
{
    public decimal ValorTotalEntradas { get; set; }
    public decimal ValorTotalSaidas { get; set; }
    public decimal BaseIcms { get; set; }
    public decimal IcmsDebitado { get; set; }
    public decimal IcmsCreditado { get; set; }
    public decimal IcmsAntecipado { get; set; }
    public decimal IcmsCreditoEstoque { get; set; }
    public decimal IcmsARecolher { get; set; }
}
