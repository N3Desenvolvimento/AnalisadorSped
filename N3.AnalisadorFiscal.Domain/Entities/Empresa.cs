namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class Empresa
{
    public int Id { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? Uf { get; set; }
    public string? CodigoMunicipio { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
