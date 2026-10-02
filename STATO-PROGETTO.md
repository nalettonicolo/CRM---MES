# Stato del progetto: Nicolò MES (CrmMes)

Aggiornato: 2 ottobre 2026. Questo file si aggiorna a ogni sessione di lavoro: cosa c'è, cosa manca, cosa è stato fatto. Per il diario tecnico dettagliato vedi [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md); per la mappa dei file [PROJECT-MAP.md](PROJECT-MAP.md); per il ripristino dei dati [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md).

> **Blocco 02/10 pubblicato su `main`:** G8, OEE da macchina, dichiarazione di origine UE, MRP base e tema aziendale; migrazioni Neon applicate.

## In sintesi

- **Cos'è**: gestionale di produzione (MES) con parte commerciale e documentale, per piccole e medie aziende manifatturiere. Nato per un quadrista, oggi si configura per cinque settori.
- **Versione pubblicata**: v1.7.0 (30/09/2026): installer del programma e, novità, installer del server per i clienti con server proprio. I PC con una versione precedente si aggiornano da soli. Contiene verifica in due passaggi, teleassistenza, import ed export articoli, reparti e licenza.
- **Architettura**: client Windows (WPF, .NET 8) + API web (ASP.NET Core 8) su Render + database Postgres su Neon. La stessa API serve la piattaforma web (`/app/`, Blazor WebAssembly) e la pagina dei tecnici (`/tecnici/`).
- **Test automatici**: 371 sull'API, 122 sul client desktop, 75 sulla piattaforma web e 18 sulla console, tutti verdi (02/10/2026; 586 totali). Le fatture elettroniche sono validate contro lo schema ufficiale FatturaPA.
- **Backtest end-to-end** (30/09/2026): 68 passi su 68 superati su un database vuoto e isolato, percorrendo tutti i ruoli, dal preventivo alla fattura, più cantiere, alimentare, macchine e 14 controlli di sicurezza. Dettaglio nella sezione "Backtest".
- **Uso attuale**: interno, un'azienda con due sedi.

## Cosa c'è

### Configurazione e settori
- **Configurazione in tre passi** (30/09/2026): attività dell'azienda (anche più di una, compresa la nuova "Costruzione macchine e impianti"), reparti (ufficio tecnico, produzione meccanica, carpenteria, montaggio, quadristi, collaudo, service, cantiere, spedizioni...) che diventano aree con i loro centri di lavoro tipici, moduli proposti con il motivo. La ricerca commesse filtra per il reparto dell'operatore (anche per quello identificato con il PIN).
- **Analisi di mercato** aggiornata in [ANALISI-MERCATO-MES.md](ANALISI-MERCATO-MES.md): confronto con i MES italiani e internazionali, settore per settore, con le priorità.
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
- **Costruzione macchine: collaudo e CE** (01/10/2026, piattaforma web, pagina "Collaudo e CE"):
  - collaudi **FAT** (in fabbrica) e **SAT** (presso il cliente) numerati, con lista di verifiche standard modificabile (marcatura e documenti, sicurezza elettrica EN 60204-1, arresto di emergenza e ripari, dispositivi di sicurezza, prove funzionali; per il SAT installazione, prove in produzione, formazione e consegna), valore atteso e rilevato, esito per ogni verifica; chiusura con matricola, data e nome del collaudatore; basta una verifica non superata e il collaudo resta come "non superato" (se ne apre uno nuovo); riapertura solo Admin;
  - **fascicolo tecnico**: lista degli elementi richiesti (Allegato VII Direttiva 2006/42/CE, Allegato IV Regolamento UE 2023/1230) con "dove si trova" ciascun documento, elementi aggiuntivi liberi; lo compilano ufficio tecnico e direzione;
  - **dichiarazione di conformità** precompilata (macchina, costruttore, norme EN ISO 12100 ed EN 60204-1, EMC), emessa solo con fascicolo completo e un collaudo superato della stessa matricola; numero progressivo; base giuridica scelta da sola in base alla data (Direttiva fino al 19/01/2027, Regolamento 2023/1230 dal 20/01/2027); dopo l'emissione è bloccata e si stampa; ritiro solo dell'Admin con motivo.

### Web e mobile
- **Piattaforma web** `/app/` (in produzione dal 30/09/2026, https://crmmes-api.onrender.com/app/): stesso indirizzo e stesse credenziali del programma desktop, si adatta a PC, tablet e telefono, tema chiaro o scuro automatico.
  - Oggi contiene: cruscotto (commesse aperte, completate, puntualità, OEE, fermi), commesse (filtri per stato, ricerca anche con le ultime cifre, avanzamento, ritardi, codici copiabili), dettaglio commessa (fasi, lotti di materiale usati), materiali (giacenze, sotto scorta), **vendite** (clienti con preventivi e commesse; preventivi con righe e totali, e le azioni segna come inviato, accettato o rifiutato dal cliente, crea le commesse, con conferma), **documenti di trasporto** (elenco per stato e ricerca, dettaglio con destinatario, trasporto, merce, lotti e rientri del conto lavoro), **fatture** (per amministrazione, direzione e commerciale: righe, riepilogo IVA, DDT collegati, avvisi, scarico del file XML FatturaPA delle fatture emesse), **acquisti** (fornitori con ordini e listino; ordini fornitore con consegne in ritardo, conferma e ricevimento merce riga per riga con quantità e lotto del fornitore; elenco "da ordinare" con sotto scorta e mancanti), canali di accesso per l'amministratore.
  - Le altre aree restano nel programma desktop e il menu web le elenca, così nessuno si chiede dove siano finite. Sul web ci sono anche **distinte di prelievo**, **lotti materiali** e **ricerca catalogo** (stessi nomi del desktop), più l'import Metel se il modulo è acceso.
  - Provata dal vivo: login, cruscotto, ricerca per ultime cifre, dettaglio, canali (il menu cambia subito), blocco per ruolo, vista da telefono, tema chiaro e scuro.
- **Canali di accesso configurabili dall'amministratore** (desktop, piattaforma web, pagina tecnici da telefono): quali canali usa l'azienda, da dove entra ogni ruolo, su quale canale si vede ogni area. L'amministratore entra sempre da tutti i canali attivi. La scelta vale al login e al rinnovo della sessione (entro 30 minuti), con un messaggio chiaro a chi non è abilitato. Configurabile sia dal desktop ("Canali di accesso" in Amministrazione) sia dal web.
- Pagina `/tecnici/` per telefono e tablet (stesse credenziali del gestionale):
  - rapportini con firma col dito;
  - avvio e completamento delle fasi;
  - registrazione ore.
- La sessione si rinnova da sola e il lavoro non salvato resta sul dispositivo. Tutto pubblicato con la v1.4.0.

### Sicurezza e affidabilità
- Accesso con JWT e token di rinnovo a rotazione; sei ruoli (Admin, Management, Warehouse, Purchasing, Sales, Operator).
- **Verifica in due passaggi** (30/09/2026): codici dell'app di autenticazione, segreto cifrato, codici di recupero monouso, azzeramento dall'amministratore; facoltativa per tutti e obbligatoria per i ruoli scelti dall'amministratore. Al terminale resta il PIN.
- **Selettore "solo il mio reparto"** nel terminale e nella pagina tecnici.
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
| Listini Metel | Fatto | Modulo attivabile. Import da file ANIE/METEL (record fissi tipo A o CSV/TXT). Programma: pulsante in Materiali. Web: Ricerca catalogo. Test su file sintetici; un listino produttore reale può servire solo per tarare varianti |
| Alimentare: GTIN sui prodotti, piano HACCP guidato per tipologia | Da fare | Un'azienda pilota del settore |
| Scarico di magazzino da DDT e rapportini | Fatto | Emissione DDT con riga di catalogo (MaterialId) scarica giacenza e lotti; l'annullamento li riporta. Rapportino firmato scarica solo i materiali in più rispetto a una distinta già chiusa sulla stessa commessa (il carico furgone resta il prelievo). Il DDT di vendita del prodotto finito dalla commessa non tocca i componenti. |
| Import catalogo PDF validato su cataloghi reali | Da verificare | Il primo catalogo reale disponibile |
| Piattaforma web: altre aree (qualità, HACCP, cantiere, pianificazione) | Fatto (consultazione) | Web: qualità, pianificazione, HACCP, rapportini, conto lavoro, spedizioni, margini, anagrafiche, utenti, prodotti, verifica quadri. Creazione/dipingere board/firma restano desktop o /tecnici. Terminale di reparto e etichette alimentari restano nel programma. |
| Ufficio tecnico | Fatto | Modulo "Ufficio tecnico" attivabile. **Documenti tecnici** del prodotto (disegni, schemi elettrici, istruzioni di lavoro, programmi CNC, foto) per tutto il prodotto o per una fase del ciclo, con versioni: stesso titolo e fase = versione successiva, la precedente va nello storico; ritiro senza cancellare; massimo 20 MB; file eseguibili e pagine web rifiutati, tipo del file deciso dal server. **Revisioni** del prodotto (A, B, C…): le commesse registrano la revisione con cui sono state create. **Modifiche tecniche** numerate (MT 1, 2…): proposta di nuova distinta e/o ciclo, confronto con l'attuale, commesse aperte coinvolte, approvazione, applicazione che archivia la revisione precedente, passa alla successiva e ricostruisce fasi e griglia per matricola delle commesse **in bozza**; quelle già rilasciate restano intatte e sono elencate nel resoconto. Web: pagina Ufficio tecnico (documenti, modifiche, revisioni); programma: "Documenti tecnici" dalla scheda prodotto (le modifiche tecniche per ora solo dal web) |
| Istruzioni di lavoro e disegni al terminale | Fatto | Terminale di reparto: pulsante "Istruzioni e disegni (N)" sulla fase in corso, apre l'ultima versione con il programma del PC; web: documenti sotto ogni fase nella pagina commessa |
| Collaudo macchine e CE | Fatto (web e programma) | Collaudi FAT/SAT con checklist, fascicolo tecnico, dichiarazione CE/UE con stampa; modulo attivabile. Schermata nel programma dalla scheda commessa ("Collaudo e CE"): collaudi e checklist, fascicolo tecnico, dati della dichiarazione, emissione/ritiro (i campi meno comuni della dichiarazione restano modificabili solo dal web). **Documenti collegati al fascicolo** (nuovo): ogni elemento del fascicolo tecnico si può collegare a un documento già caricato nell'ufficio tecnico per lo stesso prodotto (sempre la sua versione attuale); collegarlo segna da solo l'elemento come presente; un documento di un prodotto diverso viene rifiutato. **Firma sulla dichiarazione** (nuovo, solo web): a chi emette la dichiarazione si offre di firmare a schermo (stessa tecnica già usata per i rapportini di cantiere) — un'immagine della firma stampata sul documento. **Non è una firma digitale qualificata** (CAdES/PAdES di un prestatore accreditato) e non ne ha il valore legale di non ripudio: è facoltativa e, se lasciata vuota, la dichiarazione si emette comunque senza. **Resta**: PDF del rapporto di collaudo come file a sé (oggi solo la vista a stampa del browser), firma digitale qualificata vera e propria se mai richiesta, cattura della firma anche dal programma desktop |
| Service post-vendita | Fatto (web e programma) | Macchine installate presso i clienti con matricola (unica in azienda), cliente, commessa di origine, ubicazione, garanzia; stato attiva/dismessa. Richieste di assistenza numerate (RA) con oggetto, priorità (normale/urgente), canale (telefono/email/portale), apertura/chiusura/riapertura. Interventi per richiesta: tecnico, data, ore, descrizione, materiali usati, dentro o fuori garanzia; registrarli è aperto a qualsiasi utente autenticato, come al terminale. Pagina web e **schermata nel programma** (nuova, dalla barra laterale). **Collegamento automatico commessa → macchina installata**: quando una spedizione in uscita legata a una commessa con cliente viene avviata, la macchina si registra da sola (se non già presente), senza bloccare la spedizione se manca il cliente. **Notifica scadenza garanzia**: avviso quando una macchina ha la garanzia in scadenza entro 30 giorni (web e programma), più la distinzione visiva in garanzia/in scadenza/scaduta nell'elenco; è un avviso mostrato aprendo la pagina, non un'email o una notifica push. **Resta**: nessun invio automatico (email/notifica) della scadenza garanzia |
| Monitoraggio energetico (iperammortamento 2026) | Fatto (web e programma) | Il contatore cumulativo di energia (kWh) si aggiunge come campo opzionale alle letture che macchina/gateway già inviano (Industria 4.0); il consumo di un periodo si calcola da solo sommando le variazioni positive tra letture consecutive, ignorando gli azzeramenti del contatore invece di sballare il totale. "Progetti di efficientamento" per macchina: periodo di riferimento (ex ante) e periodo successivo (ex post, impostabile in un secondo momento), con percentuale di risparmio calcolata e normalizzata per giorno se i due periodi hanno lunghezza diversa — adatta come base per la perizia asseverata richiesta dall'iperammortamento/Transizione 5.0. **kWh per commessa** (nuovo): somma dei kWh delle macchine che riportano il codice della commessa nelle letture, anche su più macchine insieme. **Report stampabile** (nuovo, web): vista a stampa del progetto (periodo, kWh, risparmio, metodo di calcolo) con il bottone "Stampa" del browser, come già per la dichiarazione CE — non un PDF generato dal server. Pagina web e **schermata nel programma** (nuova, dalla barra laterale) con elenco progetti, creazione, periodo ex post, consumo libero di una macchina o di una commessa. Gestione progetti riservata ad Admin e Direzione; consultare i consumi è aperto a tutti. Modulo attivabile. **Resta aperto**: nessuna macchina reale collega ancora un contatore di energia (serve il gateway Industria 4.0 collegato, vedi sezione "Decisioni in sospeso"); il report stampabile è una vista web, non un file PDF scaricabile autonomo |
| Import ed export articoli da Excel (listino "LISTA WEL") | Fatto, da provare dal titolare | Scheda Materiali (programma e web): "Importa da Excel" con anteprima (nuovi, da aggiornare, già presenti, errori e avvisi riga per riga), scelta se aggiornare gli esistenti, poi conferma; riconosce da solo intestazioni e colonne (anche sotto una riga di titolo), converte NR/METRO/CONFEZ/MATASSA... in pz/m/conf/matassa, legge prezzo, IVA e data; mai toccate le giacenze. "Esporta Excel" con le stesse colonne, per modificare e reimportare. L'import del listino reale lo lancia il titolare |
| Console fornitore remota (pagamenti, stato installazioni, attivazione servizi e canone, teleassistenza) | Fatta, da pubblicare e collegare a Stripe | **Passo 1 (gestionale del cliente)**: licenza firmata (piano, moduli pagati, utenti, stato) ricevuta ogni ora dalla console inviando soli dati tecnici; moduli non pagati spenti, limite utenti; sospensione = solo consultazione generale (cruscotto ed elenchi); avviso in programma e web; senza chiave di licenza tutto come prima. **Passo 2 (console, progetto `CrmMes.Console`)**: accesso con password e codice obbligatorio dell'app (primo avvio guidato); cruscotto (clienti in regola, in ritardo, sospesi, canoni mensili, installazioni online); clienti con piano, moduli e utenti aggiuntivi che ricalcolano il canone; installazioni con chiave di licenza mostrata una sola volta, revoca, ultimo contatto, versione, utenti, moduli in uso; bonifici registrati a mano con data di copertura; giorni di tolleranza; sospensione o riattivazione manuale; listino modificabile; registro delle operazioni; chiave di firma cifrata nel database. **Da fare**: pubblicarla su Render (serve un database dedicato e il segreto `CONSOLE_SECRET`: istruzioni in `CrmMes.Console/render-console.yaml`), **Passo 3 fatto (Stripe pronto da collegare)**: link di pagamento mensile da inviare al cliente (carta o addebito SEPA), pagamenti registrati all'istante dal webhook firmato (anche se arrivano due volte), data "pagato fino al" aggiornata dal periodo della fattura, pagamenti non riusciti registrati senza sospendere subito (valgono i giorni di tolleranza), cambio di moduli o utenti che aggiorna il prezzo su Stripe con conguaglio, link per far aggiornare carta o conto al cliente. Servono le chiavi `STRIPE_SECRET_KEY` e `STRIPE_WEBHOOK_SECRET` del titolare. **Passo 4 fatto**: "Scrivi all'assistenza" nel programma e sul web (oggetto, descrizione, recapito, ID RustDesk) → la richiesta arriva numerata nella console con lo stato tecnico del server allegato; dalla console si risponde e si cambia lo stato, e il cliente vede risposta e stato nel gestionale; funziona anche con abbonamento sospeso |
| Fatture passive e scadenziario | Fatto e pubblicato (G8) | Import XML FatturaPA ricevuto; scadenziario pagamenti e incassi; segna pagato e registra solleciti (senza email). API/web/desktop; `AddPayables` applicata su Neon il 02/10/2026; PayablesTests 5/5 e suite completa 586/586 |
| Piattaforma web: creare e modificare anagrafiche e documenti | Da fare | Oggi si consulta tutto e si fanno i passaggi dei preventivi; la creazione di clienti, preventivi e commesse resta nel desktop |
| Server presso il cliente | Fatto, da provare su un server reale | Installer `NicoloMES-Server-Setup.exe` (dalla prossima release): crea database e utente PostgreSQL, chiave, `server.json` protetto, servizio di Windows con riavvio automatico, firewall solo rete aziendale, migrazioni, backup notturno con verifica e copia su NAS; aggiornamento rieseguendo l'installer; variante Docker con database incluso (`server/docker`); istruzioni in `server/LEGGIMI-SERVER.md`. Manca solo la prova su un Windows Server vero |
| Teleassistenza | Fatto | Finestra Teleassistenza nel programma (anche dalla schermata di accesso, senza credenziali): contatti di assistenza, avvio di RustDesk (o pagina ufficiale per scaricarlo) con server e chiave propri se impostati, pacchetto diagnostico sul Desktop (versioni, stato server, diagnostica per l'admin, errori della settimana, nessuna password); registro degli errori del programma (14 giorni); pagina Assistenza sulla piattaforma web con stato del server per l'admin. Contatti e server RustDesk si impostano in `server.json` (server del cliente) o nelle variabili `Support__*` (cloud) |
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
| 30/09/2026 | (prossima release) | Ufficio tecnico: documenti versionati per fase, revisioni del prodotto, modifiche tecniche con approvazione e aggiornamento delle commesse in bozza; documenti al terminale e sul web; migrazione AddEngineering applicata al database di produzione |
| 01/10/2026 | (prossima release) | Collaudo macchine e CE: collaudi FAT/SAT, fascicolo tecnico, dichiarazione di conformità con base giuridica automatica (2006/42/CE → 2023/1230), pagine web "Collaudo e CE"; migrazione AddMachineTesting applicata al database di produzione |
| 01/10/2026 | (prossima release) | Piattaforma web: nuova pagina Manutenzione (macchine, interventi preventivi e correttivi con scadenza e ricorrenza, completamento che genera la prossima occorrenza) |
| 01/10/2026 | (prossima release) | Service post-vendita: macchine installate con matricola e garanzia, richieste di assistenza numerate, interventi con ore e materiali; pagina web "Service post-vendita"; migrazione AddService applicata al database di produzione |
| 01/10/2026 | (prossima release) | Monitoraggio energetico: contatore kWh nelle letture macchina, calcolo del consumo che ignora gli azzeramenti del contatore, progetti di efficientamento con periodo ex ante/ex post e risparmio normalizzato per giorno; pagina web "Monitoraggio energetico"; migrazione AddEnergyMonitoring applicata al database di produzione |
| 01/10/2026 | (prossima release) | Chiusura lacune tecniche segnalate: notifica garanzia in scadenza (Service), collegamento automatico commessa→macchina installata alla spedizione, kWh per commessa e report stampabile (Energia), schermate nel programma desktop per Service post-vendita, Collaudo e CE e Monitoraggio energetico. Nessuna migrazione |
| 01/10/2026 | (prossima release) | Collaudo e CE: collegamento degli elementi del fascicolo tecnico ai documenti dell'ufficio tecnico (sempre la versione attuale, validato contro il prodotto della commessa); firma a schermo facoltativa sulla dichiarazione di conformità, stampata sul documento — chiaramente non una firma digitale qualificata. Migrazione AddMachineTestingDocumentAndSignature applicata al database di produzione |
| 02/10/2026 | (prossima release) | G8: import fatture passive XML FatturaPA, scadenziario pagamenti/incassi e solleciti su web e desktop; blocco G8/OEE/origine UE/MRP/tema aziendale pubblicato su main con migrazioni Neon applicate; test API mirati 14/14 e suite completa 586/586 |
| 30/09/2026 | v1.6.0 | Installer da collegare in seguito; piattaforma web `/app/` con vendite, DDT e fatture; (cruscotto, commesse, materiali); canali di accesso configurabili dall'amministratore; basi per server presso il cliente (servizio Windows, configurazione, migrazioni automatiche, log) e per la teleassistenza (contatti e diagnostica) |
| prossimo | - | MRP, ubicazioni, capacità finita, pacchetti di settore, altre aree sul web |

## Pubblicazione e ambienti

- **API**: https://crmmes-api.onrender.com (piano gratuito Render), deploy automatico a ogni push su `main`.
- **Database**: Neon Postgres, progetto `cool-field-94626300`. Tutte le migrazioni fino a `AddMachineInterconnection` sono applicate in produzione il 30/09/2026, dopo prova su un branch dedicato; `AddEngineering` il 30/09/2026 e `AddMachineTesting` il 01/10/2026 (solo tabelle nuove, prima del push). `AddAccessChannels` è applicata in produzione il 30/09/2026 (prima del push): le 105 sessioni aperte risultano "desktop", nessuno è stato disconnesso. `AddPayables`, `AddSoftwareOriginDeclaration` e `AddUiTheme` risultano applicate su Neon il 02/10/2026, prima del push su `main`.
- **Client**: release GitHub con `NicoloMES-Setup.exe` e checksum; il programma avvisa e si aggiorna da solo.
- **Branch Neon di prova ancora esistente**: `test-multisettore`, da eliminare quando non serve più.

## Decisioni in sospeso

1. Acquisto dell'hosting a pagamento su Render e del certificato di firma: vedi le istruzioni in fondo a [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md), sezione "Hosting e firma".
2. Invio delle fatture elettroniche: caricamento manuale gratuito sul portale "Fatture e Corrispettivi" oppure intermediario a pagamento (invio automatico).
3. Eliminare il branch Neon `test-multisettore` (database di prova `backtest`, `webtest`, `webtest2`): approvato dal titolare, ma l'eliminazione va fatta da lui dalla console Neon (all'assistente è bloccata dalle regole di sicurezza).
