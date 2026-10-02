# Analisi di mercato dei MES e confronto con Nicolò MES

Aggiornata al **2 ottobre 2026** (allineata allo stato prodotto in [STATO-PROGETTO.md](STATO-PROGETTO.md) e a ricerca di mercato ripresa in questa data). Punto di vista del team: commerciale, commercialista, specialista fatturazione, capocantiere, responsabile tecnico impianti, capo area e capo reparto, ingegneri (gestionale, meccanico, informatico, Industria 4.0/5.0), cybersecurity manager, data analyst e graphic designer.

## 1. In sintesi

- **Il mercato.** In Italia i MES resteranno un pezzo centrale della digitalizzazione di fabbrica: studio AlixPartners–Qualitas (marzo 2025) → **~120 M€ entro il 2027**, CAGR **5,2%** (2022–2027). In Europa occidentale il CAGR è più alto (**6,7%**, mercato UE ~**887 M€** al 2027); a livello globale si parla di circa **3 Md€** al 2027 (CAGR ~5,6%). Il mercato italiano è **frammentato (~23 fornitori)**: 11 specializzati MES, pochi indipendenti; consolidamento e passaggio da software “fatto in casa” a prodotti standard, cloud, IoT/edge e AI.
- **Cosa distingue ancora i prodotti.** Connettività macchina reale, dati in tempo reale, integrazione ERP/contabilità, e (dal 2026) **documentazione di interconnessione** utile alla perizia dell’iperammortamento. I MES “solo reparto” vincono sul campo; chi ha anche commerciale e fiscale italiano riduce i pezzi da integrare.
- **Il nostro punto di forza.** Un solo prodotto: produzione + commerciale + documenti italiani (DDT, FatturaPA validata) + moduli di settore (CEI EN 61439, HACCP/allergeni/SSCC, rapportini firmati, collaudo FAT/SAT e CE, service, energia, ufficio tecnico). Configurabile per attività e reparti; desktop, web e telefono. I MES PMI tipici restano sul reparto e demandano ERP esterno per clienti, DDT e fatture.
- **Vuoti ancora rilevanti (02/10/2026, dopo chiusura contro in codice):**
  - **invio automatico allo SdI** (G9); G8 passive/scadenziario: codice + migrazione Neon fatti, **commit/push ancora da fare**;
  - **capacità finita** e **ubicazioni** magazzino;
  - MRP: **esplosione distinta + proposte fatti** (G10 base); resta creazione automatica ordini e multi-livello;
  - **OEE cruscotto da macchina** quando ci sono letture (`OeeSource`); resta collegare macchine/contatori in campo;
  - **origine UE software**: pagina/API fatti; veridicità a carico del produttore;
  - pacchetti verticali (EPLAN/lista fili, DM 37/08/SAL, 3.1, nutrizionale/bilance);
  - **assistente IA**, SSO, più lingue; web ancora più consultazione che creazione;
  - hosting a pagamento e referenze pubbliche (scelta del titolare).
- **Momento favorevole (incentivi).** La L. 199/2025 (bilancio 2026) ha sostituito i crediti 4.0/5.0 con **iperammortamento** (maggiorazione del costo ammortizzabile): **180%** fino a 2,5 M€, **100%** fino a 10 M€, **50%** fino a 20 M€, investimenti **1/1/2026–30/9/2028**. Software in Allegato V agevolabile se **interconnesso** al sistema di gestione produzione / rete di fornitura; serve perizia asseverata. Dal **12/06/2026** è attiva la piattaforma GSE per le comunicazioni. Attenzione nuova: il decreto attuativo chiede dichiarazione di **origine UE** del software (≥50% del valore di sviluppo sostanziale in UE/SEE) — rilevante in vendita e in perizia.

## 2. Mappa del mercato

| Fascia | Esempi | Per chi | Prezzo indicativo (2026) | Punto forte | Limite per una PMI |
|---|---|---|---|---|---|
| MES enterprise | Siemens Opcenter, SAP DM, Rockwell FactoryTalk/Plex, AVEVA, Critical Manufacturing | Grandi gruppi | Progetti da decine–centinaia di k€ + canoni | Profondità, PLC/SCADA, APS, AI/governance | Costi, mesi di progetto, integratori |
| MES italiani per PMI | Bravo Manufacturing, siMES (SiVaF), Opera MES, MES Factory (Campi), moduli TeamSystem / Zucchetti, NET@PRO (Qualitas) | PMI manifatturiere | Bravo Plus ~**290 €/risorsa**/anno (min. 10); siMES da ~**3.500 €/macchina** + canone ~15%; connettività campo citata da ~**150 €/anno**/macchina | OEE da macchina, IoT, ERP connector, marketing Transizione 5.0 / iperammortamento | Quasi sempre **solo reparto**: commerciale, DDT, FatturaPA restano nell’ERP |
| MRP / ERP cloud | MRPeasy, Katana, Odoo MRP | Piccole aziende | Decine–centinaia $/mese (Katana fascia alta) | Avvio rapido, distinte, magazzino | Fiscale IT debole, poche macchine, qualità/schedulazione leggere |
| MES “no code” / composable | Tulip e simili | Chi ha ufficio tecnico interno | Centinaia $/mese per interfaccia | App di reparto su misura, istruzioni digitali | Va costruito tutto (mesi) |
| Verticali impiantistica | TeamSystem Cantieri, Antos Impianti.net, D-TEC, mInterventi | Impiantisti | Canoni per utente | Computo, Metel, ticket, app tecnici | Niente produzione di fabbrica |
| Verticali alimentare | Plex/AVEVA food, V5, FoodDocs, SafetyChain | Food | SaaS → progetti | HACCP, richiami, audit | Spesso esteri; fiscale IT assente |

**Lettura competitiva (ricerca 02/10/2026).** Bravo spinge costi di commessa, dashboard e IoT come add-on; siMES spinge AI predittiva e retrofit macchine legacy; Opera MES spinge modularità (produzione, qualità, energia, manutenzione) + connettori ERP. Nessuno di questi, nella fascia PMI, mette insieme **FatturaPA nativa + DDT + settori IT** come Nicolò: il confronto tipico del cliente è “MES + ERP già in casa”, non “un solo prodotto”.

## 3. Cosa chiede il mercato nel 2026

1. **Interconnessione vera** (OPC UA, MQTT, Modbus, S7…): dati in tempo reale, non a fine turno — base anche per la perizia.
2. **Energia e digitalizzazione agevolabile**: software interconnesso (Allegato V); per chi punta al risparmio restano utili dashboard kWh e confronti prima/dopo (il vecchio credito 5.0 con soglie 3%/5% è stato sostituito dall’iperammortamento, ma i clienti e i periti continuano a chiedere **prove di misura**).
3. **Schedulazione a capacità finita** e, in prospettiva, ripianificazione guidata da eventi (AI “copilota” con umano in loop — Gartner MES Guide 2026: interoperabilità, API aperte, fiducia/governance prima dell’autonomia).
4. **Istruzioni digitali** alla postazione (disegni, foto, video; sempre più assistite da IA).
5. **Cloud e mobile**, con opzione **on-premise** per chi non vuole tutto fuori.
6. **Time-to-value**: per una PMI con ~20 macchine restano citati **2–4 mesi** di avvio; la licenza è spesso la parte minore del costo totale.
7. **Sicurezza e conformità** (2FA, audit, NIS2 per clienti strutturati).

## 4. Confronto funzione per funzione

Legenda: ✅ c’è · 🟡 parziale · ❌ manca · 📦 codice pronto / da pubblicare

| Area | Nicolò MES (02/10/2026) | MES PMI italiani | MRP cloud | Enterprise |
|---|---|---|---|---|
| Distinte, cicli, commesse, fasi | ✅ | ✅ | ✅ | ✅ |
| Terminale di reparto con PIN, anche offline | ✅ | ✅ | 🟡 | ✅ |
| Filtro / selettore “mio reparto” al terminale e su /tecnici | 🟡 server sì; selettore UI ancora da chiudere (G1) | ✅ | ❌ | ✅ |
| Tracciabilità lotti avanti/indietro, richiamo | ✅ | ✅ | 🟡 | ✅ |
| Matricole / unità per pezzo | ✅ | 🟡 | 🟡 | ✅ |
| OEE | ✅ fasi; **da macchina sul cruscotto** se ci sono letture (`OeeSource`) | ✅ da macchina | ❌ | ✅ |
| Interconnessione macchine (OPC UA, MQTT) | ✅ gateway + token; manca campo reale | ✅ | ❌ | ✅ |
| Monitoraggio energetico (iperammortamento / 5.0) | ✅ kWh macchina/commessa, progetti, stampa; manca contatore reale | ✅ | ❌ | ✅ |
| Dichiarazione origine UE software (perizia) | ✅ `/origine-software` + API | 🟡 | ❌ | 🟡 |
| Schedulazione a capacità finita | 🟡 board/planning senza vincoli duri | 🟡/✅ | 🟡 | ✅ |
| Istruzioni di lavoro e disegni alla postazione | ✅ terminale + web | 🟡 | ❌ | ✅ |
| Ufficio tecnico (revisioni, MT, allegati) | ✅ | 🟡 | ❌ | ✅ |
| Collaudo FAT/SAT, fascicolo, dichiarazione CE/UE | ✅ | 🟡 | ❌ | ✅ |
| Service post-vendita (matricola, garanzia, RA, interventi) | ✅ | 🟡 | ❌ | ✅ |
| Qualità: piani, NC, certificati | ✅ | ✅ | 🟡 | ✅ |
| Taratura strumenti, CAPA, audit ISO 9001 | ❌ | 🟡 | ❌ | ✅ |
| Manutenzione macchine | ✅ | ✅ | ❌ | ✅ |
| MRP sulle distinte + proposte d’ordine | ✅ esplosione + proposta; ❌ crea PO da solo | 🟡 dall’ERP | ✅ | ✅ |
| Magazzino ubicazioni, inventario barcode da telefono | 🟡 lotti sì | 🟡 | ✅ | ✅ |
| Clienti, preventivi | ✅ | ❌ (ERP) | ✅ | 🟡 |
| DDT, conto lavoro | ✅ | ❌ | 🟡 | 🟡 |
| Fattura elettronica XML FatturaPA | ✅ schema ufficiale | ❌ (ERP) | ❌ | ❌ |
| Invio allo SdI | ❌ (caricamento manuale) | ❌ | ❌ | ❌ |
| Fatture passive + scadenziario | ✅ codice+DB Neon; 📦 push release | ❌ | 🟡 | ❌ |
| Costi e margini di commessa | ✅ | 🟡 | 🟡 | ✅ |
| Presenze / timbrature | ❌ (ore di commessa sì) | 🟡 | ❌ | ✅ |
| Web e telefono | ✅ web in crescita (molto consultazione); /tecnici | ✅ | ✅ | ✅ |
| Cloud o server del cliente | ✅ entrambi (installer server da provare su Windows Server reale) | ✅ | solo cloud | ✅ |
| Teleassistenza | ✅ | ✅ | ✅ | ✅ |
| Autenticazione a due fattori | ✅ TOTP + recovery; ❌ SSO | 🟡 | ✅ | ✅ |
| API pubbliche documentate per terzi | 🟡 API sì, docs terzi no | 🟡 | ✅ | ✅ |
| Assistente IA | ❌ | 🟡 (es. siMES) | 🟡 | ✅ |
| Più lingue e valute | ❌ | 🟡 | ✅ | ✅ |

## 5. Settore per settore

### 5.1 Quadri elettrici e automazione
- **Abbiamo:** commesse/cablaggio, verifica e dichiarazione CEI EN 61439, lotti, DDT/fattura, costi, **Metel** (import ANIE), documenti tecnici.
- **Gli altri:** EPLAN → distinta, lista fili per taglio/siglatura, etichette cavi/morsetti.
- **Resta:** import EPLAN (Excel/CSV), lista fili ed etichette, allegati di commessa legati alla dichiarazione.

### 5.2 Meccanica e carpenteria
- **Abbiamo:** cicli, conto lavoro, OEE (dichiarato), manutenzione, dati macchina, documenti/CNC come allegati di fase, istruzioni al terminale.
- **Gli altri:** DNC verso macchina, nesting, 3.1 su colata, taratura strumenti.
- **Resta:** certificato 3.1 sul lotto, preventivo meccanico avanzato, taratura.

### 5.3 Costruzione macchine e impianti
- **Abbiamo (chiuso rispetto all’analisi di settembre):** ufficio tecnico, collaudo FAT/SAT + fascicolo + dichiarazione CE/UE (base giuridica automatica fino/oltre 20/01/2027), service con matricola/garanzia/RA/interventi, energia per perizia, firma a schermo sulla dichiarazione (non qualificata).
- **Resta:** PDF autonomo rapporto collaudo, firma digitale qualificata se richiesta, distinte CAD via Excel/CSV già usabili ma non verticalizzate, QR matricola → storico.

### 5.4 Impiantistica
- **Abbiamo:** rapportini firmati da telefono, costi in commessa, DDT/fattura, Metel.
- **Gli altri:** computo, SAL, calendario squadre, ticket, DM 37/08, magazzino furgone.
- **Resta:** DM 37/08, SAL/acconti, calendario squadre (riuso Service), Angaisa.

### 5.5 Alimentare
- **Abbiamo:** scadenze FEFO, allergeni, etichetta lotto, SSCC, richiamo, HACCP.
- **Gli altri:** resa/cali, tabella nutrizionale, bilance, IFS/BRC, resi.
- **Resta:** nutrizionale, bilance via gateway, ricetta con resa, HACCP guidato per tipologia.

### 5.6 Manifattura generica
Moduli comuni coprono il nucleo. MRP base (esplosione + proposte) c’è; i vuoti più sentiti restano **capacità finita** e **ubicazioni**.

## 6. I nostri pro e contro (aggiornati)

**Pro**
- Un solo sistema dal preventivo alla fattura, con requisiti italiani nativi (DDT, FatturaPA validata, 61439, HACCP, conto lavoro).
- Configurazione multi-attività e multi-reparto; canali desktop/web/mobile per ruolo e area.
- Tracciabilità lotti + richiamo; matricole; ufficio tecnico; collaudo CE; service; energia.
- Interconnessione predisposta; **OEE di cruscotto che preferisce i dati macchina** quando arrivano.
- **MRP** con esplosione distinta e proposte; **fatture passive + scadenziario** (DB pronto); **dichiarazione origine UE** stampabile.
- Margini di commessa riservati alla direzione; 2FA; test e CI; auto-update; server del cliente.
- Argomento vendita 2026–2028: interconnessione + energia + origine UE in un prodotto italiano.

**Contro**
- Nessun invio SdI automatico; prima nota completa resta fuori.
- Lavoro 02/10 (G8/OEE/origine/MRP) **ancora da commit/push** su `main`.
- Niente capacità finita né ubicazioni; MRP non crea ancora gli ordini da solo né multi-livello.
- Senza macchine/contatori in campo OEE “Machine” ed energia restano vuoti in produzione reale.
- Web incompleto in scrittura; Windows-first; niente IA, SSO, multilingua.
- Hosting free senza SLA; poche referenze; team piccolo.
- Concorrenza MES di campo ancora avanti su AI marketing e connettività plug-and-play.

## 7. Priorità proposte (dopo quanto già chiuso in codice)

| # | Funzione | Chi la chiede | Impatto | Impegno | Nota |
|---|---|---|---|---|---|
| 1 | Commit/push release (G8 + OEE + origine + MRP) | Tutti | Alto | S | Migrazioni Neon già applicate |
| 2 | G9 SdI automatico (intermediario) | Commercialista | Alto | M | Serve contratto |
| 3 | Una macchina + contatore reali in campo | 4.0/5.0 | Molto alto | M | Gateway e OEE pronti |
| 4 | Ubicazioni + inventario barcode (G11) | Magazzino | Medio-alto | M | |
| 5 | Capacità finita (G12) | Gestionale | Alto | L | |
| 6 | MRP: crea PO dalle proposte; multi-livello | Acquisti | Medio | M | Base G10 fatta |
| 7 | Selettore reparto UI (G1) | Capo reparto | Medio | S | |
| 8 | Pacchetti settore (G13) | Verticali | Alto sul verticale | M | Metel già ok |
| 9 | Hosting a pagamento + referenze | Commerciale | Alto | Titolare | |
| 10 | Web scrittura, API docs, SSO, IA | IT / utenti | Medio | M | |

## 8. Percorso consigliato

1. **Push** del lavoro 02/10 (vedi [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md)).
2. **G9 SdI** quando c’è intermediario; intanto XML manuale resta valido.
3. **Prova campo** una macchina + energia; verificare OEE `Machine` sul cruscotto.
4. **Ubicazioni → capacità finita**; approfondire MRP (PO automatici).
5. **Pacchetti di settore** sul primo cliente esterno.
6. **Fiducia commerciale**: hosting a pagamento, referenze, docs API.

## 9. Cosa è cambiato il 02/10/2026 (sessione contro)

- G8: migrazione Neon `AddPayables`; gate chiuso (push pending).
- OEE cruscotto da letture macchina (`OeeSource`).
- Dichiarazione origine UE software (migrazione + pagina stampabile).
- MRP base (esplosione distinta + proposte; web `/mrp`).
- Prossimo focus operativo: **push**, poi G9 / campo / G11 / G12.

## Fonti

### Mercato e tendenze
- [Mercato MES Italia — AlixPartners e Qualitas](https://www.alixpartners.com/newsroom/mercato-dei-mes-in-forte-crescita-in-italia-studio-alixpartners-e-qualitas/) (19/03/2025)
- [Automazione News: 120 M€ al 2027, 23 fornitori, Europa 887 M€](https://www.automazionenews.it/il-mercato-dei-mes-in-italia-cresce-previsti-120-milioni-di-euro-entro-il-2027/)
- [Industria Italiana — evoluzione MES](https://www.industriaitaliana.it/manufacturing-execution-systems-alixpartners-digitalizzazione-manifattura/)
- [Qualitas: trend MES / Net@PRO](https://www.qualitas.it/blog/un-mercato-in-crescita-i-trend-del-mes)
- [Gartner MES Market Guide 2026 — lettura Siemens Opcenter (AI, API, trust)](https://blogs.sw.siemens.com/opcenter/2026-gartner-market-guide-for-manufacturing-execution-systems-how-ai-is-shaping-mes-and-where-we-believe-opcenter-adds-customer-value/)
- [AI agents in MES (IIoT World)](https://www.iiot-world.com/smart-manufacturing/ai-agents-mes-beyond-chatbot/)
- [Agentic scheduling / UNS (HiveMQ)](https://www.hivemq.com/blog/agentic-scheduling-adaptive-planning-uns/)

### Incentivi
- [MIMIT — Nuovo Piano Transizione 5.0 / Iperammortamento](https://www.mimit.gov.it/it/incentivi/nuovo-piano-transizione-5-0-iperammortamento)
- [Heuris — iperammortamento e software MES 2026](https://www.heuris.it/iperammortamento-mes-2026)
- [Innovation Post — decreto attuativo, origine UE software](https://www.innovationpost.it/attualita/il-decreto-attuativo-del-nuovo-iper-ammortamento-2026-ecco-tutti-i-dettagli-della-normativa-con-le-definizioni-di-made-in-eu/)

### Concorrenti e prezzi
- [Bravo Manufacturing — prezzi Plus/Pro](https://www.bravomanufacturing.it/confronta-prezzi/)
- [siMES — MES PMI / AI / prezzi macchina](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/)
- [Confronto MES PMI (SiVaF)](https://www.sivaf.it/confronto-mes-pmi-manifatturiere/)
- [Opera MES](https://www.operames.it/opera-mes/)
- [TeamSystem produzione/MES](https://www.teamsystem.com/aziende/enterprise/funzionalita/produzione-mes-ts-enteprise/)

### Verticali e contorno
- [Logisticamente — MES Industria 4.0 2026](https://www.logisticamente.it/articoli/57360/migliori-software-mes-industria-2026-funzioni-vantaggi-prezzi/)
- [TeamSystem Cantieri / computo](https://www.teamsystem.com/construction/teamsystem-cantieri/funzionalita/preventivi-computo-metrico/)
- [Antos Impianti.net](https://www.01net.it/?p=71705)
- [Tracciabilità alimentare (Capterra IT)](https://www.capterra.it/directory/30563/food-traceability/software)

Stato prodotto di riferimento: [STATO-PROGETTO.md](STATO-PROGETTO.md). Gate aperti: [GATES.md](GATES.md).
