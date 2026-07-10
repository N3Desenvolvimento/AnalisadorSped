namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardFiscalCfopCstDto
{
    public string IndicadorOperacao { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string CstIcms { get; set; } = string.Empty;
    public decimal ValorOperacao { get; set; }
    public decimal BaseIcms { get; set; }
    public decimal ValorIcms { get; set; }
}
