namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardPisCofinsCfopCstDto
{
    public string IndicadorOperacao { get; set; } = string.Empty;
    public string Cfop { get; set; } = string.Empty;
    public string CstPis { get; set; } = string.Empty;
    public string CstCofins { get; set; } = string.Empty;
    public decimal ValorOperacao { get; set; }
    public decimal BasePis { get; set; }
    public decimal ValorPis { get; set; }
    public decimal BaseCofins { get; set; }
    public decimal ValorCofins { get; set; }
}
