using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class PreAnaliseSpedDados
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string HashArquivo { get; set; } = string.Empty;
    public Empresa Empresa { get; set; } = new();
    public SpedArquivo Arquivo { get; set; } = new();
    public List<PreAnaliseSpedNotaDados> Notas { get; } = [];
}

public sealed class PreAnaliseSpedNotaDados
{
    public SpedC100 Nota { get; set; } = new();
    public string NomeParticipante { get; set; } = string.Empty;
    public string CnpjParticipante { get; set; } = string.Empty;
    public decimal ValorBaseIcms { get; set; }
    public decimal ValorIcmsCredito { get; set; }
    public List<PreAnaliseSpedItemDados> Itens { get; } = [];
}

public sealed class PreAnaliseSpedItemDados
{
    public SpedC170 Item { get; set; } = new();
    public string Descricao { get; set; } = string.Empty;
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    public int? PreAnaliseRegraItemId { get; set; }
    public string Classificacao { get; set; } = "PENDENTE";
    public string? NivelConfianca { get; set; }
    public string? Justificativa { get; set; }
    public decimal? CreditoPermitido { get; set; }
    public decimal? DiferencaCredito { get; set; }
}
