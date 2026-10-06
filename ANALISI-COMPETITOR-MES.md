# Analisi competitor e punti da migliorare — Nicolò MES

Aggiornata al **6 ottobre 2026**. Fonti di mercato: [ANALISI-MERCATO-MES.md](ANALISI-MERCATO-MES.md) e le ricerche indicate sotto. Lo stato interno è in [STATO-PROGETTO.md](STATO-PROGETTO.md).

## 1. Ricerca: competitor verificati

| Prodotto | Prezzo pubblicato | Forza | Limite rispetto a Nicolò MES |
|---|---|---|---|
| **Bravo** ([prezzi](https://www.bravomanufacturing.it/confronta-prezzi/)) | Bravo Plus da circa 290 €/risorsa/anno; Bravo Pro; versione Free | OEE, tempi di fermo, avanzamento; IoT Connector; integrazione con SAP, Mago, TeamSystem, Zucchetti, Passpartout | Il fiscale e la fattura restano nell'ERP esterno |
| **siMES, SiVaF** ([sito](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/)) | Da circa 3.500 € per macchina, certificato Industria 4.0 | Retrofit su macchine senza PLC; enfasi su AI e manutenzione predittiva | Prezzo per macchina; il commerciale e la fattura sono fuori |
| **NET@PRO** ([scheda](https://www.erpselection.it/software/software-gestione-produzione-mes-manufacturing-netpro/)) | Su preventivo | Piattaforma web unica: MES, tracciabilità, qualità, manutenzione, magazzino, pianificazione | Posizionamento enterprise; prezzo non pubblico |
| **Opera MES** ([scheda](https://www.linkmanagement.it/software-programmazione-controllo-produzione/software-opera-mes/)) | Su preventivo | Monitoraggio e controllo avanzamento produzione | Ambito produzione, senza commerciale |
| **TeamSystem / Zucchetti** ([confronto](https://www.guidasoftware.it/teamsystem-vs-zucchetti-pmi/)) | Canoni per utente; modulo produzione a parte (circa 2.500 €/anno per TeamSystem) | Fiscale, contabilità, fattura elettronica SDI, forte presenza PMI | La produzione è un modulo aggiuntivo, con meno profondità sul reparto |

**Lettura.** Nessun competitor citato unisce, in un solo prodotto per la PMI: fattura elettronica nativa, DDT, MES di reparto e moduli di settore. I più vicini (Bravo, siMES) vincono sulla macchina, ma restano dipendenti da un ERP esterno per il fiscale.

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
- Strumento Layout coperto solo su 8 schermate di circa 70.
- Verticali (DNC, computo, IFS) non profondi come gli specialisti.
- Contabilità fino a fattura e scadenziario, non prima nota.

### Competitor (sintesi)
- **Bravo:** pro su OEE e integrazione con gli ERP italiani; contro sul fiscale e sull'ERP da tenere in parallelo.
- **siMES:** pro su retrofit e AI; contro sul prezzo per macchina e sul commerciale assente.
- **NET@PRO:** pro su copertura funzionale ampia; contro su posizionamento enterprise e prezzo non pubblico.
- **TeamSystem / Zucchetti:** pro su fiscale e presenza; contro sulla produzione come modulo a parte.

## 3. Punti da migliorare (in ordine di impatto)

1. **Connessione alla macchina:** un connettore OPC UA o Modbus di base per conteggi e fermi. È il punto dove Bravo e siMES sono avanti.
2. **Strumento Layout su tutte le schermate:** oggi 9 schermate su circa 70. Preventivi e fatture sono i prossimi.
3. **Preventivo:** la finestra `QuoteEditorWindow` non ha ancora il layout. Va collegata come gli ordini, con intestazioni di colonna per le righe.
4. **Riferimenti clienti:** almeno due casi documentati prima di proporre il prodotto come alternativa a Bravo o siMES.
5. **Prezzo pubblico chiaro:** un listino per fascia di dimensione, perché il cliente lo confronta subito con Bravo.
6. **Backup notturno:** il segreto `NEON_DATABASE_URL` va verificato su GitHub; il job oggi può fallire senza avviso.

## 4. Stato di questa analisi

- Ricerca fatta su quattro gruppi di competitor, con fonti pubbliche. Mancano listini di NET@PRO, Opera e siMES su preventivo: vanno chiesti in trattativa.
- Il punto 1 (connettore macchina) e il punto 4 (riferimenti) non si risolvono nel codice: richiedono lavoro fuori dal repository.
