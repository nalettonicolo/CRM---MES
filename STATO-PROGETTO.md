# Stato del progetto: Nicolò MES (CrmMes)

Aggiornato: 30 settembre 2026. Questo file si aggiorna a ogni sessione di lavoro: cosa c'è, cosa manca, cosa è stato fatto. Per il diario tecnico dettagliato vedi [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md); per la mappa dei file [PROJECT-MAP.md](PROJECT-MAP.md); per il ripristino dei dati [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md).

## In sintesi

- **Cos'è**: gestionale di produzione (MES) con parte commerciale e documentale, per piccole e medie aziende manifatturiere. Nato per un quadrista, oggi si configura per cinque settori.
- **Versione pubblicata**: v1.6.0 (30/09/2026), con installer per Windows e aggiornamento automatico: i PC con una versione precedente si aggiornano da soli. Comprende tutto il lavoro della v1.5.0 (la cui release si era fermata sui test).
- **Architettura**: client Windows (WPF, .NET 8) + API web (ASP.NET Core 8) su Render + database Postgres su Neon. La stessa API serve la piattaforma web (`/app/`, Blazor WebAssembly) e la pagina dei tecnici (`/tecnici/`).
- **Test automatici**: 276 sull'API, 113 sul client desktop e 47 sulla piattaforma web, tutti verdi (30/09/2026). Le fatture elettroniche sono validate contro lo schema ufficiale FatturaPA.
- **Backtest end-to-end** (30/09/2026): 68 passi su 68 superati su un database vuoto e isolato, percorrendo tutti i ruoli, dal preventivo alla fattura, più cantiere, alimentare, macchine e 14 controlli di sicurezza. Dettaglio nella sezione "Backtest".
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
- **Interconnessione macchine (Industria 4.0/5.0)**: ogni macchina invia stato, pezzi e allarmi con un token proprio; nel dettaglio macchina disponibilità, pezzi, ore di marcia e fermo, allarmi della giornata; gateway OPC UA/MQTT pronto in `scripts/machine-gateway`.

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
- **Piattaforma web** `/app/` (in produzione dal 30/09/2026, https://crmmes-api.onrender.com/app/): stesso indirizzo e stesse credenziali del programma desktop, si adatta a PC, tablet e telefono, tema chiaro o scuro automatico.
  - Oggi contiene: cruscotto (commesse aperte, completate, puntualità, OEE, fermi), commesse (filtri per stato, ricerca anche con le ultime cifre, avanzamento, ritardi, codici copiabili), dettaglio commessa (fasi, lotti di materiale usati), materiali (giacenze, sotto scorta), **vendite** (clienti con preventivi e commesse; preventivi con righe e totali, e le azioni segna come inviato, accettato o rifiutato dal cliente, crea le commesse, con conferma), **documenti di trasporto** (elenco per stato e ricerca, dettaglio con destinatario, trasporto, merce, lotti e rientri del conto lavoro), **fatture** (per amministrazione, direzione e commerciale: righe, riepilogo IVA, DDT collegati, avvisi, scarico del file XML FatturaPA delle fatture emesse), **acquisti** (fornitori con ordini e listino; ordini fornitore con consegne in ritardo, conferma e ricevimento merce riga per riga con quantità e lotto del fornitore; elenco "da ordinare" con sotto scorta e mancanti), canali di accesso per l'amministratore.
  - Le altre aree restano nel programma desktop e il menu web le elenca, così nessuno si chiede dove siano finite.
  - Provata dal vivo: login, cruscotto, ricerca per ultime cifre, dettaglio, canali (il menu cambia subito), blocco per ruolo, vista da telefono, tema chiaro e scuro.
- **Canali di accesso configurabili dall'amministratore** (desktop, piattaforma web, pagina tecnici da telefono): quali canali usa l'azienda, da dove entra ogni ruolo, su quale canale si vede ogni area. L'amministratore entra sempre da tutti i canali attivi. La scelta vale al login e al rinnovo della sessione (entro 30 minuti), con un messaggio chiaro a chi non è abilitato. Configurabile sia dal desktop ("Canali di accesso" in Amministrazione) sia dal web.
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

## Backtest

Ultima esecuzione: 30/09/2026, API avviata in locale su un database dedicato e vuoto (nel branch Neon di prova), nessun dato reale. Esito: **68/68 passi superati**.

| Area | Passi | Cosa è stato verificato |
|---|---|---|
| Configurazione e utenti | 9 | Primo Admin, registrazione pubblica poi chiusa, un utente per ruolo, preset di settore, dati fiscali con controllo IBAN, prefisso GS1 |
| Vendite e direzione | 6 | Cliente e dati SDI, preventivo inviato, accettato e convertito in commessa; costo reale e margine (vendita 9.120 €), controllo margini |
| Magazzino e acquisti | 8 | Materiali, lotti, prelievo dalla commessa con scarico FIFO, conto lavoro con rientro parziale e scarto (residuo esatto) |
| Produzione e reparto | 11 | Fasi dal ciclo, rilascio, ricerca per ultime 4 cifre, elenco commesse aperte, PIN, fasi avviate e chiuse, ore; verifica CEI EN 61439 completata |
| Documenti e fatturazione | 10 | DDT numerato, fattura differita dal DDT con prezzo e sconto del preventivo, emissione, XML TD24 con totale corretto, stesso DDT non fatturabile due volte |
| Tracciabilità e alimentare | 4 | Etichetta dalla distinta, pallet con SSCC, richiamo del lotto fino a DDT e cliente, HACCP con azione correttiva obbligatoria |
| Cantiere | 4 | Rapportino negato su commessa chiusa, creato, firmato con immagine reale, ore e materiali nei costi |
| Industria 4.0 | 2 | Dati macchina accettati col token e rifiutati senza; giornata con 100 pezzi e 1 allarme |
| Sicurezza | 14 | Accesso richiesto su cinque aree, operatore senza permessi admin, margini negati al commerciale, token JWT falso respinto, intestazioni CSP, blocco dei tentativi di login (429) |

Difetto trovato e corretto durante il backtest: nei PDF generati le coppie di lettere "ff", "fi", "fl", "tt" venivano disegnate come un solo simbolo tipografico; copiando un codice dal PDF alcune lettere sparivano. Ora le legature sono disattivate in tutti i PDF del client, con test dedicato.

Non ancora provato: la pagina web dei tecnici dal browser con accesso reale (il login automatico con credenziali è bloccato dalle regole di sicurezza dell'assistente); i suoi flussi sono coperti dai test automatici e dal backtest sulle stesse API.

## Cosa manca

| Punto | Stato | Cosa serve |
|---|---|---|
| Invio automatico delle fatture allo SdI | Da decidere | Intermediario accreditato a pagamento; oggi il file si carica a mano sul portale gratuito |
| Hosting a pagamento (garanzia di nessuna sospensione) | Da acquistare | Tuo acquisto del piano su Render: istruzioni in RIEPILOGO-SVILUPPO.md, "Hosting e firma" |
| Firma digitale dell'eseguibile (niente avviso di Windows) | Da acquistare | Certificato di firma del codice: istruzioni in RIEPILOGO-SVILUPPO.md |
| Collegamento reale delle macchine | Da configurare | Ingresso dati e gateway pronti: servono indirizzi OPC UA o topic MQTT di ogni macchina e un PC gateway in reparto |
| OEE del cruscotto dai dati macchina | Da fare | Oggi il cruscotto usa fasi e fermi dichiarati; i dati macchina sono nel dettaglio di ciascuna |
| Listini Metel | In attesa | Un file Metel reale di un produttore |
| Alimentare: GTIN sui prodotti, piano HACCP guidato per tipologia | Da fare | Un'azienda pilota del settore |
| Scarico di magazzino da DDT e rapportini | Scelta aperta | Oggi DDT e rapportini registrano cosa esce ma non scaricano la giacenza |
| Import catalogo PDF validato su cataloghi reali | Da verificare | Il primo catalogo reale disponibile |
| Piattaforma web: altre aree (qualità, HACCP, cantiere, manutenzione, pianificazione) | In corso | Una alla volta; oggi cruscotto, commesse, materiali, vendite, acquisti, DDT, fatture, canali |
| Piattaforma web: creare e modificare anagrafiche e documenti | Da fare | Oggi si consulta tutto e si fanno i passaggi dei preventivi; la creazione di clienti, preventivi e commesse resta nel desktop |
| Server presso il cliente | Basi pronte | L'API gira come servizio di Windows, legge `C:\ProgramData\NicoloMES\server.json`, aggiorna il database da sola (`--migrate` o `Database:AutoMigrate`), scrive log giornalieri. Mancano l'installer del server, il backup locale e la variante Docker con database incluso |
| Teleassistenza | Basi pronte | Lato server: contatti di assistenza leggibili prima del login e diagnostica per l'amministratore (versione, database, migrazioni, disco), senza segreti. Mancano nel client: finestra Teleassistenza, avvio della sessione remota (RustDesk), pacchetto diagnostico |
| Installer del client "da collegare in seguito" | Fatto, pubblicato con la v1.6.0 | L'installer chiede server aziendale, cloud o "collegherò il server in seguito"; nel terzo caso il programma chiede l'indirizzo al primo avvio |
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
| 30/09/2026 | (in v1.6.0) | Backtest end-to-end 68/68; legature disattivate nei PDF (codici copiabili senza lettere perse), test PDF resi stabili |
| 30/09/2026 | v1.5.0 (release fermata dai test) | Interconnessione macchine (token per macchina, giornata con disponibilità e pezzi, gateway OPC UA/MQTT); corretto l'import catalogo PDF con codici contenenti ff/fi/fl |
| 30/09/2026 | v1.6.0 | Installer da collegare in seguito; piattaforma web `/app/` con vendite, DDT e fatture; (cruscotto, commesse, materiali); canali di accesso configurabili dall'amministratore; basi per server presso il cliente (servizio Windows, configurazione, migrazioni automatiche, log) e per la teleassistenza (contatti e diagnostica) |
| prossimo | - | Altre aree sulla piattaforma web; installer del server e teleassistenza nel client; OEE dai dati macchina |

## Pubblicazione e ambienti

- **API**: https://crmmes-api.onrender.com (piano gratuito Render), deploy automatico a ogni push su `main`.
- **Database**: Neon Postgres, progetto `cool-field-94626300`. Tutte le migrazioni fino a `AddMachineInterconnection` sono applicate in produzione il 30/09/2026, dopo prova su un branch dedicato. `AddAccessChannels` è applicata in produzione il 30/09/2026 (prima del push): le 105 sessioni aperte risultano "desktop", nessuno è stato disconnesso.
- **Client**: release GitHub con `NicoloMES-Setup.exe` e checksum; il programma avvisa e si aggiorna da solo.
- **Branch Neon di prova ancora esistente**: `test-multisettore`, da eliminare quando non serve più.

## Decisioni in sospeso

1. Acquisto dell'hosting a pagamento su Render e del certificato di firma: vedi le istruzioni in fondo a [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md), sezione "Hosting e firma".
2. Invio delle fatture elettroniche: caricamento manuale gratuito sul portale "Fatture e Corrispettivi" oppure intermediario a pagamento (invio automatico).
3. Eliminare il branch Neon `test-multisettore` (database di prova `backtest`, `webtest`, `webtest2`): approvato dal titolare, ma l'eliminazione va fatta da lui dalla console Neon (all'assistente è bloccata dalle regole di sicurezza).
