namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardFiscalDto
{
    public DashboardFiscalResumoDto Resumo { get; set; } = new();
    public List<DashboardFiscalCfopCstDto> ResumoPorCfopCst { get; set; } = [];
}
