using System.Globalization;
using N3.AnalisadorFiscal.Domain.Entities;

namespace N3.AnalisadorFiscal.Data.Repositories;

public interface IEfdContribuicoesRepository
{
    Task<int> InsertAsync(EfdContribuicoesRegistro registro, int arquivoId, int empresaId,
        int? registroPaiId = null, int? participanteId = null, int? produtoId = null,
        CancellationToken cancellationToken = default);
}

public sealed class EfdContribuicoesRepository : RepositoryBase, IEfdContribuicoesRepository
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public EfdContribuicoesRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<int> InsertAsync(EfdContribuicoesRegistro r, int arquivoId, int empresaId,
        int? registroPaiId = null, int? participanteId = null, int? produtoId = null,
        CancellationToken cancellationToken = default)
    {
        var f = r.Campos;
        return r.Codigo switch
        {
            "C100" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_C100 (ID_ARQUIVO,ID_EMPRESA,ID_PARTICIPANTE,IND_OPER,IND_EMIT,COD_PART,COD_MOD,COD_SIT,SER,NUM_DOC,CHV_NFE,DT_DOC,DT_E_S,VL_DOC,VL_DESC,VL_MERC,IND_FRT,VL_FRT,VL_SEG,VL_OUT_DA,VL_BC_ICMS,VL_ICMS,VL_BC_ICMS_ST,VL_ICMS_ST,VL_IPI,VL_PIS,VL_COFINS,VL_PIS_ST,VL_COFINS_ST)
                OUTPUT INSERTED.ID_CONTRIB_C100 VALUES (@ArquivoId,@EmpresaId,@ParticipanteId,@P2,@P3,@P4,@P5,@P6,@P7,@P8,@P9,@D10,@D11,@N12,@N14,@N16,@P17,@N18,@N19,@N20,@N21,@N22,@N23,@N24,@N25,@N26,@N27,@N28,@N29);
                """, Params(f, arquivoId, empresaId, participanteId: participanteId), cancellationToken),
            "C170" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_C170 (ID_CONTRIB_C100,ID_PRODUTO,NUM_ITEM,COD_ITEM,DESCR_COMPL,QTD,UNID,VL_ITEM,VL_DESC,CST_ICMS,CFOP,NAT_BC_CRED,VL_BC_ICMS,ALIQ_ICMS,VL_ICMS,VL_BC_ICMS_ST,ALIQ_ST,VL_ICMS_ST,IND_APUR,CST_IPI,COD_ENQ,VL_BC_IPI,ALIQ_IPI,VL_IPI,CST_PIS,VL_BC_PIS,ALIQ_PIS,QUANT_BC_PIS,ALIQ_PIS_QUANT,VL_PIS,CST_COFINS,VL_BC_COFINS,ALIQ_COFINS,QUANT_BC_COFINS,ALIQ_COFINS_QUANT,VL_COFINS,COD_CTA)
                OUTPUT INSERTED.ID_CONTRIB_C170 VALUES (@PaiId,@ProdutoId,@I2,@P3,@P4,@N5,@P6,@N7,@N8,@P10,@P11,@P12,@N13,@N14,@N15,@N16,@N17,@N18,@P19,@P20,@P21,@N22,@N23,@N24,@P25,@N26,@N27,@N28,@N29,@N30,@P31,@N32,@N33,@N34,@N35,@N36,@P37);
                """, Params(f, arquivoId, empresaId, registroPaiId, produtoId: produtoId), cancellationToken),
            "A100" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_A100 (ID_ARQUIVO,ID_EMPRESA,ID_PARTICIPANTE,IND_OPER,IND_EMIT,COD_PART,COD_SIT,SER,SUB,NUM_DOC,CHV_NFSE,DT_DOC,DT_EXE_SERV,VL_DOC,VL_DESC,VL_BC_PIS,VL_PIS,VL_BC_COFINS,VL_COFINS,VL_PIS_RET,VL_COFINS_RET)
                OUTPUT INSERTED.ID_CONTRIB_A100 VALUES (@ArquivoId,@EmpresaId,@ParticipanteId,@P2,@P3,@P4,@P5,@P6,@P7,@P8,@P9,@D10,@D11,@N12,@N13,@N14,@N15,@N16,@N17,@N18,@N19);
                """, Params(f, arquivoId, empresaId, participanteId: participanteId), cancellationToken),
            "A170" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_A170 (ID_CONTRIB_A100,NUM_ITEM,COD_ITEM,DESCR_COMPL,VL_ITEM,VL_DESC,NAT_BC_CRED,IND_ORIG_CRED,CST_PIS,VL_BC_PIS,ALIQ_PIS,VL_PIS,CST_COFINS,VL_BC_COFINS,ALIQ_COFINS,VL_COFINS,COD_CTA)
                OUTPUT INSERTED.ID_CONTRIB_A170 VALUES (@PaiId,@I2,@P3,@P4,@N5,@N6,@P7,@P8,@P9,@N10,@N11,@N12,@P13,@N14,@N15,@N16,@P17);
                """, Params(f, arquivoId, empresaId, registroPaiId), cancellationToken),
            "M100" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_M100 (ID_ARQUIVO,COD_CRED,IND_CRED_ORI,VL_BC_PIS,ALIQ_PIS,QUANT_BC_PIS,ALIQ_PIS_QUANT,VL_CRED,VL_AJUS_ACRES,VL_AJUS_REDUC,VL_CRED_DIF,VL_CRED_DISP,IND_DESC_CRED,VL_CRED_DESC,SLD_CRED)
                OUTPUT INSERTED.ID_CONTRIB_M100 VALUES (@ArquivoId,@P2,@P3,@N4,@N5,@N6,@N7,@N8,@N9,@N10,@N11,@N12,@P13,@N14,@N15);
                """, Params(f, arquivoId, empresaId), cancellationToken),
            "M200" => InsertResumoAsync("EFD_CONTRIB_M200", "ID_CONTRIB_M200", f, arquivoId, empresaId, cancellationToken),
            "M500" => InsertAsync("""
                INSERT INTO EFD_CONTRIB_M500 (ID_ARQUIVO,COD_CRED,IND_CRED_ORI,VL_BC_COFINS,ALIQ_COFINS,QUANT_BC_COFINS,ALIQ_COFINS_QUANT,VL_CRED,VL_AJUS_ACRES,VL_AJUS_REDUC,VL_CRED_DIF,VL_CRED_DISP,IND_DESC_CRED,VL_CRED_DESC,SLD_CRED)
                OUTPUT INSERTED.ID_CONTRIB_M500 VALUES (@ArquivoId,@P2,@P3,@N4,@N5,@N6,@N7,@N8,@N9,@N10,@N11,@N12,@P13,@N14,@N15);
                """, Params(f, arquivoId, empresaId), cancellationToken),
            "M600" => InsertResumoAsync("EFD_CONTRIB_M600", "ID_CONTRIB_M600", f, arquivoId, empresaId, cancellationToken),
            _ => Task.FromResult(0)
        };
    }

    private Task<int> InsertResumoAsync(string tabela, string id, string[] f, int arquivoId, int empresaId, CancellationToken ct)
        => InsertAsync($"""
            INSERT INTO {tabela} (ID_ARQUIVO,VL_TOT_CONT_NC_PER,VL_TOT_CRED_DESC,VL_TOT_CRED_DESC_ANT,VL_TOT_CONT_NC_DEV,VL_RET_NC,VL_OUT_DED_NC,VL_CONT_NC_REC,VL_TOT_CONT_CUM_PER,VL_RET_CUM,VL_OUT_DED_CUM,VL_CONT_CUM_REC,VL_TOT_CONT_REC)
            OUTPUT INSERTED.{id} VALUES (@ArquivoId,@N2,@N3,@N4,@N5,@N6,@N7,@N8,@N9,@N10,@N11,@N12,@N13);
            """, Params(f, arquivoId, empresaId), ct);

    private static IDictionary<string, object?> Params(string[] f, int arquivoId, int empresaId,
        int? paiId = null, int? participanteId = null, int? produtoId = null)
    {
        var p = new Dictionary<string, object?> { ["ArquivoId"] = arquivoId, ["EmpresaId"] = empresaId,
            ["PaiId"] = paiId, ["ParticipanteId"] = participanteId, ["ProdutoId"] = produtoId };
        for (var i = 2; i <= 40; i++)
        {
            var value = i < f.Length ? f[i].Trim() : string.Empty;
            p[$"P{i}"] = string.IsNullOrEmpty(value) ? null : value;
            p[$"N{i}"] = decimal.TryParse(value, NumberStyles.Number, PtBr, out var n) ? n : 0m;
            p[$"I{i}"] = int.TryParse(value, out var number) ? number : null;
            p[$"D{i}"] = DateTime.TryParseExact(value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }
        return p;
    }
}
