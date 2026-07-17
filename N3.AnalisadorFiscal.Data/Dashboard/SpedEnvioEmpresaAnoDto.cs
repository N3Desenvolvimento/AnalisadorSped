namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class SpedEnvioEmpresaAnoDto
{
    public int EmpresaId { get; set; }
    public string RazaoSocial { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public IReadOnlySet<int> MesesEnviados { get; set; } = new HashSet<int>();
}

