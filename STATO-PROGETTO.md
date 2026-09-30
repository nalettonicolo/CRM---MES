# Stato del progetto: Nicolò MES (CrmMes)

Aggiornato: 30 settembre 2026. Questo file si aggiorna a ogni sessione di lavoro: cosa c'è, cosa manca, cosa è stato fatto. Per il diario tecnico dettagliato vedi [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md); per la mappa dei file [PROJECT-MAP.md](PROJECT-MAP.md); per il ripristino dei dati [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md).

## In sintesi

- **Cos'è**: gestionale di produzione (MES) con parte commerciale e documentale, per piccole e medie aziende manifatturiere. Nato per un quadrista, oggi si configura per cinque settori.
- **Versione pubblicata**: v1.4.0 (30/09/2026), con installer per Windows e aggiornamento automatico.
- **Architettura**: client Windows (WPF, .NET 8) + API web (ASP.NET Core 8) su Render + database Postgres su Neon. Una pagina web per i tecnici è servita dalla stessa API.
- **Test automatici**: 258 sull'API e 111 sul client, tutti verdi all'ultima misura (30/09/2026). Le fatture elettroniche sono validate contro lo schema ufficiale FatturaPA.
- **Uso attuale**: interno, un'azienda con due sedi.

## Cosa c'è

### Configurazione e settori
- Configurazione azienda al primo accesso dell'Admin: dati azienda (anche per DDT, dichiarazioni, etichette), prefisso GS1, settore, moduli.
- Cinque settori: Quadri elettrici e automazione, Meccanica e carpenteria, Alimentare, Impiantistica e installazioni, Manifattura generica. Il settore accende i moduli tipici; ogni modulo si attiva o si spegne a mano. Spegnere un modulo nasconde menu e pulsanti, non cancella dati.

### Vendite e direzione
- Clienti, preventivi (bozza, inviato, accettato, rifiutato), stima prezzo da distinta, PDF, conversione in commesse.
- **Fattura elettronica** (FatturaPA): differita dai DDT con i prezzi venduti, o immediata; IVA per riga con nature e reverse charge edile; numerazione annuale; file XML validato contro lo schema ufficiale, da caricare sul portale dell'Agenzia delle Entrate o dare al commercialista; copia di cortesia PDF; dati fiscali di azienda e clienti.
- Costo di commessa stimato e reale (materiali al prezzo d'acquisto del lotto, fasi, ore registrate, materiali di cantiere), margine; area "Controllo margini" visibile solo ad Admin e Management, negata dall'API agli altri ruoli.

### Magazzino e acquisti
- Materiali, lotti con tracciabilità e scadenza, prelievo "prima scade, prima esce" (poi ordine di arrivo), sottoscorta, materiali mancanti.
- Distinte di prelievo, fornitori, ordini fornitore con ricevimento anche parziale, cataloghi fornitore da CSV, Excel e PDF, ricerca catalogo.
- Conto lavoro: invio al terzista con DDT, rientri parziali e scarti, elenco di cosa è fuori con i ritardi.

### Produzione
- Prodotti con distinta base e ciclo; commesse con fasi, unità e matricole tracciate una per una.
- Terminale di reparto con PIN operatore e coda offline; ricerca per ultime cifre del codice o per lotto; per Admin, direzione e magazzino l'elenco delle commesse aperte e in lavorazione da toccare; fermi macchina con causale; non conformità; OEE completo.
- Scheda Commesse con filtro "Aperte e in lavorazione" e ricerca; codici commessa e lotto copiabili (pulsante Copia, tasto destro, Ctrl+C).
- Centri di lavoro con capacità e tariffa oraria; pianificazione a calendario; board settimanale delle scadenze; planning produzione dipinto a mano.
- Qualità: piani di controllo, misure, certificato di conformità.
- Manutenzione: macchine e interventi preventivi ricorrenti o correttivi.

### Documenti di legge e logistica
- DDT con nove causali, numerazione progressiva annuale all'emissione, annullamento con motivo (il numero resta), PDF con firme; bozza precompilata dalla commessa.
- Esportazione Excel delle righe DDT del periodo per la fatturazione nel gestionale contabile.
- Corrieri e spedizioni in ingresso e uscita.

### Moduli di settore
- **Quadri elettrici**: verifica individuale CEI EN 61439 (nove verifiche dell'art. 11, dati di targa), dichiarazione di conformità e rapporto in PDF.
- **Alimentare**:
  - scadenze dei lotti;
  - richiamo di lotto guidato (anche da lotto prodotto) con rapporto PDF;
  - allergeni UE ed etichetta del lotto (ingredienti in ordine di quantità, allergeni in grassetto, data di scadenza);
  - pallet SSCC con etichetta GS1-128;
  - registri HACCP con azioni correttive obbligatorie ed esportazione per le ispezioni.
- **Impiantistica**: rapportini di cantiere con ore, materiali e firma del cliente. Firmati, entrano nei costi della commessa. PDF con la firma.

### Web e mobile
- Pagina `/tecnici/` per telefono e tablet (stesse credenziali del gestionale):
  - rapportini con firma col dito;
  - avvio e completamento delle fasi;
  - registrazione ore.
- La sessione si rinnova da sola e il lavoro non salvato resta sul dispositivo. Tutto pubblicato con la v1.4.0.

### Sicurezza e affidabilità
- Accesso con JWT e token di rinnovo a rotazione; sei ruoli (Admin, Management, Warehouse, Purchasing, Sales, Operator).
- Blocco account dopo 5 password errate, limiti di richieste su login e PIN, PIN univoci, cambio e reimpostazione password.
- Registro delle operazioni (audit).
- Backup notturno del database con verifica del ripristino, procedura di disaster recovery provata.
- Installer per utente senza permessi di amministratore; aggiornamento automatico verificato con checksum SHA-256.
- Pipeline GitHub: test a ogni push, release a ogni tag, firma del codice pronta ma inattiva.
- Server sempre sveglio: il server chiama se stesso ogni 4 minuti; nelle ore di lavoro tiene sveglio anche il database; GitHub lo riaccende ogni 5 minuti. Il client riprova per un minuto se trova il server in avvio.

## Cosa manca

| Punto | Stato | Cosa serve |
|---|---|---|
| Invio automatico delle fatture allo SdI | Da decidere | Intermediario accreditato a pagamento; oggi il file si carica a mano sul portale gratuito |
| Hosting a pagamento (garanzia di nessuna sospensione) | Da acquistare | Tuo acquisto del piano su Render: istruzioni in RIEPILOGO-SVILUPPO.md, "Hosting e firma" |
| Firma digitale dell'eseguibile (niente avviso di Windows) | Da acquistare | Certificato di firma del codice: istruzioni in RIEPILOGO-SVILUPPO.md |
| Interconnessione macchine (Transizione 5.0) | Da fare (prossimo) | Ingresso dati macchina con token; per il collegamento OPC UA o MQTT serve l'elenco delle macchine |
| Listini Metel | In attesa | Un file Metel reale di un produttore |
| Alimentare: GTIN sui prodotti, piano HACCP guidato per tipologia | Da fare | Un'azienda pilota del settore |
| Scarico di magazzino da DDT e rapportini | Scelta aperta | Oggi DDT e rapportini registrano cosa esce ma non scaricano la giacenza |
| Import catalogo PDF validato su cataloghi reali | Da verificare | Il primo catalogo reale disponibile |
| Log centralizzati su Grafana | Pronto, non attivo | Account gratuito Grafana e variabili su Render |
| Password dell'account di sviluppo esposto in passato | Da fare | Va cambiata dal titolare |

## Cosa è stato fatto (cronologia)

| Data | Versione | Lavoro |
|---|---|---|
| fino a set. 2026 | v1.0.0 | Magazzino, acquisti, produzione con fasi, lotti, OEE, terminale di reparto con PIN e coda offline, qualità, manutenzione, spedizioni, sedi, pianificazione, planning produzione, backup, logging |
| 28-29/09/2026 | v1.1.0 | Grafica globale rinnovata, numeri e date in formato italiano, backup notturno riparato |
| 29/09/2026 | v1.2.0 | Clienti e preventivi; costi, ore e margini con area direzione; verifica di sicurezza e correzioni; installer e aggiornamento affidabile; tabelle a tutta larghezza |
| 29-30/09/2026 | v1.3.0 | Configurazione multi-settore; DDT, conto lavoro, esportazione per la contabilità; verifica CEI EN 61439; alimentare completo (scadenze, richiamo, allergeni ed etichette, SSCC, HACCP); rapportini di cantiere con pagina web per i tecnici |
| 30/09/2026 | v1.4.0 | Fattura elettronica FatturaPA; terminale con ricerca per ultime cifre ed elenco commesse aperte; filtro commesse; codici copiabili; server sempre sveglio e login più rapido; pagina web con Fasi e Ore; istruzioni per hosting e firma |
| prossimo | - | Ingresso dati macchine (Transizione 5.0); importatore Metel configurabile |

## Pubblicazione e ambienti

- **API**: https://crmmes-api.onrender.com (piano gratuito Render), deploy automatico a ogni push su `main`.
- **Database**: Neon Postgres, progetto `cool-field-94626300`. Tutte le migrazioni fino a `AddElectronicInvoices` sono applicate in produzione il 30/09/2026, dopo prova su un branch dedicato.
- **Client**: release GitHub con `NicoloMES-Setup.exe` e checksum; il programma avvisa e si aggiorna da solo.
- **Branch Neon di prova ancora esistente**: `test-multisettore`, da eliminare quando non serve più.

## Decisioni in sospeso

1. Acquisto dell'hosting a pagamento su Render e del certificato di firma: vedi le istruzioni in fondo a [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md), sezione "Hosting e firma".
2. Invio delle fatture elettroniche: caricamento manuale gratuito sul portale "Fatture e Corrispettivi" oppure intermediario a pagamento (invio automatico).
3. Eliminare il branch Neon `test-multisettore`.
