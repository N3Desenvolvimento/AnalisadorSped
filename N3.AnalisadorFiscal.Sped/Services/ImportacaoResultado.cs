namespace N3.AnalisadorFiscal.Sped.Services;

public sealed class ImportacaoResultado
{
    public bool Sucesso { get; set; }
    public bool ArquivoValido { get; set; }
    public bool ArquivoJaImportado { get; set; }
    public bool EmpresaPeriodoJaImportado { get; set; }
    public int? SpedArquivoId { get; set; }
    public string HashArquivo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public string? Uf { get; set; }
    public string? InscricaoEstadual { get; set; }
    public DateTime? PeriodoInicial { get; set; }
    public DateTime? PeriodoFinal { get; set; }
    public int ParticipantesImportados { get; set; }
    public int UnidadesMedidaImportadas { get; set; }
    public int ProdutosImportados { get; set; }
    public int C100Importados { get; set; }
    public int C170Importados { get; set; }
    public int C190Importados { get; set; }
    public int E110Importados { get; set; }
    public int E111Importados { get; set; }
    public int A100Importados { get; set; }
    public int A170Importados { get; set; }
    public int M100Importados { get; set; }
    public int M200Importados { get; set; }
    public int M500Importados { get; set; }
    public int M600Importados { get; set; }
    public int TotalRegistrosImportados => ParticipantesImportados
        + UnidadesMedidaImportadas
        + ProdutosImportados
        + C100Importados
        + C170Importados
        + C190Importados
        + E110Importados
        + E111Importados
        + A100Importados + A170Importados
        + M100Importados + M200Importados + M500Importados + M600Importados;
    public List<string> Mensagens { get; } = [];
}
