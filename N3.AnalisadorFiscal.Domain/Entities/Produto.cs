namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class Produto
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SpedArquivoId { get; set; }
    public int? UnidadeMedidaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string? CodigoBarra { get; set; }
    public string? CodigoAnterior { get; set; }
    public string? Unidade { get; set; }
    public string? TipoItem { get; set; }
    public string? CodigoNcm { get; set; }
    public string? ExIpi { get; set; }
    public string? CodigoGenero { get; set; }
    public string? CodigoLst { get; set; }
    public decimal? AliquotaIcms { get; set; }
}


