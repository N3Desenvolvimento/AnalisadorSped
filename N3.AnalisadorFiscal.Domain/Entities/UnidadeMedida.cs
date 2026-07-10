namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class UnidadeMedida
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SpedArquivoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
}


