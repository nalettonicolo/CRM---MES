# Analisi di mercato dei MES e gestionali — confronto con Nicolò MES

Ricompilata il **7 ottobre 2026**, unendo in un solo documento le ricerche del 2, 6 e 7 ottobre (prima erano sezioni separate, con parti ripetute). Centrata su **cosa possiamo migliorare**: la sezione 8 è il punto di partenza per decidere il prossimo passo. Allineata a [STATO-PROGETTO.md](STATO-PROGETTO.md), [GATES.md](GATES.md) e [ANALISI-COMPETITOR-MES.md](ANALISI-COMPETITOR-MES.md).
Punto di vista: commerciale, commercialista, fatturazione, capocantiere, responsabile tecnico, capo reparto, ingegneri (gestionale / meccanico / informatico / 4.0–5.0), cybersecurity, data analyst.

I prezzi dei competitor vengono da aggregatori e pagine di terzi, non dai listini ufficiali: da confermare prima di usarli in trattativa. **Aggiornamento 08/10/2026**: Bravo, siMES e Opera MES sono stati verificati direttamente sui siti ufficiali — erano segnalati come "non verificati" nella versione precedente di questo documento. Il prezzo di siMES scritto in precedenza (~3.500 €/macchina) era **sbagliato**: quello reale è ~1.100 €/anno base, molto più basso. Lezione per le prossime ricerche: "nessuna fonte trovata" in una ricerca generica non significa che il dato sia falso, solo che va cercato con termini più mirati (nome esatto dell'azienda, non solo del prodotto) prima di scartarlo.

---

## 1. In sintesi

- **Il mercato.** Studio AlixPartners–Qualitas (marzo 2025): MES in Italia verso **~120 M€ entro il 2027**, CAGR **5,2%** (2022–2027). Europa occidentale CAGR **6,7%** (~887 M€ al 2027); globale ~3 Md€ al 2027. Una fonte più recente (RevenueBase, dati di settembre 2026) conta **26 fornitori MES con sede in Italia** (Brescia il polo principale) — un numero diverso dai "~23" della stima AlixPartners, segno che il mercato resta frammentato e che le stime cambiano secondo la fonte, non che sia cresciuto in modo misurabile in sei mesi.
- **Cosa distingue i prodotti.** Connettività macchina reale, dati in tempo reale, integrazione con ERP/contabilità, documentazione di interconnessione per la perizia degli incentivi. I MES "solo reparto" vincono sul campo; chi ha anche commerciale e fiscale italiano riduce i pezzi da integrare.
- **Posizionamento Nicolò.** Un solo prodotto: produzione (MES) + commerciale + documenti italiani (DDT, FatturaPA) + moduli di settore (61439, HACCP, rapportini, FAT/SAT/CE, service, energia, ufficio tecnico) + MRP, magazzino, qualità. Desktop + web + telefono. I MES PMI tipici restano sul reparto e demandano un ERP esterno.
- **Momento normativo 2026–2028.** La legge 88/2026 (conversione del DL fiscale 38/2026) ha reso retroattivo dal 1/1/2026 il venir meno del vincolo di origine UE sui beni agevolabili, e ha portato al 89,77% il credito per le imprese escluse dai fondi precedenti di Transizione 5.0; restano **4 miliardi di euro** disponibili sull'iperammortamento, con un'ipotesi di proroga di altri due anni e un'apertura alle imprese a forte consumo energetico. Resta comunque obbligatoria l'**interconnessione** del bene, la **perizia** e le comunicazioni GSE — l'origine del bene non c'entra più, ma il software deve comunque dialogare davvero con la macchina. I canoni SaaS restano zona grigia per l'agevolazione: meglio vendere/licenziare in modo capitalizzabile quando possibile.

---

## 2. Mappa del mercato (MES + gestionali)

| Fascia | Esempi | Per chi | Prezzo indicativo (2026) | Punto forte | Limite per una PMI |
|---|---|---|---|---|---|
| MES enterprise | Siemens Opcenter, SAP DM, Rockwell/Plex, AVEVA, Critical Manufacturing | Grandi gruppi | Decine–centinaia di k€ + canoni | Profondità, PLC/SCADA, APS, AI | Costo, mesi di progetto, integratori |
| MES italiani PMI | Bravo (Antos), siMES (Sivaf Informatica), Opera MES (Cybertec, gruppo Zucchetti), NET@PRO, moduli TeamSystem/Zucchetti, Mago.Net (Microarea) | PMI manifatturiere | **Confermati 08/10 sui siti ufficiali**: Bravo Plus **290 €/risorsa/anno** (min. 10 non confermato); siMES **~1.100 €/anno** base, 1 postazione (non ~3.500 €/macchina come scritto prima — errore corretto) | OEE da macchina, IoT, connettori ERP | Quasi sempre **solo reparto**: DDT/FatturaPA/clienti restano nell'ERP |
| ERP / gestionali IT | TeamSystem, Zucchetti, Danea, Fatture in Cloud + add-on | PMI di ogni settore | Canoni per utente (TeamSystem modulo produzione ~2.500 €/anno) | Fiscale IT, fatturazione, contabilità | Produzione e campo deboli o assenti |
| MRP / ERP cloud esteri | MRPeasy ($49–149/utente/mese), Katana (Free/$299/mese/su preventivo), Odoo (Italia €24,90–37,40/utente/mese) | Piccole aziende | Decine–centinaia $/mese | Avvio rapido, distinte, magazzino | Fiscale IT debole, poche macchine |
| MES no-code | Tulip ($100–250/interfaccia/mese, minimo 10) | Chi ha un reparto IT interno | Centinaia–migliaia $/mese | App di reparto su misura | Va costruito tutto |
| Verticali cantieri/impianti | TeamSystem Cantieri, Antos, D-TEC, mInterventi | Impiantisti | Canoni utente | Computo, ticket, app tecnici | Niente fabbrica |
| Verticali food | Plex/AVEVA food, FoodDocs, SafetyChain | Alimentare | SaaS → progetti | HACCP, audit | Spesso esteri; fiscale IT assente |
| **Nicolò MES** | — | PMI multisettore | **Non definito** — nessun listino pubblico | Unico con fiscale IT nativo + MES completo | — |

**Lettura competitiva.** Bravo = OEE + IoT add-on + ERP esterno, 290 €/risorsa/anno confermato. siMES = capacità finita + raccolta dati macchina + AI dichiarata, **confermato a ~1.100 €/anno base** — molto più accessibile di quanto stimato in precedenza, non un prodotto di fascia alta. Opera (Cybertec, gruppo Zucchetti) / NET@PRO = modularità produzione, prezzo solo su preventivo. Mago.Net, TeamSystem, Zucchetti = ERP con la produzione come modulo, fiscale forte ma shop-floor poco profondo. **Nessun competitor citato unisce, in un solo prodotto per la PMI, FatturaPA nativa + DDT + MES di fabbrica + moduli di settore** come Nicolò: il confronto tipico resta "MES + ERP già in casa", non "un solo prodotto". Con Bravo e siMES ora a prezzi bassi e pubblici, il prezzo di Nicolò MES è l'unico dato mancante per completare il confronto — e più urgente di prima, perché i due concorrenti più vicini sul fiscale/produzione costano poco.

---

## 3. Cosa chiede il mercato nel 2026

1. Interconnessione vera (OPC UA, MQTT, Modbus…) e prove per la perizia.
2. Energia / misure utili alla documentazione agevolabile.
3. Schedulazione a capacità finita (in prospettiva, copilota AI con umano in loop — Gartner MES Guide 2026).
4. Istruzioni digitali alla postazione.
5. Cloud **e** on-premise; mobile.
6. Time-to-value 2–4 mesi per una PMI con ~20 macchine (la licenza è spesso la parte minore del costo totale).
7. Sicurezza (2FA, audit log; NIS2 per i clienti più strutturati).
8. Un solo posto dove chiudere il ciclo ordine → produzione → DDT → fattura, soprattutto sotto le 50–100 persone.

---

## 4. Confronto funzione per funzione

Legenda: ✅ c'è e pubblicato · 🟡 parziale o solo su un canale · ❌ manca

| Area | Nicolò MES (07/10/2026) | MES PMI IT | MRP cloud | ERP IT | Enterprise |
|---|---|---|---|---|---|
| Distinte, cicli, commesse, fasi | ✅ | ✅ | ✅ | 🟡 | ✅ |
| Terminale reparto PIN (+ offline), filtro "mio reparto" | ✅ | ✅ | ❌ | ❌ | ✅ |
| Tracciabilità lotti / richiamo / matricole | ✅ | 🟡 | 🟡 | ❌ | ✅ |
| OEE (fasi + macchina se collegata) | ✅ | ✅ da macchina | ❌ | ❌ | ✅ |
| Gateway OPC UA/MQTT | ✅ predisposto; **macchina reale in campo ❌** | ✅ reale | ❌ | ❌ | ✅ |
| Energia / progetti perizia | ✅ software; contatore reale ❌ | 🟡 | ❌ | ❌ | ✅ |
| Capacità finita, MRP multi-livello + crea PO, ubicazioni/inventario | ✅ | 🟡 via ERP | ✅ parziale | 🟡 | ✅ |
| Ufficio tecnico, FAT/SAT + fascicolo + CE/UE, service post-vendita | ✅ | 🟡 | ❌ | 🟡 | ✅ |
| Qualità (NC, piani), taratura strumenti, CAPA, manutenzione | ✅ | ✅/🟡 | ❌/🟡 | 🟡 | ✅ |
| Clienti / preventivi / fatture (creazione anche da web) | ✅ (08/10) | ❌ (ERP) | ✅ | ✅ | 🟡 |
| DDT / conto lavoro | ✅ | ❌ | 🟡 | 🟡 | 🟡 |
| FatturaPA XML validata (creazione solo da programma) | ✅ 🟡 web | ❌ (ERP) | ❌ | ✅ | ❌ |
| Invio SdI | 🟡 provider HTTP pronto, **contratto reale ❌** | ❌ | ❌ | ✅ (spesso) | ❌ |
| Passive + scadenziario, margini di commessa | ✅ | ❌/🟡 | 🟡 | ✅/🟡 | ❌/✅ |
| Web + telefono | ✅ in crescita (12 schermate col layout personalizzabile) | ✅ | ✅ | ✅ | ✅ |
| Cloud o server cliente | ✅ entrambi | ✅ | solo cloud | ✅ | ✅ |
| 2FA / SSO aziendale | ✅ 2FA; SSO plumbing ok, **IdP di produzione ❌** | 🟡 | ✅ | 🟡 | ✅ |
| Registro operazioni (audit) | ✅ **50 controller su 52** (08/10) | 🟡 | ✅ | 🟡 | ✅ |
| Policy GDPR (retention, cancellazione) | ❌ | 🟡 | 🟡 | 🟡 | ✅ |
| Assistente IA / multilingua | 🟡 IA stub; multilingua ❌ | 🟡 | 🟡 | 🟡 | ✅ |
| Pack settore (EPLAN, DM37, SAL, 3.1…) | ✅ base | 🟡 | ❌ | 🟡 | 🟡 |

---

## 5. Settore per settore (vs verticali)

- **Quadri elettrici.** Abbiamo 61439, Metel, EPLAN→BOM, lista cavi, DDT/fattura, costi. Resta, rispetto agli specialisti: etichette cavi/morsetti dedicate, allegati legati alla dichiarazione in un click.
- **Meccanica / carpenteria.** Abbiamo cicli, conto lavoro, OEE, manutenzione, cert. 3.1 su lotto, taratura strumenti. Resta: DNC diretto, nesting, preventivo meccanico "avanzato".
- **Costruzione macchine.** Abbiamo UT, FAT/SAT, fascicolo, CE/UE, service, energia. Resta: PDF rapporto collaudo autonomo, firma qualificata, QR matricola → storico completo.
- **Impiantistica.** Abbiamo rapportini firmati, Metel, DM 37/08 generate, SAL, calendario squadre (base). Resta: computo metrico pieno, Angaisa, magazzino furgone.
- **Alimentare.** Abbiamo FEFO, allergeni, SSCC, richiamo, HACCP, nutrizionale, bilance (letture software). Resta: bilance hardware, IFS/BRC, ricetta con resa/cali.
- **Manifattura generica.** Nucleo MES + MRP + ubicazioni + capacità finita + commerciale IT. I gap sentiti restano gli stessi di sempre: campo macchine, intermediario SdI, referenze.

---

## 6. Pro e contro di Nicolò MES (verificati sul repository, 07/10/2026)

### Pro

1. **Tutto in uno italiano.** Dal preventivo (anche da web) alla FatturaPA (XML validato), con DDT e conto lavoro: niente "MES + ERP" obbligatorio.
2. **Shop-floor vero.** Terminale PIN (anche offline), fasi, fermi, NC, OEE, matricole, istruzioni/disegni alla postazione.
3. **Settori nativi.** 61439, HACCP/allergeni/SSCC, rapportini firmati, FAT/SAT/CE, service, Metel, pack EPLAN/DM37/SAL/3.1 — non add-on generici.
4. **Acquisti e magazzino.** MRP multi-livello con proposte e creazione ordini; ubicazioni e inventario; lotti e FEFO.
5. **Pianificazione.** Board settimanale (anche web) + capacità finita sui centri di lavoro.
6. **Strumento Layout.** Unico tra i concorrenti citati: l'Admin personalizza etichette, ordine, visibilità e obbligatorietà dei campi di 12 schermate (in crescita) senza toccare il codice, e può delegare il permesso ad altri ruoli.
7. **Canali multipli.** Desktop Windows, web `/app`, `/tecnici` da telefono; temi grafici aziendali; cloud o server cliente; 2FA; Swagger; auto-update.
8. **Agevolazioni 2026–2028.** Gateway di interconnessione + energia + dichiarazione origine software già pronti per la narrazione di perizia.
9. **Costo di ingresso potenzialmente competitivo** rispetto a Bravo Plus (se il prezzo citato fosse confermato) o a progetti enterprise, se il modello commerciale resta snello — ma serve definirlo.
10. **Profondità tecnica dimostrabile.** Suite test **686/686** (08/10/2026), CI, migrazioni, registro operazioni (**50 controller su 52**, essenzialmente completo — i 2 rimasti sono di sola lettura automatica), header di sicurezza estesi, health check standard.

### Contro

1. **Nessuna referenza né prova sul campo.** Nessuna macchina reale collegata, nessun cliente pubblico citabile. È il rischio commerciale più alto, più di qualsiasi funzione mancante.
2. **Prezzo non definito.** Senza un listino, anche solo indicativo, il confronto con Bravo o MRPeasy si ferma prima di cominciare.
3. **Macchina fisica in officina.** Demo-feed e simulatore Python chiudono la demo commerciale; per la perizia e per vincere su Bravo/siMES serve un collegamento reale.
4. ~~**Fattura dal web non ancora possibile.**~~ Risolto l'08/10/2026: la fattura si crea ora anche dalla piattaforma web, come il preventivo.
5. ~~**Registro operazioni parziale.**~~ Risolto il 07-08/10/2026: ora 50 controller su 52 scrivono traccia; i 2 rimasti (health check automatico, pianificazione aggregata) non hanno nulla da registrare.
6. **Nessuna policy GDPR** (conservazione, cancellazione su richiesta) — solo un riferimento di settore individuato (§8.6), non ancora una policy propria.
7. **SSO incompleto.** Plumbing e login esterno pronti; manca il mapper OIDC di produzione verso un IdP aziendale reale.
8. **Single-tenant, Windows-first.** Un'installazione per azienda; l'app nativa è solo Windows, il web copre una parte crescente ma non tutta.
9. **Validazione input manuale** endpoint per endpoint; **API non versionata**; **Swagger pubblico** in produzione da confermare col titolare.
10. **Import PDF cataloghi** non ancora validato su un catalogo fornitore reale.
11. **Dipendenza dal titolare** per contratto SdI, hosting a pagamento, firma digitale del software, referenze.
12. **Verticali da specialista** (DNC, computo, IFS) non profondi come i concorrenti dedicati a un solo settore.

---

## 7. Verdetto competitivo (una frase per fascia)

| Contro chi | Verdetto |
|---|---|
| MES PMI (Bravo, siMES, Opera…) | **Vince** su commerciale/fiscale IT e settori; **perde** su IoT plug-and-play e brand di campo finché non c'è una macchina pilota. |
| ERP italiani (TeamSystem, Zucchetti, Mago.Net) | **Vince** su produzione e shop-floor; **perde** su contabilità completa e rete commerciale. |
| MRP cloud (Katana, MRPeasy, Odoo) | **Vince** su Italia (DDT/FatturaPA/HACCP/61439); **perde** su UX cloud-native e internazionalizzazione. |
| Enterprise | Non compete sullo stesso budget; può essere **complementare** solo se il cliente non vuole un progetto da 6–18 mesi. |

**Posizione consigliata in vendita:** *"Il gestionale di fabbrica italiano che fa anche fattura e DDT — non un MES che ti chiede di tenere l'ERP a parte."*
**Condizione per non perdere la trattativa tecnica:** una demo con macchina reale + un prezzo pubblico, anche indicativo + l'intermediario SdI (anche solo su un cliente pilota).

---

## 8. Cosa possiamo migliorare — priorità

Ordinate per impatto commerciale/competitivo, non per facilità. I primi tre non si risolvono scrivendo codice: sono il vero collo di bottiglia rispetto ai concorrenti PMI.

| # | Punto | Impatto | Perché conta nel confronto | Chi | Prossimo passo concreto |
|---|---|---|---|---|---|
| 1 | Pilota con macchina reale e prima referenza | Molto alto | È l'unico vantaggio vero di Bravo e siMES: hanno un retrofit macchina dimostrabile, noi oggi solo un simulatore | Titolare / campo | Scegliere un reparto con 1–2 macchine già dotate di PLC o contatore accessibile; collegare il gateway OPC UA/MQTT già pronto in `scripts/machine-gateway` |
| 2 | Prezzo pubblico e modello commerciale | Alto | Bravo (290 €/risorsa/anno) e siMES (~1.100 €/anno base) hanno entrambi un prezzo **confermato e basso** (08/10): senza un numero nostro, il cliente li sceglie prima ancora di guardare le funzioni | Titolare | Un listino a fascia (per numero di utenti o di macchine), usando questi due come riferimento di fascia bassa — non un listino enterprise |
| 3 | Contratto con un intermediario SdI | Alto | Chiude il ciclo fiscale: oggi il provider HTTP è pronto in codice (`Sdi:Provider=http`), manca solo chi lo eroga | Titolare | Scegliere un provider (Aruba o simili), ottenere URL e chiavi, configurarlo su Render |
| 4 | ~~Creazione fattura dalla piattaforma web~~ | — | — | — | **Fatto l'08/10/2026**: `/fatture` ha ora "Nuova fattura" sul web, come i preventivi |
| 5 | ~~Registro operazioni estesa~~ | — | — | — | **Fatto il 07-08/10/2026**: 50 controller su 52, essenzialmente completo |
| 6 | Policy GDPR (conservazione, cancellazione su richiesta) | Medio | Il Codice di condotta Assosoftware (in vigore da novembre 2024) non fissa tempi di conservazione precisi — per principio (art. 5.1.e GDPR) la durata dipende dalla finalità, quindi va decisa caso per caso — ma dà una struttura condivisa di settore | Titolare + legale/DPO, poi codice | Scrivere la policy con un legale usando il codice di condotta come riferimento, poi implementare cancellazione/anonimizzazione su richiesta |
| 7 | Mapper OIDC di produzione per SSO aziendale | Medio | Oggi utile solo a chi ha già un IdP (Azure AD, Google Workspace…); senza il mapper resta solo demo | Titolare / configurazione | Attivare solo dietro un IdP realmente fidato da un cliente che lo richieda |
| 8 | Validazione input sistematica e versionamento API (`/api/v1/`) | Medio ma esteso | Oggi la validazione è manuale endpoint per endpoint; tocca gran parte dei controller | Codice | Pianificare a parte (è lo step 7 della mappa di hardening in STATO-PROGETTO.md), non improvvisare in mezzo ad altro lavoro |
| 9 | Decisione su Swagger pubblico in produzione | Basso ma aperta | Scelta deliberata del 02/10 per dare documentazione a chi integra; resta da confermare o rivedere | Titolare | Una conferma esplicita chiude il punto |
| 10 | Import PDF cataloghi su un catalogo fornitore reale | Basso | Il codice gestisce ff/fi/fl nei codici; manca ancora una prova su un PDF vero di un fornitore | Campo | Importare il primo catalogo reale appena disponibile |
| 11 | Strumento Layout sulle restanti schermate | Basso-medio, consistenza di prodotto | 12 schermate coperte oggi; la maggior parte delle ~70 pagine web e ~45 finestre desktop non lo sono ancora | Codice | Continuare per gruppi (il punto 4, fattura web, è anche un passo di questo lavoro) |

---

## Fonti

### Mercato e normativa
- [AlixPartners–Qualitas — MES Italia 120 M€ al 2027](https://www.alixpartners.com/newsroom/mercato-dei-mes-in-forte-crescita-in-italia-studio-alixpartners-e-qualitas/)
- [Industria Italiana — evoluzione MES](https://www.industriaitaliana.it/manufacturing-execution-systems-alixpartners-digitalizzazione-manifattura/)
- [RevenueBase — elenco aziende MES in Italia, dati settembre 2026](https://revenuebase.ai/companies/mes-software-companies/italy)
- [Gartner MES Guide 2026 — lettura Siemens](https://blogs.sw.siemens.com/opcenter/2026-gartner-market-guide-for-manufacturing-execution-systems-how-ai-is-shaping-mes-and-where-we-believe-opcenter-adds-customer-value/)
- [MIMIT — Transizione 5.0 / iperammortamento](https://www.mimit.gov.it/it/incentivi/nuovo-piano-transizione-5-0-iperammortamento)
- [Leyton — Iperammortamento 2026, la ripartenza degli incentivi](https://leyton.com/it/insights/articles/iperammortamento-2026-la-ripartenza-degli-incentivi-per-la-trasformazione-industriale/)
- [QualEnergia — impegno del governo su Transizione 5.0](https://www.qualenergia.it/pro/articoli-pro/transizione-5-impegno-governo-dare-continuita/)

### GDPR e codice di condotta
- [Cybersecurity360 — il codice di condotta per i software gestionali](https://www.cybersecurity360.it/news/software-gestionali-ce-il-codice-di-condotta-che-fissa-regole-e-limiti-per-il-trattamento-dati/)
- [Agenda Digitale — l'organismo di monitoraggio del codice](https://agendadigitale.eu/sicurezza/privacy/codice-di-condotta-software-al-via-lorganismo-di-monitoraggio)
- [InformazioneFiscale — software gestionale e GDPR, il ruolo del codice di condotta](https://www.informazionefiscale.it/software-gestionale-gdpr-codice-di-condotta)
- [Testo del codice di condotta (PDF)](https://lentepubblica.it/wp-content/uploads/2024/12/Codice-di-condotta-per-il-trattamento-dei-dati-personali-effettuato-dalle-imprese-di-sviluppo-e-produzione-di-software-gestionale.pdf)

### Concorrenti
- [Bravo — confronta prezzi](https://www.bravomanufacturing.it/confronta-prezzi/) — **verificato 08/10**: Bravo Plus 290 €/risorsa/anno, di Antos S.r.l. (Camerano, AN)
- [siMES — Sivaf Informatica](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/) — **verificato e corretto 08/10**: ~1.100 €/anno base (non ~3.500 €/macchina come scritto prima), Sivaf Informatica (Stezzano, BG)
- [Antos — caso d'uso Alleantia](https://www.alleantia.com/resources/use-cases/antos/)
- [Opera MES (Cybertec, gruppo Zucchetti)](https://www.operames.it/opera-mes/) — **verificato 08/10**: nessun prezzo pubblico
- [NET@PRO](https://www.erpselection.it/software/software-gestione-produzione-mes-manufacturing-netpro/)
- [TeamSystem — guida MES 2026](https://www.teamsystem.com/magazine/manufacturing/mes-software-smart-factory-guida-2026/)
- [Microarea Mago.Net — ricerca 01net](https://www.01net.it/?p=91629)
- [Katana — prezzi 2026](https://costbench.com/software/inventory-management/katana-mrp/)
- [MRPeasy — prezzi 2026](https://erpresearch.com/pricing/mrpeasy)
- [Odoo Italia — prezzi EUR](https://oec.sh/odoo-pricing/italy)
- [Tulip — prezzi](https://toolradar.com/tools/tulip/pricing)
- [Siemens — pagina MES](https://www.siemens.com/it-it/solutions/manufacturing-execution-system-mes/)

Stato prodotto: [STATO-PROGETTO.md](STATO-PROGETTO.md) · Gate: [GATES.md](GATES.md) · Handoff: [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md) · Dettaglio competitor: [ANALISI-COMPETITOR-MES.md](ANALISI-COMPETITOR-MES.md).
