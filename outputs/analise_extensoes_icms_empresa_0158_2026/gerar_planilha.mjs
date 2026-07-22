import fs from "node:fs/promises";
import { Workbook, SpreadsheetFile } from "@oai/artifact-tool";
const dir=new URL("./",import.meta.url).pathname.replace(/^\/(.:)/,"$1");
const rows=JSON.parse((await fs.readFile(`${dir}dados.json`,"utf8")).replace(/^\uFEFF/,"")).rows;
if(!rows.length)throw new Error("Nenhum item encontrado.");
const n=v=>Number(v??0),t=v=>v==null?"":String(v),id=v=>v==null||String(v)===""?"":`\u200B${v}`;
const date=v=>{const m=String(v??"").match(/^\/Date\((\d+)/),d=new Date(m?Number(m[1]):v);return Number.isNaN(d.getTime())?null:d};
const round=v=>Math.round((Number(v)+Number.EPSILON)*100)/100;
const notes=new Set(rows.map(r=>t(r.ChaveNfe)||`${r.NumeroNota}|${r.DataEntrada}`));
const suppliers=[...new Set(rows.map(r=>`${r.Fornecedor}|${r.DocumentoFornecedor}`))];
const wb=Workbook.create(),s=wb.worksheets.add("Resumo"),d=wb.worksheets.add("Itens analisados");
const navy="#17365D",blue="#D9EAF7",red="#FCE8E6",border="#CBD5E1";
function head(sh,end,title,sub){sh.showGridLines=false;sh.getRange(`A1:${end}1`).merge();sh.getRange("A1").values=[[title]];sh.getRange(`A1:${end}1`).format={fill:navy,font:{bold:true,color:"#FFFFFF",size:16},rowHeight:30};sh.getRange(`A2:${end}2`).merge();sh.getRange("A2").values=[[sub]];sh.getRange(`A2:${end}2`).format={fill:blue,font:{italic:true,color:navy},rowHeight:24};}
head(s,"F","Extensões com ICMS destacado — análise do NCM",`${rows[0].Empresa} | Código Fortes 0158 | NCM esperado 85444200 | Fonte: base SQL do Analisador SPED`);
s.getRange("A4:F5").values=[["Fornecedores",suppliers.length,"Notas",notes.size,"Itens",rows.length],["Valor dos itens",round(rows.reduce((a,r)=>a+n(r.ValorItem),0)),"ICMS destacado",round(rows.reduce((a,r)=>a+n(r.IcmsDestacado),0)),"NCM divergente/ausente",rows.filter(r=>r.SituacaoNcm!=="NCM correto").length]];
s.getRange("A4:F5").format={fill:"#F5F8FC",borders:{preset:"all",style:"thin",color:border},font:{color:navy}};s.getRange("B5:D5").setNumberFormat("R$ #,##0.00");
s.getRange("A7:F8").values=[["Fornecedor","CNPJ/CPF","Qtd. notas","Qtd. itens","Valor dos itens","ICMS destacado"],[rows[0].Fornecedor,id(rows[0].DocumentoFornecedor),notes.size,rows.length,round(rows.reduce((a,r)=>a+n(r.ValorItem),0)),round(rows.reduce((a,r)=>a+n(r.IcmsDestacado),0))]];
s.tables.add("A7:F8",true,"ResumoFornecedorExtensaoIcms").style="TableStyleMedium2";s.getRange("B8").setNumberFormat("@");s.getRange("E8:F8").setNumberFormat("R$ #,##0.00");s.freezePanes.freezeRows(7);for(const[c,w]of Object.entries({A:42,B:20,C:14,D:14,E:28,F:18}))s.getRange(`${c}:${c}`).format.columnWidth=w;
head(d,"V","Itens iniciados por “EXTENSÃO” com ICMS maior que zero","NCM esperado 85444200 | Não houve correspondência exata: o NCM cadastrado está ausente nas ocorrências");
const h=["Fornecedor","CNPJ/CPF","Entrada","Emissão","Série","Nota","Chave NF-e","Item","Código produto","Produto","NCM cadastrado","NCM esperado","Situação NCM","CFOP","CST ICMS","Quantidade","Unidade","Valor item","Base ICMS","Alíquota ICMS","ICMS destacado","Situação doc."];
const data=rows.map(r=>[r.Fornecedor,id(r.DocumentoFornecedor),date(r.DataEntrada),date(r.DataEmissao),id(r.Serie),id(r.NumeroNota),id(r.ChaveNfe),id(r.NumeroItem),id(r.CodigoItem),r.Produto,id(r.NcmCadastrado),id(r.NcmEsperado),r.SituacaoNcm,id(r.Cfop),id(r.CstIcms),n(r.Quantidade),r.Unidade,n(r.ValorItem),n(r.BaseIcms),n(r.AliquotaIcms)/100,n(r.IcmsDestacado),id(r.SituacaoDocumento)]);
d.getRange(`A4:V${4+data.length}`).values=[h,...data];d.tables.add(`A4:V${4+data.length}`,true,"ItensExtensaoComIcms").style="TableStyleMedium2";d.getRange(`C5:D${4+data.length}`).setNumberFormat("dd/mm/yyyy");d.getRange(`B5:B${4+data.length}`).setNumberFormat("@");d.getRange(`E5:O${4+data.length}`).setNumberFormat("@");d.getRange(`R5:S${4+data.length}`).setNumberFormat("R$ #,##0.00");d.getRange(`T5:T${4+data.length}`).setNumberFormat("0.00%");d.getRange(`U5:U${4+data.length}`).setNumberFormat("R$ #,##0.00");d.getRange(`M5:M${4+data.length}`).format={fill:red,font:{bold:true,color:"#B91C1C"}};d.freezePanes.freezeRows(4);d.freezePanes.freezeColumns(2);
for(const[c,w]of Object.entries({A:38,B:18,C:13,D:13,E:8,F:12,G:46,H:8,I:16,J:46,K:16,L:16,M:22,N:9,O:11,P:13,Q:10,R:15,S:15,T:15,U:17,V:13}))d.getRange(`${c}:${c}`).format.columnWidth=w;
const x=await SpreadsheetFile.exportXlsx(wb);await x.save(`${dir}analise_extensoes_icms_maior_zero_empresa_0158_2026.xlsx`);
for(const[name,file]of[["Resumo","preview_resumo.png"],["Itens analisados","preview_itens.png"]]){const p=await wb.render({sheetName:name,autoCrop:"all",scale:1,format:"png"});await fs.writeFile(`${dir}${file}`,new Uint8Array(await p.arrayBuffer()));}
console.log((await wb.inspect({kind:"region",sheetId:"Resumo",range:"A1:F8",maxChars:2000})).ndjson);
console.log((await wb.inspect({kind:"region",sheetId:"Itens analisados",range:"A1:V7",maxChars:3000})).ndjson);
console.log((await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:100},maxChars:1200})).ndjson);
