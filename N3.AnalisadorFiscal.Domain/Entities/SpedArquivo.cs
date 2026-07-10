namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class SpedArquivo
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string? HashArquivo { get; set; }
    public string? VersaoLeiaute { get; set; }
    public string? FinalidadeArquivo { get; set; }
    public DateTime PeriodoInicial { get; set; }
    public DateTime PeriodoFinal { get; set; }
    public DateTime ImportadoEm { get; set; } = DateTime.UtcNow;
}
