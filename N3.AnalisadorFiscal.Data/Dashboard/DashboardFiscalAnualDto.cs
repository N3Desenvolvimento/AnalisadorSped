namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardFiscalAnualDto
{
    public int Ano { get; set; }
    public List<DashboardFiscalMesDto> Meses { get; set; } = [];
}
