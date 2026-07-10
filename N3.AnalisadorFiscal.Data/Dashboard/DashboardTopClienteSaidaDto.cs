namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardTopClienteSaidaDto
{
    public string CodigoParticipante { get; set; } = string.Empty;
    public string NomeParticipante { get; set; } = string.Empty;
    public int QuantidadeNotas { get; set; }
    public decimal ValorSaida { get; set; }
}
