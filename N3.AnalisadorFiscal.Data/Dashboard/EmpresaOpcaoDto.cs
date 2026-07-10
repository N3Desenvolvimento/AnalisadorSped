namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class EmpresaOpcaoDto
{
    public int Id { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string? Uf { get; set; }
}
