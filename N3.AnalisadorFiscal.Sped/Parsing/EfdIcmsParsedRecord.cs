namespace N3.AnalisadorFiscal.Sped.Parsing;

public sealed class EfdIcmsParsedRecord
{
    public EfdIcmsParsedRecord(string tipo, object entidade)
    {
        Tipo = tipo;
        Entidade = entidade;
    }

    public string Tipo { get; }
    public object Entidade { get; }
}
