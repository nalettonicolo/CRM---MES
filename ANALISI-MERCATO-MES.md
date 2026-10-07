# Analisi di mercato dei MES e gestionali — confronto con Nicolò MES

Aggiornata al **2 ottobre 2026** (sera). Allineata a [STATO-PROGETTO.md](STATO-PROGETTO.md), [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md) e ricerca di mercato ripresa in questa data.
Punto di vista: commerciale, commercialista, fatturazione, capocantiere, responsabile tecnico, capo reparto, ingegneri (gestionale / meccanico / informatico / 4.0–5.0), cybersecurity, data analyst, graphic designer.

---

## Ricerca competitor approfondita — 6 ottobre 2026

Ricerca web di giornata sui competitor citati e verifica delle affermazioni già presenti in questo documento. I prezzi vengono da aggregatori e da pagine di terzi, non dai listini ufficiali dei fornitori: prima di usarli in trattativa vanno confermati.

### Correzioni al documento precedente
- **Bravo** è il prodotto di **Antos SRL** (Marche), "Bravo Manufacturing" con gateway edge plug-and-play. Il prezzo "Bravo Plus ~290 €/risorsa/anno (min. 10)" che avevamo scritto **non è stato verificato** e viene tolto dal confronto finché non si trova una fonte.
- **siMES**: nessuna fonte trovata in questa ricerca. Il prezzo "da ~3.500 €/macchina" è **non verificato**.
- **Opera MES** è di Cybertec (dal 1991), disponibile cloud/SaaS/web. **NET@PRO** parla 7 lingue e offre cloud, on-premise o ibrido.

### Prezzi competitor (listino pubblico, 2026)

| Prodotto | Modello | Prezzo indicativo | Note |
|---|---|---|---|
| Katana MRP | Free / Core / Advantage | Free; Core **$299/mese**; Advantage su preventivo | Il piano più alto è sceso da ~$1.800 a $299 nel febbraio 2026; un piano aggiunto a luglio 2026 |
| MRPeasy | 5 piani per utente | **$49–149 per utente/mese** | Il piano top è salito a $149 nel febbraio 2026 |
| Odoo (Italia, EUR) | Standard / Custom | Standard **€24,90**/utente/mese (€19,90 annuale promo); Custom **€37,40** | Il manifatturiero è incluso nei piani, non venduto a parte |
| Tulip (MES frontline) | Essentials / Professional | Essentials **$100/interfaccia/mese**, minimo 10 interfacce (~$12k/anno); Professional **$250/interfaccia/mese** (~$30k/anno minimo) | Il prezzo cresce con le postazioni |
| Retrofit OEE (es. Teeptrak) | Abbonamento + hardware | **€30–150/macchina/mese** + hardware **€200–2.000/macchina** | Dato indicativo da un fornitore: un progetto MES completo costa molto di più |
| Siemens Opcenter, Plex, AVEVA | Enterprise | Non pubblico | Progetti di mesi; fuori dallo stesso budget di una PMI |
| **Nicolò MES** | — | **Non definito** | Nessun listino nei documenti del repository: senza un prezzo non si può fare confronto di costo |

**Dato da decidere:** il prezzo di Nicolò MES. Senza, il confronto con MRPeasy o Odoo resta solo di funzioni.

### Pro e contro dei competitor (sintesi)

- **Katana / MRPeasy** — *Pro:* costo basso, avvio rapido, cloud, internazionali. *Contro:* fiscale e FatturaPA italiana debole, poca macchina e shop-floor profondo.
- **Odoo** — *Pro:* ecosistema enorme, moduli manifatturieri inclusi, localizzazione italiana presente. *Contro:* complessità, costi di implementazione (spesso maggiori del software), dipendenza da partner per la personalizzazione.
- **Tulip** — *Pro:* app di reparto senza codice, molto forte sul frontline. *Contro:* costo per postazione che cresce in fretta, serve un reparto IT interno, nessuna logica fiscale italiana.
- **Bravo (Antos)** — *Pro:* gateway per le macchine plug-and-play, prodotto italiano, esperienza su progetti IoT (dichiarazioni del fornitore: oltre 100 gateway, 30 progetti). *Contro:* prezzi non pubblici; la copertura fiscale italiana non è verificata da questa ricerca (il documento precedente la dava per "ERP esterno", da confermare).
- **Opera MES (Cybertec) / NET@PRO** — *Pro:* MES italiani, distribuzione cloud o locale, tracciabilità. *Contro:* il fiscale italiano non è il loro punto forte (da verificare caso per caso), comunicazione commerciale meno diretta.
- **TeamSystem Manufacturing / Zucchetti Manufacturing** — *Pro:* rete commerciale, fiscale e contabilità nativi, fiducia delle PMI. *Contro:* dal materiale del fornitore il MES appare come parte di un'offerta più ampia (manufacturing + ERP); la profondità di reparto non è stata verificata sul campo.
- **Siemens Opcenter / enterprise** — *Pro:* profondità, APS, integrazione PLC/SCADA. *Contro:* costo e tempi, non per una PMI.

### Pro e contro di Nicolò MES (verificati sul repository)

**Pro**
- Ampiezza in un solo prodotto: 51 controller API, moduli di settore (61439, HACCP, rapportini, FAT/SAT/CE, service, energia), MRP multi-livello, magazzino, qualità, fiscale.
- Fiscale italiano nativo: FatturaPA validata contro lo schema ufficiale, DDT con numerazione, conto lavoro.
- Canali multipli: desktop Windows, web, pagina tecnici da telefono; installazione in cloud o sul server del cliente.
- Sicurezza: 2FA, blocco account, limiti di richiesta, header di sicurezza su tutte le superfici (esteso oggi a `/api`), registro operazioni, backup notturno.
- Test automatici: 649 in totale, tutti verdi a questa data.

**Contro**
- **Nessuna referenza né prova sul campo**: nessuna macchina reale collegata, nessun cliente pubblico. È il rischio commerciale più alto.
- **Prezzo non definito.**
- **Single-tenant**: un'installazione per azienda, nessun modello SaaS multi-cliente.
- **Desktop Windows-first**: l'app nativa è solo Windows; il web copre una parte delle funzioni.
- **Registro operazioni parziale**: circa 23 controller su 51 scrivono traccia (oggi se ne sono aggiunti 2).
- **Nessuna policy GDPR** (conservazione, cancellazione su richiesta).
- **Validazione input manuale** endpoint per endpoint; **API non versionata**; **Swagger pubblico** (scelta del 02/10 da confermare).
- **Import PDF cataloghi** non validato su cataloghi reali.
- **SSO** incompleto: manca il mapper OIDC di produzione.
- **Dipendenza dal titolare** per SdI, hosting a pagamento e firma digitale del software.

### Dove ci posizioniamo (ipotesi da verificare)
Il vantaggio è **l'ampiezza con il fiscale italiano nativo** (MRPeasy e Katana non lo hanno, Bravo lo integra con l'ERP). Il punto debole è **la mancanza di prove**: senza referenze, un cliente sceglie l'attore già conosciuto. Quindi il prossimo passo di mercato conta più del prossimo modulo.

### Punti da migliorare, in ordine di impatto

| # | Punto | Stato oggi | Chi |
|---|---|---|---|
| 1 | Pilota con macchina reale e prima referenza | Aperto | Titolare / campo |
| 2 | Definire il prezzo pubblico e il modello commerciale | Aperto | Titolare |
| 3 | Registro operazioni su tutti i controller che modificano dati | Parziale: fornitori e corrieri fatti oggi, ~27 controller restanti | Codice |
| 4 | Vulnerabilità delle dipendenze di test | Quasi chiuso: resta un avviso moderato su AngleSharp (dipendenza di bUnit, solo test) | Codice |
| 5 | Header di sicurezza, limite dimensione richieste, health check | Fatto oggi | Codice |
| 6 | Decisione su Swagger pubblico | Aperto | Titolare |
| 7 | Policy GDPR e cancellazione dati su richiesta | Assente | Titolare (decisioni legali) + codice |
| 8 | Validazione input sistematica e versionamento API | Non iniziato, da pianificare | Codice |
| 9 | Verifica del backup notturno | Bloccato: serve autorizzare `gh` | Titolare |
| 10 | Import PDF su cataloghi reali | Da verificare | Campo |
| 11 | Mapper OIDC di produzione per SSO | Configurazione | Titolare |

## Approfondimento 7 ottobre 2026

Seconda ricerca per chiudere i punti rimasti deboli (fonti dirette su Bravo/Antos e siMES) e due questioni nuove: un competitor PMI non ancora censito e requisiti concreti per il gap GDPR.

### Bravo (Antos) — confermato, prezzo ancora non trovato
Bravo Manufacturing è un prodotto reale di **Antos** (software house marchigiana): governa l'avanzamento degli ordini di produzione, porta la documentazione in ufficio, raccoglie dati di campo con più strumenti (tastiera, barcode, touch screen), genera report e esporta dati verso altri software aziendali. Nessuna fonte con il prezzo, nemmeno in questa seconda ricerca: resta un buco nel confronto di costo.

### siMES — non trovato in due ricerche separate
Né questa né la ricerca del 6 ottobre hanno trovato un sito ufficiale o una fonte diretta per un prodotto chiamato "siMES". È possibile che il nome nel documento originale fosse impreciso o che si tratti di un prodotto di nicchia poco indicizzato. **Raccomandazione:** non riusare questo nome in materiale commerciale finché qualcuno non conferma il prodotto esatto (magari è "SiVaF" o un altro nome); per ora resta fuori dal confronto affidabile.

### Nuovo competitor: Mago.Net (Microarea)
ERP italiano per PMI in quattro edizioni (Standard, Professional Lite, Professional, Enterprise). L'edizione Enterprise include controllo di produzione e pianificazione risorse, gestione disegni con revisioni, note tecniche sull'anagrafica prodotto, consuntivazione per fase di lavorazione, multicalendario. È lo stesso schema di TeamSystem/Zucchetti: **ERP con produzione come modulo**, non MES nativo — punto di forza sul fiscale/gestionale, punto debole sulla profondità di reparto (non verificata in questa ricerca).

### GDPR — un riferimento concreto per il gap, non consulenza legale
Il Garante Privacy ha approvato (novembre 2024, in vigore) il primo **Codice di condotta per i produttori di software gestionale**, promosso da Assosoftware ai sensi degli artt. 40-41 GDPR. Punti operativi utili come traccia (non sono consigli legali — vanno validati con un legale o un DPO prima di implementarli):
- **Privacy by design e by default**: i principi di protezione dati vanno nel software fin dalla progettazione, non aggiunti dopo.
- **Minimizzazione dei dati** raccolti e **cifratura dei dati sensibili**.
- **Trasparenza** sull'uso dei dati verso l'utente finale.
- Il produttore, quando tratta dati per conto del cliente (installazione, assistenza, manutenzione), può assumere il ruolo di **responsabile del trattamento** ex art. 28 GDPR — con gli obblighi che ne derivano.
- **Aperto a tutte le aziende produttrici di software**, non solo ai soci Assosoftware, purché rispettino i requisiti.
- Non siamo riusciti a estrarre dal codice i tempi di conservazione esatti (il PDF ufficiale non è leggibile in modo automatico): va recuperato il testo integrale prima di scrivere una policy di retention basata su questo codice.

**Collegamento con "Cosa manca" (punto 7 sopra):** questo codice di condotta è il punto di riferimento di settore più concreto trovato finora per impostare una policy GDPR credibile — meglio usarlo come base che inventare da zero, ma il passo successivo resta una decisione del titolare con un legale, non un'implementazione autonoma.

### Fonti (7 ottobre)
- Bravo / Antos: [Antos vicina al rilascio di Bravo Manufacturing](https://www.01net.it/antos-vicina-al-rilascio-di-bravo-manufacturing/), [caso d'uso Alleantia](https://www.alleantia.com/resources/use-cases/antos/)
- Mago.Net / Microarea: [ricerca 01net su Microarea](https://www.01net.it/?p=91629)
- Codice di condotta software gestionali: [Agenda Digitale](https://www.agendadigitale.eu/cultura-digitale/codice-di-condotta-per-i-software-gestionali-tutela-dei-dati-al-centro-dello-sviluppo/), [Federprivacy](https://www.federprivacy.org/informazione/primo-piano/in-vigore-il-codice-di-condotta-sullo-sviluppo-e-produzione-di-software-gestionale-approvato-dal-garante-privacy), [testo del codice (PDF)](https://lentepubblica.it/wp-content/uploads/2024/12/Codice-di-condotta-per-il-trattamento-dei-dati-personali-effettuato-dalle-imprese-di-sviluppo-e-produzione-di-software-gestionale.pdf)

### Fonti
- Katana: [prezzi 2026](https://costbench.com/software/inventory-management/katana-mrp/), [ribasso febbraio 2026](https://costbench.com/changelog/katana-price-decrease-2026-02-2/), [piano luglio 2026](https://costbench.com/changelog/katana-plan-added-2026-07/)
- MRPeasy: [prezzi 2026](https://erpresearch.com/pricing/mrpeasy), [aumento febbraio 2026](https://costbench.com/changelog/mrpeasy-price-increase-2026-02/)
- Odoo Italia: [prezzi EUR](https://oec.sh/odoo-pricing/italy)
- Tulip: [prezzi](https://toolradar.com/tools/tulip/pricing), [piani](https://pricingsaas.com/companies/tulip)
- Retrofit OEE: [Teeptrak, prezzi indicativi](https://teeptrak.com/es/precio-software-oee/) (fonte di parte)
- Bravo / Antos: [caso d'uso Antos](https://www.alleantia.com/resources/use-cases/antos/)
- Opera MES: [scheda Capterra](https://www.capterra.it/software/1065494/opera-mes)
- NET@PRO: [scheda Capterra](https://www.capterra.it/software/216295/net-pro)
- TeamSystem: [guida MES 2026](https://www.teamsystem.com/magazine/manufacturing/mes-software-smart-factory-guida-2026/)
- Siemens: [pagina MES](https://www.siemens.com/it-it/solutions/manufacturing-execution-system-mes/)

---

## Aggiornamento 6 ottobre 2026 (stato reale)

- **Pubblicato:** G8–G15 e contro su `main` (`6cad42d`, `293f14f`); GTIN di prodotto con cifra di controllo GS1 e modelli HACCP per tipologia (`48818e8`); correzioni CI installer (`e9a6d3c`, build verde). Suite: API 414, Desktop 122, Web 77, Console 18.
- **Superate le voci "contro" del 2 ottobre:** push e deploy (fatto); modulo passive/scadenziario (fatto); MRP con crea PO (fatto); ubicazioni e inventario (fatto); capacità finita (fatto); CAPA, taratura e presenze (base fatti); SdI con provider HTTP e stati (fatto, manca il contratto).
- **Ancora aperto, in ordine di impatto commerciale:**
  1. Contratto con un intermediario SdI e relative chiavi (titolare).
  2. Un contatore o una macchina reale collegata in officina, per la perizia (campo).
  3. Hosting a pagamento e 1–2 referenze, G16 (titolare).
  4. Mapper OIDC di produzione per SSO aziendale (configurazione).
  5. Campo GTIN nelle schermate desktop e web del prodotto (oggi solo via API): prossimo passo di sviluppo.
  6. Backup notturno: il segreto `NEON_DATABASE_URL` va verificato su GitHub, il job fallisce ogni notte.
- **Limiti da dichiarare ai clienti:** IA come assistente, non come pianificazione predittiva; contabilità fino a fattura e scadenziario, non prima nota; verticali (DNC, computo, IFS) non profondi come gli specialisti.

## 1. In sintesi

- **Il mercato.** In Italia i MES restano pezzo centrale della digitalizzazione di fabbrica: studio AlixPartners–Qualitas (marzo 2025) → **~120 M€ entro il 2027**, CAGR **5,2%** (2022–2027). Europa occidentale CAGR **6,7%** (~**887 M€** al 2027); globale ~**3 Md€** al 2027 (CAGR ~5,6%). Mercato italiano **frammentato (~23 fornitori)**: consolidamento, uscita dal “fatto in casa”, cloud, IoT/edge, AI.
- **Cosa distingue i prodotti.** Connettività macchina reale, dati in tempo reale, integrazione ERP/contabilità, documentazione di **interconnessione** per la perizia dell’iperammortamento. I MES “solo reparto” vincono sul campo; chi ha anche commerciale e fiscale italiano riduce i pezzi da integrare.
- **Posizionamento Nicolò.** Un solo prodotto: produzione (MES) + commerciale + documenti italiani (DDT, FatturaPA) + moduli di settore (61439, HACCP, rapportini, FAT/SAT/CE, service, energia, ufficio tecnico) + MRP, magazzino, qualità. Desktop + web + telefono. I MES PMI tipici restano sul reparto e demandano un ERP esterno.
- **Momento commerciale 2026–2028.** Iperammortamento (L. 199/2025): maggiorazione **180% / 100% / 50%** su scaglioni fino a 20 M€, investimenti **1/1/2026–30/9/2028**. Software Allegato V agevolabile se **interconnesso**. Dal DL 38/2026 il vincolo **origine UE sui beni è stato soppresso** (retroattivo); restano obbligatori interconnessione, perizia e comunicazioni GSE. I canoni SaaS restano zona grigia: meglio vendere/licenziare in modo capitalizzabile quando possibile.

---

## 2. Mappa del mercato (MES + gestionali)

| Fascia | Esempi | Per chi | Prezzo indicativo (2026) | Punto forte | Limite per una PMI |
|---|---|---|---|---|---|
| MES enterprise | Siemens Opcenter, SAP DM, Rockwell/Plex, AVEVA, Critical Manufacturing | Grandi gruppi | Decine–centinaia di k€ + canoni | Profondità, PLC/SCADA, APS, AI | Costo, mesi di progetto, integratori |
| MES italiani PMI | Bravo, siMES (SiVaF), Opera MES, MES Factory, NET@PRO, moduli TeamSystem/Zucchetti | PMI manifatturiere | Bravo Plus ~**290 €/risorsa**/anno (min. 10); siMES da ~**3.500 €/macchina** + canone | OEE da macchina, IoT, connettori ERP, marketing 4.0/5.0 | Quasi sempre **solo reparto**: DDT/FatturaPA/clienti nell’ERP |
| ERP / gestionali IT | TeamSystem, Zucchetti, Danea, Fatture in Cloud + add-on | PMI di ogni settore | Canoni per utente | Fiscale IT, fatturazione, contabilità | Produzione e campo deboli o assenti |
| MRP / ERP cloud esteri | MRPeasy, Katana, Odoo MRP | Piccole aziende | Decine–centinaia $/mese | Avvio rapido, distinte, magazzino | Fiscale IT debole, poche macchine |
| MES no-code | Tulip e simili | Chi ha UT interno | Centinaia $/mese | App di reparto su misura | Va costruito tutto |
| Verticali cantieri/impianti | TeamSystem Cantieri, Antos, D-TEC, mInterventi | Impiantisti | Canoni utente | Computo, ticket, app tecnici | Niente fabbrica |
| Verticali food | Plex/AVEVA food, FoodDocs, SafetyChain | Alimentare | SaaS → progetti | HACCP, audit | Spesso esteri; fiscale IT assente |

**Lettura competitiva.** Bravo = costi di commessa + IoT add-on + ERP esterno. siMES = AI e retrofit macchine. Opera / NET@PRO = modularità produzione + connettori. I gestionali italiani (TeamSystem/Zucchetti) vincono sul fiscale e perdono sul shop-floor. **Nessuno nella fascia PMI mette insieme FatturaPA nativa + DDT + MES di fabbrica + settori IT** come Nicolò: il confronto tipico resta “MES + ERP già in casa”, non “un solo prodotto”.

---

## 3. Cosa chiede il mercato nel 2026

1. Interconnessione vera (OPC UA, MQTT, Modbus…) e prove per la perizia.
2. Energia / misure utili alla documentazione agevolabile.
3. Schedulazione a capacità finita (+ in prospettiva copilota AI con umano in loop — Gartner MES Guide 2026).
4. Istruzioni digitali alla postazione.
5. Cloud **e** on-premise; mobile.
6. Time-to-value 2–4 mesi per PMI ~20 macchine (licenza spesso la parte minore del TCO).
7. Sicurezza (2FA, audit; NIS2 per clienti strutturati).
8. Un solo posto dove chiudere il ciclo ordine → produzione → DDT → fattura (soprattutto sotto le 50–100 persone).

---

## 4. Confronto funzione per funzione

Legenda: ✅ c’è · 🟡 parziale · ❌ manca · 📦 codice pronto / da pubblicare su `main`

| Area | Nicolò MES (02/10/2026 sera) | MES PMI IT | MRP cloud | ERP IT | Enterprise |
|---|---|---|---|---|---|
| Distinte, cicli, commesse, fasi | ✅ | ✅ | ✅ | 🟡 | ✅ |
| Terminale reparto PIN (+ offline) | ✅ | ✅ | 🟡 | ❌ | ✅ |
| Filtro “mio reparto” | ✅ | ✅ | ❌ | ❌ | ✅ |
| Tracciabilità lotti / richiamo | ✅ | ✅ | 🟡 | 🟡 | ✅ |
| Matricole / unità | ✅ | 🟡 | 🟡 | ❌ | ✅ |
| OEE (fasi + macchina se ci sono letture) | ✅ | ✅ da macchina | ❌ | ❌ | ✅ |
| Gateway OPC/MQTT | ✅ predisposto; campo reale ❌ | ✅ | ❌ | ❌ | ✅ |
| Energia / progetti perizia | ✅ software; contatore reale ❌ | ✅ | ❌ | ❌ | ✅ |
| Capacità finita | ✅ (📦) | 🟡/✅ | 🟡 | ❌ | ✅ |
| Ufficio tecnico / revisioni | ✅ | 🟡 | ❌ | ❌ | ✅ |
| FAT/SAT + fascicolo + CE/UE | ✅ | 🟡 | ❌ | ❌ | ✅ |
| Service post-vendita | ✅ | 🟡 | ❌ | 🟡 | ✅ |
| Qualità NC + piani | ✅ | ✅ | 🟡 | 🟡 | ✅ |
| Taratura strumenti + CAPA | ✅ base (📦) | 🟡 | ❌ | ❌ | ✅ |
| Manutenzione | ✅ | ✅ | ❌ | 🟡 | ✅ |
| MRP multi-livello + crea PO | ✅ (📦) | 🟡 via ERP | ✅ | 🟡 | ✅ |
| Ubicazioni / inventario | ✅ (📦) | 🟡 | ✅ | 🟡 | ✅ |
| Clienti / preventivi | ✅ | ❌ (ERP) | ✅ | ✅ | 🟡 |
| DDT / conto lavoro | ✅ | ❌ | 🟡 | 🟡 | 🟡 |
| FatturaPA XML validata | ✅ | ❌ (ERP) | ❌ | ✅ | ❌ |
| Invio SdI automatico | 🟡 stub+stati (📦); intermediario reale ❌ | ❌ | ❌ | ✅ (spesso) | ❌ |
| Passive + scadenziario | ✅ | ❌ | 🟡 | ✅ | ❌ |
| Margini di commessa | ✅ | 🟡 | 🟡 | 🟡 | ✅ |
| Presenze / timbrature | ✅ base (📦) | 🟡 | ❌ | ✅ | ✅ |
| Web + telefono | ✅ in crescita (📦 board, pack, qualità…) | ✅ | ✅ | ✅ | ✅ |
| Cloud o server cliente | ✅ entrambi | ✅ | solo cloud | ✅ | ✅ |
| 2FA | ✅; SSO ❌ | 🟡 | ✅ | 🟡 | ✅ |
| API documentate (Swagger) | ✅ (📦) | 🟡 | ✅ | 🟡 | ✅ |
| Assistente IA | ❌ | 🟡 | 🟡 | 🟡 | ✅ |
| Multilingua / valute | ❌ | 🟡 | ✅ | 🟡 | ✅ |
| Temi grafici azienda | ✅ | 🟡 | 🟡 | 🟡 | ✅ |
| Pack settore (EPLAN, DM37, SAL, 3.1…) | ✅ base (📦) | 🟡 | ❌ | 🟡 | 🟡 |

---

## 5. Settore per settore (vs verticali)

### 5.1 Quadri elettrici
**Abbiamo:** 61439, Metel, EPLAN→BOM, lista cavi, DDT/fattura, costi.
**Resta vs specialisti:** etichette cavi/morsetti dedicate, Allegati legati alla dichiarazione in un click.

### 5.2 Meccanica / carpenteria
**Abbiamo:** cicli, conto lavoro, OEE, manutenzione, cert 3.1 su lotto, taratura strumenti.
**Resta:** DNC diretto, nesting, preventivo meccanico “avanzato”.

### 5.3 Costruzione macchine
**Abbiamo:** UT, FAT/SAT, fascicolo, CE/UE, service, energia.
**Resta:** PDF rapporto collaudo autonomo, firma qualificata, QR matricola → storico completo.

### 5.4 Impiantistica
**Abbiamo:** rapportini firmati, Metel, DM 37/08 generate, SAL, calendario squadre (base).
**Resta:** computo metrico pieno, Angaisa, magazzino furgone.

### 5.5 Alimentare
**Abbiamo:** FEFO, allergeni, SSCC, richiamo, HACCP, nutrizionale, bilance (letture software).
**Resta:** bilance hardware, IFS/BRC, ricetta con resa/cali.

### 5.6 Manifattura generica
Nucleo MES + MRP + ubicazioni + capacità finita + commerciale IT. Gap sentiti: **campo macchine**, **SdI intermediario**, **referenze**.

---

## 6. Pro e contro di Nicolò MES

### Pro — perché un cliente PMI lo sceglierebbe

1. **Tutto in uno italiano.** Dal preventivo alla FatturaPA (XML validato), con DDT e conto lavoro: niente “MES + ERP” obbligatorio.
2. **Shop-floor vero.** Terminale PIN (anche offline), fasi, fermi, NC, OEE, matricole, istruzioni/disegni alla postazione.
3. **Settori nativi.** 61439, HACCP/allergeni/SSCC, rapportini firmati, FAT/SAT/CE, service, Metel, pack EPLAN/DM37/SAL/3.1 — non add-on generici.
4. **Acquisti e magazzino.** MRP multi-livello con proposte e creazione ordini; ubicazioni e inventario; lotti e FEFO.
5. **Pianificazione.** Board settimanale (anche web) + capacità finita centri di lavoro.
6. **Qualità e HR minimi.** CAPA, taratura strumenti, timbrature — oltre le sole NCM di reparto.
7. **Agevolazioni 2026–2028.** Gateway interconnessione + energia + dichiarazione origine software già pronti per la narrazione di perizia (anche se il vincolo UE fiscale è stato tolto, resta un segnale di prodotto italiano).
8. **Canali multipli.** Desktop Windows, web `/app`, `/tecnici` da telefono; temi grafici aziendali; cloud o server cliente; 2FA; Swagger; auto-update.
9. **Costo di ingresso competitivo** rispetto a Bravo Plus (min. 10 risorse × 290 €) o a progetti enterprise, se il modello commerciale resta snello.
10. **Profondità tecnica dimostrabile.** Suite test ampia (623), CI, migrazioni, audit — non un prototipo Excel.

### Contro — dove perde o è fragile (aggiornato sera 02/10)

1. **Deploy ancora da fare.** Il codice dei contro è in tree + Neon; **manca il push** su `main`/Render (lo fai tu).
2. **SdI intermediario commerciale.** Il provider HTTP è pronto (`Sdi:Provider=http`); serve ancora **contratto + URL/chiavi** del provider (Aruba, …).
3. **Macchina fisica in officina.** Demo-feed e simulatore Python chiudono la demo commerciale; per la perizia serve un collegamento reale.
4. **SSO IdP di produzione.** Plumbing + login esterno ok; manca il mapper OIDC completo verso Azure/Google (abilitare solo dietro IdP fidato).
5. **IA “di marketing”.** Assistente stub/OpenAI presente; non è predittivo/APS come siMES.
6. **G16 / fiducia.** Hosting a pagamento, referenze pubbliche, firma .exe, Stripe — solo titolare.
7. **Contabilità completa.** Fino a fattura e scadenziario; non sostituisce TeamSystem/Zucchetti prima nota.
8. **Verticali da specialista.** Base EPLAN/DM37/SAL/3.1 sì; computo/DNC/IFS non da verticalista puro.

### Pro — aggiornamento sera

- Intermediario SdI collegabile via HTTP; demo campo senza hardware; SSO base; locale/valuta; assistente IA; Swagger; CAPA/taratura/presenze; MRP con crea PO; G13 persistenza; planning web.


---

## 7. Verdetto competitivo (una frase per fascia)

| Contro chi | Verdetto |
|---|---|
| MES PMI (Bravo, siMES, Opera…) | **Vince** su commerciale/fiscale IT e settori; **perde** su IoT plug-and-play e brand di campo finché non c’è una macchina pilota. |
| ERP italiani | **Vince** su produzione e shop-floor; **perde** su contabilità completa e rete commerciale. |
| MRP cloud (Katana, MRPeasy) | **Vince** su Italia (DDT/FatturaPA/HACCP/61439); **perde** su UX cloud-native e internazionalizzazione. |
| Enterprise | Non compete sullo stesso budget; può essere **complementare** solo se il cliente non vuole un progetto da 6–18 mesi. |

**Posizione consigliata in vendita:** *“Il gestionale di fabbrica italiano che fa anche fattura e DDT — non un MES che ti chiede di tenere l’ERP a parte.”*
**Condizione per non perdere la trattativa tecnica:** push in produzione + una demo con macchina reale + intermediario SdI (anche solo su un cliente pilota).

---

## 8. Priorità dopo questa analisi

| # | Azione | Chi | Impatto |
|---|---|---|---|
| 1 | Commit/push working tree | Dev | Alto — allinea marketing e prodotto |
| 2 | Contratto intermediario SdI | Titolare | Alto — chiude il ciclo fiscale |
| 3 | Macchina + contatore pilota | Campo | Molto alto — credibilità vs MES di campo |
| 4 | Hosting a pagamento + 1–2 referenze (G16) | Titolare | Alto — fiducia |
| 5 | Pacchetto avvio 30 giorni (template + formazione) | Commerciale | Medio-alto — time-to-value |
| 6 | SSO / IA / i18n | Codice pronto; l’IdP OIDC di produzione è configurazione titolare | — |

---

## 9. Cosa è cambiato rispetto all’analisi di stamattina

Chiusi in codice (Neon ok, push pending): MRP multi-livello + crea PO; G13 persistenza; planning web; CAPA/taratura; presenze; Swagger; suite 623/623.
Normativa: vincolo **origine UE** sull’iperammortamento **eliminato** (DL 38/2026); restano interconnessione e perizia.
Aperti strutturali: deploy, SdI reale, campo, G16, IA/SSO/i18n.

---

## Fonti

### Mercato
- [AlixPartners–Qualitas — MES Italia 120 M€ al 2027](https://www.alixpartners.com/newsroom/mercato-dei-mes-in-forte-crescita-in-italia-studio-alixpartners-e-qualitas/)
- [Industria Italiana — evoluzione MES](https://www.industriaitaliana.it/manufacturing-execution-systems-alixpartners-digitalizzazione-manifattura/)
- [Qualitas — trend MES](https://www.qualitas.it/blog/un-mercato-in-crescita-i-trend-del-mes)
- [Lookin — MES software 2026 costi PMI](https://lookin.cloud/blog/mes-software-produzione)
- [Gartner MES Guide 2026 — lettura Siemens](https://blogs.sw.siemens.com/opcenter/2026-gartner-market-guide-for-manufacturing-execution-systems-how-ai-is-shaping-mes-and-where-we-believe-opcenter-adds-customer-value/)

### Incentivi
- [MIMIT — Iperammortamento / Transizione 5.0](https://www.mimit.gov.it/it/incentivi/nuovo-piano-transizione-5-0-iperammortamento)
- [Heuris — iperammortamento e MES 2026](https://www.heuris.it/iperammortamento-mes-2026)
- [Guida operativa GSE / interconnessione](https://www.iperammortamenti.it/risorse/iperammortamento-guida-operativa)
- [Eliminazione vincolo origine UE (DL 38/2026)](https://www.studiofantiselleri.it/iperammortamento-2026-decreto-fiscale-beni-agevolabili/)

### Concorrenti
- [Bravo — prezzi Plus/Pro](https://www.bravomanufacturing.it/confronta-prezzi/)
- [siMES / SiVaF](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/)
- [Opera MES](https://www.operames.it/opera-mes/)
- [TeamSystem produzione/MES](https://www.teamsystem.com/aziende/enterprise/funzionalita/produzione-mes-ts-enteprise/)

Stato prodotto: [STATO-PROGETTO.md](STATO-PROGETTO.md) · Gate: [GATES.md](GATES.md) · Handoff: [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md).
