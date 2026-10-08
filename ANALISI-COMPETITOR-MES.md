# Analisi competitor e punti da migliorare — Nicolò MES

Aggiornata al **8 ottobre 2026** (verifica diretta delle fonti Bravo/siMES/Opera, prima segnalate come non confermate). Fonti di mercato: [ANALISI-MERCATO-MES.md](ANALISI-MERCATO-MES.md) e le ricerche indicate sotto. Lo stato interno è in [STATO-PROGETTO.md](STATO-PROGETTO.md).

## 1. Ricerca: competitor verificati

| Prodotto | Prezzo pubblicato | Forza | Limite rispetto a Nicolò MES |
|---|---|---|---|
| **Bravo** ([prezzi](https://www.bravomanufacturing.it/confronta-prezzi/)) — Antos S.r.l., Camerano (AN) | **Confermato 08/10**: Bravo Plus **290 €/risorsa/anno** (costi di produzione, operatori/macchine, ciclo/distinta, conto lavoro, pianificazione, ricette/disegni); Bravo Pro aggiunge calendari di stabilimento, lotti, macchine non presidiate, lavorazioni combinate, report designer — prezzo Pro non pubblicato. "Risorsa" = operatore o macchina; il minimo di 10 risorse non è confermato in questa verifica | OEE, tempi di fermo, avanzamento; IoT Connector; integrazione con SAP, Mago, TeamSystem, Zucchetti, Passpartout | Il fiscale e la fattura restano nell'ERP esterno |
| **siMES** ([sito](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/)) — di **Sivaf Informatica**, Stezzano (BG) | **Confermato e corretto 08/10**: non ~3.500 €/macchina come scritto prima (dato mai verificato, probabilmente un errore o una confusione con un altro prodotto), ma **~1.100 €/anno** per il modulo produzione base (1 postazione, 1 operatore, 1 licenza utente); moduli aggiuntivi 50–500 €/anno. On-premise, si integra con siERP o un ERP terzo | Schedulazione a capacità finita, raccolta dati macchina senza reinserimento, qualità con alert automatici, programmi CNC (siPartPRG), OEE | Prezzo per postazione/modulo che sale in fretta con le dimensioni; il commerciale e la fattura sono fuori |
| **NET@PRO** ([scheda](https://www.erpselection.it/software/software-gestione-produzione-mes-manufacturing-netpro/)) | Su preventivo | Piattaforma web unica: MES, tracciabilità, qualità, manutenzione, magazzino, pianificazione | Posizionamento enterprise; prezzo non pubblico |
| **Opera MES** ([sito](https://www.operames.it/opera-mes/)) — **Cybertec S.r.l., gruppo Zucchetti** | **Confermato 08/10**: nessun prezzo pubblico sul sito, serve contattare l'azienda | Piattaforma unica "Smart Factory": produzione, materiali, qualità, manutenzione, energia, AI/ML per manutenzione predittiva | Essendo del gruppo Zucchetti, probabilmente si integra (o sovrappone) con l'offerta Zucchetti sotto — da capire se è lo stesso canale commerciale |
| **TeamSystem / Zucchetti** ([confronto](https://www.guidasoftware.it/teamsystem-vs-zucchetti-pmi/)) | Canoni per utente; modulo produzione a parte (circa 2.500 €/anno per TeamSystem) | Fiscale, contabilità, fattura elettronica SDI, forte presenza PMI | La produzione è un modulo aggiuntivo, con meno profondità sul reparto |

**Lettura aggiornata.** Con i prezzi ora confermati, il quadro cambia rispetto a prima: **siMES a 1.100 €/anno base è molto più accessibile di quanto pensassimo** (non un prodotto di fascia alta "per macchina" come scritto in precedenza) — è un concorrente diretto sul prezzo d'ingresso, non solo sulla tecnologia di retrofit. Bravo resta confermato a 290 €/risorsa/anno. Nessun competitor citato unisce, in un solo prodotto per la PMI, fattura elettronica nativa, DDT, MES di reparto e moduli di settore — ma né Bravo né siMES sono "costosi": il vantaggio di prezzo di Nicolò MES, se dovesse averne uno, va dimostrato contro cifre reali e basse, non contro un listino enterprise immaginato.

## 2. Pro e contro

### Nicolò MES
**Pro**
- Un solo prodotto: produzione, commerciale, documenti italiani (DDT, FatturaPA, SDI), acquisti, magazzino, qualità.
- Moduli di settore già pronti: collaudo e CE, service, manutenzione, monitoraggio energetico, ufficio tecnico, HACCP.
- Tre canali (desktop, web, console fornitore) su una sola API, con 2FA e controllo degli accessi per canale e ruolo.
- Strumento Layout: l'Admin adatta etichette, ordine e obbligatorietà dei campi, e autorizza i ruoli a modificarli.

**Contro**
- Nessuna connessione reale alla macchina in officina: i dati di produzione li inserisce l'operatore. Bravo e siMES hanno già il retrofit.
- Nessun riferimento clienti e nessun caso documentato, al momento.
- Strumento Layout coperto su 12 schermate di circa 70 (08/10/2026; cresce di continuo).
- Verticali (DNC, computo, IFS) non profondi come gli specialisti.
- Contabilità fino a fattura e scadenziario, non prima nota.

### Competitor (sintesi)
- **Bravo:** pro su OEE e integrazione con gli ERP italiani; contro sul fiscale e sull'ERP da tenere in parallelo.
- **siMES:** pro su schedulazione a capacità finita e raccolta dati macchina a prezzo basso (1.100 €/anno base); contro sul commerciale assente e sulla scalabilità del prezzo per postazione/modulo.
- **NET@PRO:** pro su copertura funzionale ampia; contro su posizionamento enterprise e prezzo non pubblico.
- **TeamSystem / Zucchetti:** pro su fiscale e presenza; contro sulla produzione come modulo a parte.

## 3. Punti da migliorare (in ordine di impatto)

1. **Connessione alla macchina:** un connettore OPC UA o Modbus di base per conteggi e fermi. È il punto dove Bravo e siMES sono avanti.
2. **Strumento Layout su tutte le schermate:** oggi 9 schermate su circa 70. Preventivi e fatture sono i prossimi.
3. **Preventivo:** la finestra `QuoteEditorWindow` non ha ancora il layout. Va collegata come gli ordini, con intestazioni di colonna per le righe.
4. **Riferimenti clienti:** almeno due casi documentati prima di proporre il prodotto come alternativa a Bravo o siMES.
5. **Prezzo pubblico chiaro:** un listino per fascia di dimensione. Ora che Bravo (290 €/risorsa/anno) e siMES (1.100 €/anno base) hanno un prezzo reale e basso, il confronto è più urgente, non meno: senza un numero nostro, un cliente PMI li sceglie prima ancora di guardare le funzioni.
6. ~~**Backup notturno**~~ — risolto il 07/10/2026 (segreto `NEON_DATABASE_URL` impostato, workflow verificato con un'esecuzione riuscita).

## 4. Stato di questa analisi

- Ricerca fatta su quattro gruppi di competitor. **Aggiornamento 08/10/2026**: Bravo, siMES e Opera MES sono stati verificati direttamente sui siti ufficiali — il prezzo di siMES era sbagliato nella versione precedente di questo documento (scritto ~3.500 €/macchina, in realtà ~1.100 €/anno). Mancano ancora i listini di NET@PRO e Opera MES, non pubblici: vanno chiesti in trattativa.
- Il punto 1 (connettore macchina) e il punto 4 (riferimenti) non si risolvono nel codice: richiedono lavoro fuori dal repository.
- La fattura dalla piattaforma web, segnalata come gap nella versione precedente di [ANALISI-MERCATO-MES.md](ANALISI-MERCATO-MES.md), è stata completata l'08/10/2026.
