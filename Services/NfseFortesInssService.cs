using Dapper;
using FirebirdSql.Data.FirebirdClient;
using N3.AnalisadorFiscal.Data.Repositories;

namespace N3.AnalisadorFiscal.Web.Services;

public interface INfseFortesInssService
{
    Task<NfseFortesInssResultado> PreencherAsync(
        int empresaId,
        string chaveAcesso,
        decimal baseCalculo,
        decimal valorInssRetido,
        CancellationToken cancellationToken = default);
}

public sealed record NfseFortesInssResultado(
    string NumeroNota,
    int QuantidadeItens,
    decimal BaseCalculo,
    decimal Aliquota,
    decimal ValorInssRetido,
    bool SobrescreveuValores);

public sealed class NfseFortesInssService(
    IConfiguration configuration,
    IEmpresaRepository empresas) : INfseFortesInssService
{
    public async Task<NfseFortesInssResultado> PreencherAsync(
        int empresaId,
        string chaveAcesso,
        decimal baseCalculo,
        decimal valorInssRetido,
        CancellationToken cancellationToken = default)
    {
        if (baseCalculo <= 0)
            throw new InvalidOperationException("Informe uma base de cálculo do INSS maior que zero.");
        if (valorInssRetido <= 0)
            throw new InvalidOperationException("Informe um valor de INSS retido maior que zero.");
        if (valorInssRetido > baseCalculo)
            throw new InvalidOperationException("O INSS retido não pode ser maior que a base de cálculo.");

        var chave = SomenteLetrasENumeros(chaveAcesso);
        if (chave.Length != 50)
            throw new InvalidOperationException("A chave de acesso da NFS-e deve possuir 50 caracteres.");

        var empresa = await empresas.GetByIdAsync(empresaId, cancellationToken)
            ?? throw new InvalidOperationException("Empresa não encontrada.");
        var codigoFortes = empresa.CodigoEmpresaFiscalFortes?.Trim();
        if (string.IsNullOrWhiteSpace(codigoFortes))
            throw new InvalidOperationException("Informe o código da empresa no Fiscal Fortes no cadastro da empresa.");
        if (codigoFortes.Length > 4)
            throw new InvalidOperationException("O código da empresa no Fiscal Fortes deve possuir no máximo 4 caracteres.");

        var connectionString = configuration.GetConnectionString("FortesFolha")
            ?? throw new InvalidOperationException("A conexão com o Fortes não foi configurada.");
        var builder = new FbConnectionStringBuilder(connectionString);
        var senha = configuration["FortesFolha:Password"];
        if (string.IsNullOrWhiteSpace(senha))
            senha = Environment.GetEnvironmentVariable("FORTES_FOLHA_PASSWORD");
        if (string.IsNullOrWhiteSpace(senha))
            throw new InvalidOperationException(
                "A senha do banco do Fortes não foi configurada. Use o segredo FortesFolha:Password ou a variável FORTES_FOLHA_PASSWORD.");
        builder.Password = senha;

        await using var connection = new FbConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var nota = await connection.QuerySingleOrDefaultAsync<NotaFortes>(new CommandDefinition("""
            SELECT d.SEQ AS NotaSequencial, TRIM(d.NRINICIAL) AS NumeroNota
            FROM DSS d
            WHERE d.EMP_CODIGO=@CodigoFortes
              AND d.CHAVEELET=@ChaveAcesso
              AND COALESCE(d.CANCELADO, 0)=0
            """, new { CodigoFortes = codigoFortes, ChaveAcesso = chave }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));

        if (nota is null)
            throw new InvalidOperationException(
                "A NFS-e não foi localizada nas notas fiscais de serviço eletrônico de saída do Fortes pela chave de acesso. Importe ou cadastre a nota no Fortes antes de preencher o INSS.");

        var itens = (await connection.QueryAsync<ItemServicoFortes>(new CommandDefinition("""
            SELECT i.SEQ AS ItemSequencial,
                   CASE
                     WHEN COALESCE(i.SERVICOS, 0)>0 THEN i.SERVICOS
                     WHEN COALESCE(i.VRBRUTO, 0)>0 THEN i.VRBRUTO
                     ELSE COALESCE(i.SUBTOTAL, 0)
                   END AS ValorItem,
                   COALESCE(i.TFBCRETFONTEINSS, 0) AS BaseInssAtual,
                   COALESCE(i.VRRETFONTEINSS, 0) AS ValorInssAtual
            FROM ITS i
            WHERE i.EMP_CODIGO=@CodigoFortes AND i.DSS_SEQ=@NotaSequencial
            ORDER BY i.SEQ
            """, new { CodigoFortes = codigoFortes, nota.NotaSequencial }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken))).AsList();

        if (itens.Count == 0)
            throw new InvalidOperationException("A nota foi localizada no Fortes, mas não possui item de serviço para receber os valores do INSS.");

        var sobrescreveu = itens.Any(x => x.BaseInssAtual != 0 || x.ValorInssAtual != 0);
        var bases = Distribuir(baseCalculo, itens);
        var valores = Distribuir(valorInssRetido, itens);

        for (var indice = 0; indice < itens.Count; indice++)
        {
            var item = itens[indice];
            var baseItem = bases[indice];
            var valorItem = valores[indice];
            var aliquotaItem = baseItem == 0 ? 0 : Math.Round(valorItem / baseItem * 100m, 4);

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ITS
                   SET TFBCRETFONTEINSS=@BaseCalculo,
                       ALIQINSS=@Aliquota,
                       VRRETFONTEINSS=@ValorInssRetido
                 WHERE EMP_CODIGO=@CodigoFortes
                   AND DSS_SEQ=@NotaSequencial
                   AND SEQ=@ItemSequencial
                """, new
                {
                    BaseCalculo = baseItem,
                    Aliquota = aliquotaItem,
                    ValorInssRetido = valorItem,
                    CodigoFortes = codigoFortes,
                    nota.NotaSequencial,
                    item.ItemSequencial
                }, transaction, commandTimeout: 120, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE DSS
               SET TFRETFONTEINSS=@ValorInssRetido
             WHERE EMP_CODIGO=@CodigoFortes AND SEQ=@NotaSequencial
            """, new { CodigoFortes = codigoFortes, nota.NotaSequencial, ValorInssRetido = valorInssRetido }, transaction,
            commandTimeout: 120, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new NfseFortesInssResultado(
            nota.NumeroNota,
            itens.Count,
            baseCalculo,
            Math.Round(valorInssRetido / baseCalculo * 100m, 4),
            valorInssRetido,
            sobrescreveu);
    }

    private static decimal[] Distribuir(decimal total, IReadOnlyList<ItemServicoFortes> itens)
    {
        var resultado = new decimal[itens.Count];
        if (itens.Count == 1)
        {
            resultado[0] = total;
            return resultado;
        }

        var pesoTotal = itens.Sum(x => Math.Max(0, x.ValorItem));
        var distribuido = 0m;
        for (var indice = 0; indice < itens.Count; indice++)
        {
            if (indice == itens.Count - 1)
            {
                resultado[indice] = total - distribuido;
                continue;
            }

            var proporcao = pesoTotal > 0
                ? Math.Max(0, itens[indice].ValorItem) / pesoTotal
                : 1m / itens.Count;
            resultado[indice] = Math.Round(total * proporcao, 2);
            distribuido += resultado[indice];
        }

        return resultado;
    }

    private static string SomenteLetrasENumeros(string valor) =>
        new(valor.Where(char.IsLetterOrDigit).ToArray());

    private sealed class NotaFortes
    {
        public int NotaSequencial { get; set; }
        public string NumeroNota { get; set; } = string.Empty;
    }

    private sealed class ItemServicoFortes
    {
        public int ItemSequencial { get; set; }
        public decimal ValorItem { get; set; }
        public decimal BaseInssAtual { get; set; }
        public decimal ValorInssAtual { get; set; }
    }
}
