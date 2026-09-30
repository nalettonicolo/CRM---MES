# Analisi di mercato dei MES e confronto con Nicolò MES

Aggiornata al 30/09/2026. Punto di vista del team: commerciale, commercialista, specialista fatturazione, capocantiere, responsabile tecnico impianti, capo area e capo reparto, ingegneri (gestionale, meccanico, informatico, Industria 4.0/5.0), cybersecurity manager, data analyst e graphic designer.

## 1. In sintesi

- **Il mercato.** In Italia il mercato dei MES cresce: secondo lo studio AlixPartners e Qualitas arriverà a circa 120 milioni di euro nel 2027, con una crescita del 5,2% annuo. Quasi tutti i prodotti promettono le stesse cose (raccolta dati, OEE, tracciabilità). A fare la differenza sono tre cose:
  - il collegamento diretto con le macchine;
  - i dati in tempo reale;
  - l'integrazione con l'ERP e con la contabilità.
- **Il nostro punto di forza.** Un solo prodotto copre produzione, parte commerciale, documenti fiscali italiani (DDT, FatturaPA) e requisiti di settore (CEI EN 61439, HACCP e allergeni, rapportini firmati). Si configura per attività e reparti e si usa da desktop, web e telefono. I concorrenti PMI di solito coprono una sola di queste aree e si appoggiano a un ERP esterno.
- **I nostri vuoti principali:**
  - l'invio automatico allo SdI e la contabilità (scadenziario, incassi, pagamenti);
  - la schedulazione a capacità finita;
  - le istruzioni di lavoro digitali al terminale;
  - il monitoraggio energetico per l'iperammortamento 2026;
  - il service post-vendita e l'ufficio tecnico (in sviluppo);
  - l'autenticazione a due fattori;
  - alcune integrazioni attese per settore: EPLAN per i quadristi, DM 37/08 per gli impiantisti, tabella nutrizionale per l'alimentare.
- **Il momento favorevole.** La legge di bilancio 2026 (L. 199/2025) ha sostituito il credito d'imposta 5.0 con un **iperammortamento al 180%**, valido dal 1/1/2026 al 30/9/2028. È ammesso anche il software per l'interconnessione e la gestione dell'energia, purché dimostri l'interconnessione e misuri i consumi in modo continuo. Chi ha già un MES con dati macchina ed energia ha un argomento di vendita forte.

## 2. Mappa del mercato

| Fascia | Esempi | Per chi | Prezzo indicativo | Punto forte | Limite per una PMI |
|---|---|---|---|---|---|
| MES enterprise | Siemens Opcenter, SAP Digital Manufacturing, Rockwell FactoryTalk e Plex, AVEVA MES, Critical Manufacturing | Grandi gruppi, multinazionali | Progetti da decine a centinaia di migliaia di euro più canoni | Profondità, tracciabilità, PLC e SCADA, pianificazione avanzata | Costi e tempi di progetto (mesi), richiedono integratori |
| MES italiani per PMI | Bravo Manufacturing, siMES (SiVaF), MES Factory (Campi), Opera MES, moduli MES di TeamSystem Enterprise e Zucchetti | PMI manifatturiere italiane | Su preventivo; la sola connettività parte da circa 150 €/anno a macchina (siMES) | Raccolta dati dalle macchine, OEE, pronti per la Transizione 5.0 | Solo reparto: parte commerciale, DDT e fatture restano nell'ERP |
| MRP ed ERP cloud | MRPeasy, Katana, Odoo MRP | Piccole aziende, spesso sotto i 50 addetti | 49-500 $/mese; Katana da 299 $/mese più 199 $ per la produzione | Rapidi da avviare, distinte, magazzino | Poco adatti all'Italia (fiscale), qualità e schedulazione leggere, niente macchine |
| Piattaforme MES "no code" | Tulip | Aziende con ufficio tecnico interno | 100-250 $/mese per interfaccia, minimo 10 | App di reparto su misura, istruzioni digitali | Va costruito tutto, 3-6 mesi |
| Verticali per l'impiantistica | TeamSystem Cantieri, Antos Impianti.net, D-TEC, mInterventi, ArxService | Impiantisti, manutentori | Canoni per utente | Computo metrico, listini Metel, ticket, app tecnici | Niente produzione |
| Verticali per l'alimentare | Plex e AVEVA per il food, V5 Traceability, FoodDocs, SafetyChain | Alimentare | Da canoni SaaS a progetti | HACCP, richiami, ricette, audit IFS e BRC | Spesso stranieri, fiscale italiano assente |

## 3. Cosa chiede il mercato nel 2026

1. **Interconnessione vera con le macchine**: OPC UA, MQTT, Modbus, S7, con dati in tempo reale e non a fine turno. È anche il requisito dell'iperammortamento.
2. **Energia misurata per macchina e per prodotto**: consumi rapportati alla produzione, con valutazione ex ante e verifica ex post (risparmio minimo 3% di stabilimento o 5% di processo).
3. **Schedulazione a capacità finita**: capacità, manodopera, materiali, attrezzaggi, precedenze. Più fornitori la stanno lanciando nel 2026.
4. **Istruzioni di lavoro digitali**: disegni, foto e video alla postazione, sempre più generati con l'IA.
5. **Assistente IA ("copilota")**: domande in linguaggio naturale sui dati di produzione, previsione di fermi, ritardi e scarti.
6. **Cloud e mobile**, con in parallelo l'installazione presso il cliente per chi la chiede.
7. **Costo totale e tempi di avvio**: la licenza è la parte minore, conta quanto si impiega a partire (per una PMI con 20 macchine si citano 2-4 mesi).

## 4. Confronto funzione per funzione

Legenda: ✅ c'è · 🟡 parziale · ❌ manca · 🔧 in sviluppo

| Area | Nicolò MES | MES PMI italiani | MRP cloud | Enterprise |
|---|---|---|---|---|
| Distinte, cicli, commesse, fasi | ✅ | ✅ | ✅ | ✅ |
| Terminale di reparto con PIN, anche offline | ✅ | ✅ | 🟡 | ✅ |
| Filtro per reparto al terminale | ✅ (lato server; manca ancora il selettore nel terminale e nella pagina tecnici) | ✅ | ❌ | ✅ |
| Tracciabilità lotti avanti e indietro, richiamo | ✅ | ✅ | 🟡 | ✅ |
| Matricole (singolo pezzo o singola macchina) | ❌ | 🟡 | 🟡 | ✅ |
| OEE | 🟡 da fasi dichiarate; dati macchina nel dettaglio | ✅ da macchina | ❌ | ✅ |
| Interconnessione macchine (OPC UA, MQTT) | ✅ gateway pronto, manca il collegamento reale | ✅ | ❌ | ✅ |
| Monitoraggio energetico (Transizione 5.0) | ❌ | ✅ | ❌ | ✅ |
| Schedulazione a capacità finita | 🟡 board scadenze e planning, senza vincoli | 🟡/✅ | 🟡 | ✅ |
| Istruzioni di lavoro e disegni alla postazione | ❌ | 🟡 | ❌ | ✅ |
| Qualità: piani di controllo, non conformità, certificati | ✅ | ✅ | 🟡 | ✅ |
| Taratura strumenti, azioni correttive, audit ISO 9001 | ❌ | 🟡 | ❌ | ✅ |
| Manutenzione macchine | ✅ | ✅ | ❌ | ✅ |
| Calcolo fabbisogni (MRP) con proposte d'ordine | 🟡 sotto scorta e mancanti, senza calcolo sulle distinte | 🟡 dall'ERP | ✅ | ✅ |
| Magazzino con ubicazioni, inventario a barcode, etichette | 🟡 lotti sì, ubicazioni e inventario da telefono no | 🟡 | ✅ | ✅ |
| Clienti, preventivi, ordini | ✅ | ❌ (è nell'ERP) | ✅ | 🟡 |
| DDT, conto lavoro | ✅ | ❌ | 🟡 | 🟡 |
| Fattura elettronica: file XML | ✅ validato sullo schema ufficiale | ❌ (è nell'ERP) | ❌ | ❌ |
| Invio allo SdI e fatture passive | ❌ | ❌ | ❌ | ❌ |
| Scadenziario, incassi, pagamenti, prima nota | ❌ (solo esportazione per la contabilità) | ❌ | 🟡 | ❌ |
| Costi e margini di commessa | ✅ | 🟡 | 🟡 | ✅ |
| Presenze e timbrature | ❌ (ore per commessa sì) | 🟡 | ❌ | ✅ |
| Web e telefono | ✅ piattaforma web in crescita, pagina tecnici | ✅ | ✅ | ✅ |
| Cloud o server del cliente | ✅ cloud; 🟡 server del cliente (basi pronte) | ✅ | solo cloud | ✅ |
| Teleassistenza | 🟡 basi lato server | ✅ | ✅ | ✅ |
| Autenticazione a due fattori, accesso unico aziendale (SSO) | ❌ | 🟡 | ✅ | ✅ |
| API pubbliche documentate per integrazioni | 🟡 l'API esiste, manca la documentazione per terzi | 🟡 | ✅ | ✅ |
| Assistente IA | ❌ | 🟡 (siMES) | 🟡 | ✅ |
| Più lingue e valute | ❌ | 🟡 | ✅ | ✅ |

## 5. Settore per settore: cosa hanno gli altri e quali integrazioni servono

### 5.1 Quadri elettrici e automazione
- **Abbiamo:**
  - commesse e cablaggio con fasi;
  - verifica e dichiarazione CEI EN 61439;
  - lotti dei componenti;
  - DDT e fattura;
  - costi di commessa.
- **Gli altri hanno:**
  - scambio con EPLAN Electric P8 (codici articolo e distinte dallo schema, interfacce verso ERP e PLM);
  - listini Metel dei produttori;
  - lista fili per le macchine di taglio e siglatura;
  - etichette di cavi e morsetti.
- **Integrazioni mirate:**
  1. **Import distinta da EPLAN** (o da file Excel o CSV generato da EPLAN): la distinta del quadro diventa la distinta della commessa.
  2. **Metel**: l'importatore è pronto a metà e serve un file reale di un produttore.
  3. **Lista fili ed etichette** esportate per stampanti e siglatrici.
  4. **Allegati di commessa**: schema, layout e fotografie del quadro finito, da agganciare alla dichiarazione.

### 5.2 Meccanica e carpenteria (conto terzi)
- **Abbiamo:**
  - cicli con centri di lavoro;
  - conto lavoro per trattamenti con rientri e scarti;
  - OEE;
  - manutenzione;
  - dati macchina.
- **Gli altri hanno:**
  - versioni dei programmi CNC e invio alla macchina (DNC);
  - preventivazione con tempi e pesi;
  - certificati di materiale EN 10204 3.1 legati alla colata;
  - nesting della lamiera;
  - controllo dimensionale con strumenti tarati.
- **Integrazioni mirate:**
  1. Programmi CNC come allegati versionati della fase, scaricabili al terminale.
  2. **Certificato 3.1 sul lotto di materiale**, con numero di colata, richiamato nel certificato di conformità al cliente.
  3. Preventivo meccanico: tempi per fase per costo orario, materiale a peso, trattamenti.
  4. Taratura degli strumenti di misura, con scadenze.

### 5.3 Costruzione macchine e impianti (nuova attività configurabile)
- **Abbiamo:**
  - la configurazione per reparti (produzione meccanica, montaggio, quadristi, collaudo, service);
  - commesse con fasi;
  - verifica 61439 del quadro a bordo macchina;
  - rapportini presso il cliente.
- **Gli altri hanno:**
  - ufficio tecnico con revisioni e modifiche tecniche;
  - commessa su più livelli (gruppi e sottogruppi) con avanzamento percentuale;
  - service post-vendita per matricola, con garanzia e ricambi;
  - collaudi in fabbrica e presso il cliente (FAT e SAT);
  - fascicolo tecnico e dichiarazione CE.
- **Sviluppi decisi (in quest'ordine):**
  1. **Ufficio tecnico**: revisioni di distinte e cicli, allegati (disegni, schemi), modifiche tecniche approvate che aggiornano le commesse aperte.
  2. **Collaudo macchine e CE**: liste di controllo FAT e SAT, fascicolo tecnico, dichiarazione CE secondo la Direttiva Macchine e il Regolamento macchine UE 2023/1230, obbligatorio dal 20/01/2027.
  3. **Service post-vendita**: macchine installate con matricola e garanzia, richieste di assistenza, interventi con rapportino, ricambi dalla distinta, storico per matricola.
- **Integrazioni mirate:** distinte dal CAD meccanico (SolidWorks, Inventor, Solid Edge) tramite Excel o CSV, e matricole con codice QR sulla macchina che aprono lo storico.

### 5.4 Impiantistica e installazioni
- **Abbiamo:**
  - rapportini con ore, materiali e firma del cliente da telefono;
  - costi del cantiere nella commessa;
  - DDT e fattura.
- **Gli altri hanno:**
  - computo metrico e preventivi da listini (Metel, Angaisa);
  - avanzamento lavori e SAL;
  - pianificazione delle squadre su calendario;
  - richieste di intervento (ticket);
  - contratti di manutenzione periodica sugli impianti dei clienti;
  - **dichiarazione di conformità secondo il DM 37/08**;
  - magazzino del furgone.
- **Integrazioni mirate:**
  1. **Dichiarazione di conformità DM 37/08** generata dal cantiere, con i materiali del rapportino.
  2. **SAL e fatture di acconto** dalla commessa.
  3. **Calendario delle squadre** e richieste di intervento, riusando il futuro modulo Service.
  4. Listini Metel e Angaisa per i preventivi.

### 5.5 Alimentare
- **Abbiamo:**
  - scadenze dei lotti, con prelievo del lotto che scade prima;
  - ingredienti e allergeni dalla distinta;
  - etichetta del lotto;
  - pallet con codice SSCC ed etichetta GS1-128;
  - richiamo avanti e indietro;
  - registri HACCP.
- **Gli altri hanno:**
  - ricette con resa e cali peso;
  - **tabella nutrizionale** secondo il Reg. UE 1169/2011;
  - pesatura con bilance collegate;
  - procedure di pulizia per gli allergeni;
  - preparazione agli audit IFS e BRC;
  - gestione dei resi.
- **Integrazioni mirate:**
  1. **Valori nutrizionali** per materia prima e calcolo sull'etichetta.
  2. Collegamento con le **bilance**, tramite il gateway macchine già esistente.
  3. Ricetta con resa e calo peso, che genera quantità e costi.
  4. Piano HACCP guidato per tipologia di azienda.

### 5.6 Manifattura generica
I moduli comuni coprono il fabbisogno. Il vuoto che si nota di più è il **calcolo dei fabbisogni sulle distinte**, con le proposte d'ordine ai fornitori.

## 6. I nostri pro e contro

**Pro**
- Un solo sistema dal preventivo alla fattura, con requisiti italiani nativi: DDT, FatturaPA validata sullo schema ufficiale, 61439, HACCP, conto lavoro.
- Si configura per attività multiple e reparti; per ogni ruolo e area si decide se usarlo da desktop, dal web o da entrambi.
- Tracciabilità lotti completa, con richiamo in un clic.
- Interconnessione predisposta: token per macchina e gateway OPC UA/MQTT con coda locale.
- Costi e margini reali per commessa, riservati alla direzione.
- Sicurezza curata:
  - token a rotazione, limiti ai tentativi, blocco dell'account;
  - protezioni del browser (CSP) sulle pagine web;
  - registro delle operazioni;
  - backup verificato.
- Oltre 440 test automatici e una pipeline che controlla ogni rilascio.
- Aggiornamento automatico del client e installer che non richiede i permessi di amministratore di Windows.
- Nessun costo di licenza di terzi: si può proporre a un prezzo competitivo.

**Contro**
- Invio allo SdI, fatture passive e scadenziario assenti: il commercialista resta su un altro programma.
- Nessuna schedulazione a capacità finita e nessuna istruzione di lavoro alla postazione.
- Energia non misurata: oggi non basta per la perizia dell'iperammortamento.
- La piattaforma web non copre ancora tutto: mancano qualità, HACCP, cantiere, manutenzione e pianificazione, e sul web oggi si consulta, non si crea.
- Il client completo gira solo su Windows.
- Non ci sono autenticazione a due fattori, accesso unico aziendale né più lingue.
- Hosting sul piano gratuito: nessuna garanzia di continuità finché non si passa a un piano a pagamento.
- Prodotto giovane: nessuna referenza pubblica, nessuna certificazione, sviluppo e assistenza concentrati su poche persone.

## 7. Cosa hanno gli altri e noi no: priorità proposte

Impatto: valore per il cliente e per la vendita. Impegno: stima relativa (S piccolo, M medio, L grande).

| # | Funzione mancante | Chi la chiede | Impatto | Impegno | Nota |
|---|---|---|---|---|---|
| 1 | Ufficio tecnico: revisioni, allegati, modifiche tecniche | Ingegnere meccanico, capo reparto | Alto | M | Già deciso; serve anche per le istruzioni di lavoro |
| 2 | Collaudo macchine FAT/SAT e dichiarazione CE | Responsabile tecnico, costruttori | Alto | M | Regolamento UE 2023/1230 in vigore dal 20/01/2027 |
| 3 | Service post-vendita con matricole | Commerciale, capo area | Alto | M | Deciso; margini alti su ricambi e interventi |
| 4 | Monitoraggio energetico per l'iperammortamento 2026 | Ingegnere 4.0/5.0, commerciale | Molto alto | M | Il gateway esiste già: vanno aggiunti kWh per macchina e il report ex ante ed ex post |
| 5 | Invio SdI con intermediario e fatture passive | Commercialista, specialista fatturazione | Alto | M | Serve la scelta dell'intermediario (costo) |
| 6 | Scadenziario incassi e pagamenti, solleciti | Commercialista | Alto | M | |
| 7 | Autenticazione a due fattori e accesso unico aziendale | Cybersecurity manager | Alto (NIS2, clienti strutturati) | S-M | |
| 8 | Istruzioni di lavoro e disegni al terminale | Capo reparto | Alto | S dopo il punto 1 | |
| 9 | Schedulazione a capacità finita (Gantt con vincoli) | Ingegnere gestionale | Alto | L | |
| 10 | Calcolo fabbisogni sulle distinte con proposte d'ordine | Ingegnere gestionale, acquisti | Medio-alto | M | |
| 11 | Magazzino con ubicazioni, inventario a barcode dal telefono, etichette per stampanti industriali | Capo area, magazzino | Medio-alto | M | |
| 12 | DM 37/08, SAL, calendario squadre | Capocantiere, responsabile impianti | Alto per l'impiantistica | M | |
| 13 | Import da EPLAN, lista fili, Metel | Quadristi | Alto per i quadristi | S-M | Metel in attesa di un file reale |
| 14 | Certificati 3.1 e versioni dei programmi CNC | Meccanica | Medio | S | |
| 15 | Tabella nutrizionale, ricette con resa, bilance | Alimentare | Alto per l'alimentare | M | |
| 16 | Presenze e timbrature | Capo area, commercialista | Medio | M | |
| 17 | Taratura strumenti, azioni correttive, audit ISO 9001 | Qualità | Medio | S-M | |
| 18 | Assistente IA sui dati di produzione | Data analyst | Medio (fa colpo in vendita) | M | Dopo che i dati sono completi |
| 19 | Portale clienti e fornitori | Commerciale | Medio | M | La piattaforma web è la base |
| 20 | Più lingue, API documentate per terzi, app offline per il magazzino | Ingegnere informatico | Medio | M | |

## 8. Proposta di percorso

1. **Adesso, per completare il lavoro avviato**:
   - finire la configurazione per reparti nel terminale e nella pagina tecnici (selettore "mio reparto / tutto");
   - poi Ufficio tecnico, Collaudo e CE, Service post-vendita.
2. **Subito dopo, per vendere con l'incentivo**: monitoraggio energetico con report per la perizia; OEE e andon in tempo reale dai dati macchina.
3. **Per chiudere il ciclo amministrativo**: intermediario SdI, fatture passive, scadenziario (serve la tua scelta dell'intermediario).
4. **Sicurezza e fiducia**: autenticazione a due fattori, hosting a pagamento, documentazione dell'API.
5. **Profondità di reparto**: schedulazione a capacità finita, fabbisogni sulle distinte, magazzino con ubicazioni.
6. **Pacchetti di settore**: EPLAN e lista fili, DM 37/08 e SAL, certificati 3.1, tabella nutrizionale e bilance.

## Fonti

- [I 7 migliori software MES per l'Industria 4.0 nel 2026 (Logisticamente)](https://www.logisticamente.it/articoli/57360/migliori-software-mes-industria-2026-funzioni-vantaggi-prezzi/)
- [Confronto MES per PMI manifatturiere (SiVaF)](https://www.sivaf.it/confronto-mes-pmi-manifatturiere/)
- [Mercato dei MES in forte crescita in Italia, studio AlixPartners e Qualitas](https://www.alixpartners.com/newsroom/mercato-dei-mes-in-forte-crescita-in-italia-studio-alixpartners-e-qualitas/)
- [Best Manufacturing Software 2026 (Morsa)](https://morsa.ai/guides/best-manufacturing-software)
- [Best Katana Alternatives 2026 (Qoblex)](https://qoblex.com/blog/8-best-katana-mrp-alternatives-in-2026-ranked-compared/)
- [TeamSystem: produzione e MES in TS Enterprise](https://www.teamsystem.com/aziende/enterprise/funzionalita/produzione-mes-ts-enteprise/)
- [Nuova Transizione 5.0 per il 2026: iperammortamento (BibLus)](https://biblus.acca.it/transizione-5-0-credito-imposta-efficientamento-energetico/)
- [Software di monitoraggio energetico, MES e IoT per la Transizione 5.0 (Consulmarc)](https://www.consulmarc.it/2025/12/22/monitoraggio-energetico-software-mes-transizione-5-0/)
- [Piano Transizione 5.0 (MIMIT)](https://www.mimit.gov.it/it/incentivi/piano-transizione-5-0)
- [EPLAN e interfacce verso ERP e PLM (FAU FAPS)](https://www.faps.fau.eu/?p=2020)
- [Gestionali per elettricisti e impiantisti (Koalendar)](https://koalendar.com/it/blog/miglior-gestionale-elettricisti)
- [Antos Impianti.net per impiantisti (01net)](https://www.01net.it/?p=71705)
- [TeamSystem Construction: preventivi e computo metrico](https://www.teamsystem.com/construction/teamsystem-cantieri/funzionalita/preventivi-computo-metrico/)
- [Software di tracciabilità alimentare (Capterra)](https://www.capterra.it/directory/30563/food-traceability/software)
- [Tracciabilità dei prodotti da forno con V5 (SG Systems)](https://sgsystemsglobal.com/it/?p=6779)
- [Service post-vendita e ERP per costruttori di macchine (Edana)](https://edana.ch/en/2026/02/09/industrial-after-sales-service-erp-as-a-driver-of-customer-loyalty-profitability-and-industry-4-0-maintenance/)
- [Copiloti IA nei MES (Critical Manufacturing)](https://www.criticalmanufacturing.com/blog/the-rise-of-copilots-transforming-the-interaction-with-manufacturing-execution-systems-mes/)
- [5 tendenze MES per il 2026 (SPK)](https://www.spkaa.com/blog/5-mes-trends-for-2026-and-how-to-get-ahead)
- [Tendenze ERP e MES 2026 (PSI)](https://www.psi.de/en/trends/article/erp-mes-trends-for-2026-these-developments-will-shape-the-new-year)
