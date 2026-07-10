using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Sped.Parsing;

public sealed class EfdIcmsParseResult
{
    public Empresa? Empresa { get; set; }
    public SpedArquivo? Arquivo { get; set; }
    public List<Participante> Participantes { get; } = [];
    public List<UnidadeMedida> UnidadesMedida { get; } = [];
    public List<Produto> Produtos { get; } = [];
    public List<SpedC100> C100 { get; } = [];
    public List<SpedC170> C170 { get; } = [];
    public List<SpedC190> C190 { get; } = [];
    public List<SpedE110> E110 { get; } = [];
    public List<SpedE111> E111 { get; } = [];
    public List<EfdIcmsParsedRecord> Registros { get; } = [];
}

