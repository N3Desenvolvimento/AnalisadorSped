namespace N3.AnalisadorFiscal.Data.Dashboard;

public sealed class FolhaEspelhoDto
{
    public int EmpresaId { get; set; }
    public string RazaoSocial { get; set; } = string.Empty;
    public int Ano { get; set; }
    public int Mes { get; set; }
    public DateTime ImportadoEm { get; set; }
    public IReadOnlyList<FolhaEspelhoTrabalhadorDto> Trabalhadores { get; set; } = [];
    public decimal TotalProventos => Trabalhadores.Sum(x => x.TotalProventos);
    public decimal TotalDescontos => Trabalhadores.Sum(x => x.TotalDescontos);
    public decimal TotalLiquido => TotalProventos - TotalDescontos;
}

public sealed class FolhaEspelhoTrabalhadorDto
{
    public int Id { get; set; }
    public string CodigoFortes { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public IReadOnlyList<FolhaEspelhoEventoDto> Eventos { get; set; } = [];
    public decimal TotalProventos => Eventos.Where(x => x.TipoEvento == 1).Sum(x => x.Valor);
    public decimal TotalDescontos => Eventos.Where(x => x.TipoEvento == -1).Sum(x => x.Valor);
    public decimal Liquido => TotalProventos - TotalDescontos;
}

public class FolhaEspelhoEventoDto
{
    public string CodigoFortes { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public int TipoEvento { get; set; }
    public decimal Referencia { get; set; }
    public decimal Valor { get; set; }
}

public sealed class FolhaResumoMesDto
{
    public int Mes { get; set; }
    public decimal ValorFolha { get; set; }
    public decimal ValorInssPatronal { get; set; }
    public decimal ValorFgts { get; set; }
}
