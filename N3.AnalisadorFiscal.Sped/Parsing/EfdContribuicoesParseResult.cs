using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Sped.Parsing;

public sealed class EfdContribuicoesParseResult
{
    public Empresa? Empresa { get; set; }
    public SpedArquivo? Arquivo { get; set; }
    public List<Participante> Participantes { get; } = [];
    public List<UnidadeMedida> UnidadesMedida { get; } = [];
    public List<Produto> Produtos { get; } = [];
    public List<EfdContribuicoesRegistro> RegistrosContribuicoes { get; } = [];
    public List<object> Registros { get; } = [];
}
