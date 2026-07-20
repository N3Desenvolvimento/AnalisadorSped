using System.Globalization;
using System.Text;
using N3.AnalisadorFiscal.Data.Dashboard;

namespace N3.AnalisadorFiscal.Sped.Services;

public interface IPreAnaliseItemRuleService
{
    void Aplicar(PreAnaliseSpedDados dados, IReadOnlyList<PreAnaliseRegraItemDto> regras);
}

public sealed class PreAnaliseItemRuleService : IPreAnaliseItemRuleService
{
    public void Aplicar(PreAnaliseSpedDados dados, IReadOnlyList<PreAnaliseRegraItemDto> regras)
    {
        foreach (var nota in dados.Notas)
        foreach (var item in nota.Itens)
            AplicarAoItem(item, nota.CnpjParticipante, regras);
    }

    private static void AplicarAoItem(
        PreAnaliseSpedItemDados item, string? cnpjFornecedor,
        IReadOnlyList<PreAnaliseRegraItemDto> regras)
    {
        var ncm = SomenteDigitos(item.Ncm);
        var descricao = NormalizarTexto(item.Descricao);
        PreAnaliseRegraItemDto? regraParcial = null;
        string? motivoParcial = null;

        foreach (var regra in regras)
        {
            if (!CorrespondeAoFornecedor(cnpjFornecedor, regra.CnpjFornecedor))
                continue;

            if (!CorrespondeALista(item.Item.CstIcms, regra.CstsAplicaveis) ||
                !CorrespondeALista(item.Item.Cfop, regra.CfopsAplicaveis))
                continue;

            var excecoes = Separar(regra.NcmExcecoes).Select(SomenteDigitos);
            if (ncm.Length > 0 && excecoes.Any(ncm.StartsWith))
                continue;

            var prefixo = SomenteDigitos(regra.NcmPrefixo);
            var ncmCorresponde = prefixo.Length == 0 ||
                                 (ncm.Length > 0 && ncm.StartsWith(prefixo, StringComparison.Ordinal));
            var termos = Separar(regra.TermosDescricao).Select(NormalizarTexto).ToArray();
            var descricaoCorresponde = termos.Length == 0 || termos.Any(descricao.Contains);

            if (ncmCorresponde && descricaoCorresponde)
            {
                AplicarRegraConfirmada(item, regra);
                return;
            }

            if (regraParcial is null && (ncmCorresponde || descricaoCorresponde))
            {
                regraParcial = regra;
                motivoParcial = ncmCorresponde
                    ? "O NCM corresponde à regra, mas a descrição precisa ser confirmada."
                    : "A descrição sugere enquadramento, mas o NCM não confirma a regra.";
            }
        }

        if (regraParcial is not null)
            AplicarRegraParcial(item, regraParcial, motivoParcial!);
    }

    private static void AplicarRegraConfirmada(
        PreAnaliseSpedItemDados item, PreAnaliseRegraItemDto regra)
    {
        item.PreAnaliseRegraItemId = regra.PreAnaliseRegraItemId;
        item.Classificacao = regra.Resultado;
        item.NivelConfianca = "ALTA";
        item.CreditoPermitido = regra.PercentualCredito.HasValue
            ? Math.Round((item.Item.ValorItem - item.Item.ValorDesconto) *
                         regra.PercentualCredito.Value / 100m, 2)
            : null;
        item.DiferencaCredito = item.CreditoPermitido.HasValue
            ? item.Item.ValorIcms - item.CreditoPermitido.Value
            : null;
        item.Justificativa = MontarJustificativa(regra,
            "O NCM está relacionado como produto sujeito à substituição tributária sem direito a crédito no RN.");
    }

    private static void AplicarRegraParcial(
        PreAnaliseSpedItemDados item, PreAnaliseRegraItemDto regra, string motivo)
    {
        item.PreAnaliseRegraItemId = regra.PreAnaliseRegraItemId;
        item.Classificacao = "REVISAR";
        item.NivelConfianca = string.IsNullOrWhiteSpace(item.Ncm) ? "BAIXA" : "MEDIA";
        item.CreditoPermitido = null;
        item.DiferencaCredito = null;
        item.Justificativa = MontarJustificativa(regra, motivo);
    }

    private static string MontarJustificativa(PreAnaliseRegraItemDto regra, string motivo)
    {
        var fundamento = string.IsNullOrWhiteSpace(regra.FundamentoLegal)
            ? string.Empty
            : $" Fundamento: {regra.FundamentoLegal}";
        return $"{regra.Nome}. {motivo}{fundamento}";
    }

    private static bool CorrespondeALista(string valor, string? lista)
    {
        var valores = Separar(lista);
        return valores.Length == 0 || valores.Contains(valor.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool CorrespondeAoFornecedor(string? cnpjFornecedor, string? fornecedoresDaRegra)
    {
        var fornecedores = Separar(fornecedoresDaRegra).Select(SomenteDigitos).ToArray();
        if (fornecedores.Length == 0) return true;
        var cnpj = SomenteDigitos(cnpjFornecedor);
        return cnpj.Length > 0 && fornecedores.Contains(cnpj, StringComparer.Ordinal);
    }

    private static string[] Separar(string? valor) => string.IsNullOrWhiteSpace(valor)
        ? []
        : valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SomenteDigitos(string? valor) => string.IsNullOrWhiteSpace(valor)
        ? string.Empty
        : new string(valor.Where(char.IsDigit).ToArray());

    private static string NormalizarTexto(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var decomposed = valor.Normalize(NormalizationForm.FormD);
        var semAcentos = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return string.Join(' ', semAcentos
            .ToUpperInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .Aggregate(new StringBuilder(), (texto, c) =>
            {
                if (c != ' ' || texto.Length == 0 || texto[^1] != ' ') texto.Append(c);
                return texto;
            })
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
