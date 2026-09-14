namespace N3.AnalisadorFiscal.Domain.Entities;

public sealed class FaqArtigo
{
    public int Id { get; set; }
    public string Pergunta { get; set; } = string.Empty;
    public string Resposta { get; set; } = string.Empty;
    public string Categoria { get; set; } = "Geral";
    public string? PalavrasChave { get; set; }
    public bool Publicado { get; set; } = true;
    public bool Ativo { get; set; } = true;
    public string Origem { get; set; } = "Manual";
    public string? ReferenciaExterna { get; set; }
    public string? UrlOrigem { get; set; }
    public string? Autor { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public int TotalUtil { get; set; }
    public int TotalNaoUtil { get; set; }
}
