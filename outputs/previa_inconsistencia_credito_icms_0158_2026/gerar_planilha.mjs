import fs from "node:fs/promises";
import { Workbook, SpreadsheetFile } from "@oai/artifact-tool";
const dir=new URL("./",import.meta.url).pathname.replace(/^\/(.:)/,"$1");
const payload=JSON.parse((await fs.readFile(`${dir}dados.json`,"utf8")).replace(/^\uFEFF/,"")),rows=payload.rows;
if(!rows.length)throw new Error("Nenhuma inconsistência encontrada.");
const n=v=>Number(v??0),t=v=>v==null?"":String(v),id=v=>v==null||String(v)===""?"":`\u200B${v}`;
const date=v=>{const m=String(v??"").match(/^\/Date\((\d+)/),d=new Date(m?Number(m[1]):v);return Number.isNaN(d.getTime())?null:d};
const round=v=>Math.round((Number(v)+Number.EPSILON)*100)/100,noteKey=r=>t(r.ChaveNfe)||`${r.DocumentoFornecedor}|${r.NumeroNota}|${r.DataEntrada}`;
const prod=new Map(),sup=new Map();
for(const r of rows){
  const pk=t(r.CodigoItem);if(!prod.has(pk))prod.set(pk,{codigo:pk,produto:t(r.Produto),ncm:t(r.Ncm),com:0,sem:0,forn:new Set(),notas:new Set(),cfops:new Set(),csts:new Set(),valor:0,base:0,icms:0});const p=prod.get(pk);p[r.IcmsDestacado>0?"com":"sem"]++;p.forn.add(t(r.DocumentoFornecedor));p.notas.add(noteKey(r));p.cfops.add(t(r.Cfop));p.csts.add(t(r.CstIcms));p.valor+=n(r.ValorItem);p.base+=n(r.BaseIcms);p.icms+=n(r.IcmsDestacado);
  const sk=`${r.Fornecedor}|${r.DocumentoFornecedor}`;if(!sup.has(sk))sup.set(sk,{nome:t(r.Fornecedor),doc:t(r.DocumentoFornecedor),prod:new Set(),notas:new Set(),com:0,sem:0,valor:0,icms:0});const s=sup.get(sk);s.prod.add(pk);s.notas.add(noteKey(r));s[r.IcmsDestacado>0?"com":"sem"]++;s.valor+=n(r.ValorItem);s.icms+=n(r.IcmsDestacado);
}
const products=[...prod.values()].sort((a,b)=>(b.com+b.sem)-(a.com+a.sem)),suppliers=[...sup.values()].sort((a,b)=>b.prod.size-a.prod.size);
const wb=Workbook.create(),sp=wb.worksheets.add("Resumo por produto"),sf=wb.worksheets.add("Resumo fornecedores"),sd=wb.worksheets.add("Itens detalhados"),sc=wb.worksheets.add("Critérios");
const navy="#17365D",blue="#D9EAF7",light="#F5F8FC",red="#FCE8E6",yellow="#FFF2CC",border="#CBD5E1";
function head(sh,end,title,sub){sh.showGridLines=false;sh.getRange(`A1:${end}1`).merge();sh.getRange("A1").values=[[title]];sh.getRange(`A1:${end}1`).format={fill:navy,font:{bold:true,color:"#FFFFFF",size:16},rowHeight:30};sh.getRange(`A2:${end}2`).merge();sh.getRange("A2").values=[[sub]];sh.getRange(`A2:${end}2`).format={fill:blue,font:{italic:true,color:navy},rowHeight:24};}
const notes=new Set(rows.map(noteKey));
head(sp,"N","Prévia — inconsistências de crédito de ICMS por produto",`${rows[0].Empresa} | Empresa Fortes 0158 | Ano 2026 | CFOP 1552 excluído | Fonte: base SQL do Analisador SPED`);
sp.getRange("A4:H5").values=[["Produtos divergentes",products.length,"Fornecedores",suppliers.length,"Notas",notes.size,"Itens analisados",rows.length],["Valor dos itens",round(rows.reduce((a,r)=>a+n(r.ValorItem),0)),"Base ICMS",round(rows.reduce((a,r)=>a+n(r.BaseIcms),0)),"ICMS creditado",round(rows.reduce((a,r)=>a+n(r.IcmsDestacado),0)),"Critério","Com e sem crédito"]];
sp.getRange("A4:H5").format={fill:light,borders:{preset:"all",style:"thin",color:border},font:{color:navy}};sp.getRange("B5:D5").setNumberFormat("R$ #,##0.00");sp.getRange("F5").setNumberFormat("R$ #,##0.00");
const ph=["Código produto","Produto","NCM","Com crédito","Sem crédito","Fornecedores","Notas","CFOPs","CSTs","Valor dos itens","Base ICMS","ICMS creditado","% itens com crédito","Situação"];
const pd=products.map(p=>[id(p.codigo),p.produto,id(p.ncm),p.com,p.sem,p.forn.size,p.notas.size,[...p.cfops].sort().join(", "),[...p.csts].sort().join(", "),round(p.valor),round(p.base),round(p.icms),p.com/(p.com+p.sem),"Divergente — revisar"]);
sp.getRange(`A7:N${7+pd.length}`).values=[ph,...pd];sp.tables.add(`A7:N${7+pd.length}`,true,"ProdutosCreditoDivergente").style="TableStyleMedium2";sp.getRange(`A8:C${7+pd.length}`).setNumberFormat("@");sp.getRange(`J8:L${7+pd.length}`).setNumberFormat("R$ #,##0.00");sp.getRange(`M8:M${7+pd.length}`).setNumberFormat("0.0%");sp.getRange(`N8:N${7+pd.length}`).format={fill:yellow,font:{bold:true,color:"#7F6000"}};sp.freezePanes.freezeRows(7);sp.freezePanes.freezeColumns(2);
for(const[c,w]of Object.entries({A:17,B:46,C:13,D:13,E:13,F:14,G:11,H:18,I:18,J:17,K:16,L:17,M:19,N:22}))sp.getRange(`${c}:${c}`).format.columnWidth=w;

head(sf,"I","Fornecedores envolvidos nas inconsistências","Quantidade de produtos que alternaram entre entradas com crédito e sem crédito de ICMS");
const fh=["Fornecedor","CNPJ/CPF","Produtos divergentes","Notas","Itens com crédito","Itens sem crédito","Valor dos itens","ICMS creditado","Prioridade"];
const fd=suppliers.map(s=>[s.nome,id(s.doc),s.prod.size,s.notas.size,s.com,s.sem,round(s.valor),round(s.icms),s.prod.size>=20?"Alta":s.prod.size>=5?"Média":"Baixa"]);
sf.getRange(`A4:I${4+fd.length}`).values=[fh,...fd];sf.tables.add(`A4:I${4+fd.length}`,true,"FornecedoresInconsistencia").style="TableStyleMedium2";sf.getRange(`B5:B${4+fd.length}`).setNumberFormat("@");sf.getRange(`G5:H${4+fd.length}`).setNumberFormat("R$ #,##0.00");sf.getRange(`I5:I${4+fd.length}`).conditionalFormats.add("containsText",{text:"Alta",format:{fill:red,font:{bold:true,color:"#B91C1C"}}});sf.freezePanes.freezeRows(4);sf.freezePanes.freezeColumns(2);for(const[c,w]of Object.entries({A:45,B:20,C:20,D:11,E:18,F:18,G:17,H:17,I:13}))sf.getRange(`${c}:${c}`).format.columnWidth=w;

head(sd,"U","Itens de entrada dos produtos divergentes","Somente CFOPs de compra encontrados: 1102, 1403, 1556, 2102 e 2403; CFOP 1552 excluído");
const dh=["Situação","Fornecedor","CNPJ/CPF","Entrada","Emissão","Série","Nota","Chave NF-e","Item","Código produto","Produto","NCM","CFOP","CST ICMS","Quantidade","Unidade","Valor item","Base ICMS","Alíquota ICMS","ICMS destacado","Situação doc."];
const dd=rows.map(r=>[r.SituacaoCredito,r.Fornecedor,id(r.DocumentoFornecedor),date(r.DataEntrada),date(r.DataEmissao),id(r.Serie),id(r.NumeroNota),id(r.ChaveNfe),id(r.NumeroItem),id(r.CodigoItem),r.Produto,id(r.Ncm),id(r.Cfop),id(r.CstIcms),n(r.Quantidade),r.Unidade,n(r.ValorItem),n(r.BaseIcms),n(r.AliquotaIcms)/100,n(r.IcmsDestacado),id(r.SituacaoDocumento)]);
sd.getRange(`A4:U${4+dd.length}`).values=[dh,...dd];sd.tables.add(`A4:U${4+dd.length}`,true,"ItensCreditoDivergente").style="TableStyleMedium2";sd.getRange(`D5:E${4+dd.length}`).setNumberFormat("dd/mm/yyyy");sd.getRange(`C5:C${4+dd.length}`).setNumberFormat("@");sd.getRange(`F5:N${4+dd.length}`).setNumberFormat("@");sd.getRange(`Q5:R${4+dd.length}`).setNumberFormat("R$ #,##0.00");sd.getRange(`S5:S${4+dd.length}`).setNumberFormat("0.00%");sd.getRange(`T5:T${4+dd.length}`).setNumberFormat("R$ #,##0.00");sd.getRange(`A5:A${4+dd.length}`).conditionalFormats.add("containsText",{text:"Sem crédito",format:{fill:red,font:{bold:true,color:"#B91C1C"}}});sd.freezePanes.freezeRows(4);sd.freezePanes.freezeColumns(3);
for(const[c,w]of Object.entries({A:16,B:40,C:18,D:13,E:13,F:8,G:12,H:46,I:8,J:17,K:48,L:12,M:9,N:11,O:13,P:10,Q:15,R:15,S:15,T:17,U:13}))sd.getRange(`${c}:${c}`).format.columnWidth=w;

head(sc,"B","Critérios utilizados","Premissas da prévia para validar a viabilidade da funcionalidade");
sc.getRange("A4:B11").values=[["Critério","Aplicação"],["Empresa","Código fiscal Fortes 0158"],["Período","Entradas do ano de 2026"],["Documentos","Somente notas válidas; canceladas excluídas"],["CFOPs de compra encontrados","1102, 1403, 1556, 2102 e 2403"],["CFOP excluído","1552"],["Com crédito","ICMS do item maior que zero"],["Sem crédito","ICMS do item igual a zero"]];sc.tables.add("A4:B11",true,"CriteriosPreviaCredito").style="TableStyleMedium2";sc.getRange("A:A").format.columnWidth=32;sc.getRange("B:B").format.columnWidth=75;sc.getRange("B4:B11").format.wrapText=true;
const out=await SpreadsheetFile.exportXlsx(wb);await out.save(`${dir}previa_inconsistencias_credito_icms_empresa_0158_2026.xlsx`);
for(const[name,file,range]of[["Resumo por produto","preview_produtos.png","A1:N28"],["Resumo fornecedores","preview_fornecedores.png","A1:I28"],["Itens detalhados","preview_itens.png","A1:U28"],["Critérios","preview_criterios.png","A1:B11"]]){const p=await wb.render({sheetName:name,range,scale:1,format:"png"});await fs.writeFile(`${dir}${file}`,new Uint8Array(await p.arrayBuffer()));}
console.log((await wb.inspect({kind:"region",sheetId:"Resumo por produto",range:"A1:N18",maxChars:3500})).ndjson);
console.log((await wb.inspect({kind:"region",sheetId:"Resumo fornecedores",range:"A1:I14",maxChars:2500})).ndjson);
console.log((await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:200},maxChars:1500})).ndjson);
