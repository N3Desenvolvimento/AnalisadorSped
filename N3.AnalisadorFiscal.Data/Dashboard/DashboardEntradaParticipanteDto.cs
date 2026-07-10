namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class DashboardEntradaParticipanteDto
{
    public int ParticipanteId { get; set; }
    public string CodigoParticipante { get; set; } = string.Empty;
    public string NomeParticipante { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public bool? SimplesNacional { get; set; }
    public DateTime? SimplesNacionalConsultadoEm { get; set; }
    public string? SimplesNacionalMensagem { get; set; }
    public decimal ValorEntrada { get; set; }
    public decimal BaseCalculo { get; set; }
    public decimal ValorIcmsCredito { get; set; }
    public decimal AliquotaEfetivaCredito { get; set; }
    public decimal AliquotaIcms { get; set; }
}
