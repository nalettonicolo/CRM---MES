# Mappa progetto Gestionale Elettrico

Aggiornata: 2026-09-22 (seconda revisione: URL API configurabile, import distinta base da file, centri di lavoro con carico indicativo, preparazione hosting gratuito Render.com)

## Visione architetturale

```mermaid
flowchart LR
    WPF[Client Windows WPF - shell presente]
    API[CrmMes.Api - ASP.NET Core .NET 8]
    CORE[CrmMes.Core - modelli e DbContext]
    DB[(Neon PostgreSQL)]
    IMPORT[Import cataloghi e documenti - da portare nel backend reale]

    WPF --> API
    API --> CORE
    CORE --> DB
    IMPORT --> API
```

## Gia costruito e verificato

### Backend reale .NET

- Soluzione `CrmMes.sln`.
- `CrmMes.Api`: API ASP.NET Core .NET 8.
- `CrmMes.Core`: modelli condivisi e `ApplicationDbContext`.
- Swagger disponibile in ambiente Development.
- Health check disponibile su `GET /health`.
- Connessione Neon PostgreSQL configurata tramite User Secrets.
- Migrazioni EF Core applicate a Neon: `InitialCreate`, `AddUserAuthentication`, `AddPurchaseOrderReceiving`, `AddLowStockReordering`, `AddProductionCore` (nucleo MES: prodotti, distinta base, ciclo di lavoro, commesse), `AddMaterialLotTraceability`, `AddWorkCenters`.
- Build verificata con 0 errori e 0 warning su tutti i 4 progetti (`CrmMes.Api`, `CrmMes.Core`, `CrmMes.Desktop`, `CrmMes.Api.Tests`).
- Gestione errori centralizzata: `UseExceptionHandler()` + `AddProblemDetails()` (risposte RFC 9110 uniformi; dettaglio dell'eccezione incluso solo in ambiente Development).
- HSTS attivo fuori da Development.
- Logging delle richieste HTTP (`UseHttpLogging`, metodo/percorso/stato/durata).
- Compressione delle risposte (gzip) abilitata.
- `CrmMes.Api.Tests`: 81 test di integrazione (xUnit + `WebApplicationFactory` + Sqlite in-memory, non toccano mai Neon) che coprono autenticazione, refresh token, bootstrap Admin, policy di autorizzazione per ruolo, ciclo distinte di prelievo (incluse le modifiche), sottoscorte, ciclo ordini fornitore (incluse le modifiche), import catalogo CSV/Excel, prodotti/distinta base/ciclo di lavoro (incluso import distinta da file CSV/Excel), commesse, tracciabilità lotti materiali, verifica disponibilità/performance, e centri di lavoro con carico. Eseguibili con `dotnet test CrmMes.Api.Tests`.
- Workflow GitHub Actions (`.github/workflows/build.yml`): build + test automatici su push/PR verso `main`.
- Refresh token: login/registrazione restituiscono un access token di 30 minuti + un refresh token di 30 giorni (rotazione ad ogni uso, revoca su logout). Endpoint `POST /api/auth/refresh` e `POST /api/auth/logout`. Il client desktop rinnova automaticamente in background prima della scadenza.
- Import catalogo Excel (`POST /api/supplier-catalog/import-excel`, libreria ClosedXML, MIT): stessa logica di upsert del CSV, condivisa tramite un metodo comune.

### Modello dati presente

- Utenti.
- Aree.
- Materiali.
- Fornitori.
- Relazione materiale-fornitore.
- Distinte di prelievo: collegamento opzionale `WorkOrderId` alla commessa che le ha generate.
- Righe distinta.
- Materiali mancanti: `WithdrawalSlipId` reso opzionale per supportare richieste generate da sottoscorta senza distinta di origine.
- Ordini di acquisto: aggiunti `ConfirmedAt` e `ReceivedAt`.
- Righe ordine di acquisto: aggiunti `ReceivedQuantity` e collegamento opzionale a `MissingMaterial`.
- Log di audit.
- Refresh token: tabella dedicata con hash del token (mai salvato in chiaro), scadenza, revoca e collegamento al token successivo (rotazione).
- **Prodotti** (`Product`): anagrafica di cosa si produce, volutamente generica/multisettore (codice, nome, descrizione, attivo/disattivo) — non specifica ai quadri elettrici, applicabile a qualsiasi settore manifatturiero.
- **Distinta base** (`BillOfMaterialItem`): righe materiale+quantità+note collegate a un prodotto, per codice materiale (stesso pattern delle righe distinta di prelievo).
- **Ciclo di lavoro** (`RoutingStep`): fasi ordinate (numero sequenza, nome, descrizione, centro di lavoro testo libero, minuti stimati) collegate a un prodotto — il campo "centro di lavoro" è testo libero apposta per restare neutro rispetto al settore.
- **Commesse** (`WorkOrder`): job di produzione (codice, prodotto, quantità, area opzionale, riferimento cliente, stato Draft/Released/InProgress/Completed/Cancelled, scadenza, note, timestamp di rilascio/completamento).
- **Fasi di commessa** (`WorkOrderOperation`): scatto fotografico del ciclo di lavoro del prodotto preso al momento della creazione della commessa, così che modifiche successive al ciclo del prodotto non alterino retroattivamente commesse già in corso; ogni fase ha un proprio stato Pending/InProgress/Done con timestamp di avvio/completamento.
- **Lotto prodotto** (`WorkOrder.ProductLotNumber`): identificativo di lotto/matricola dei beni finiti prodotti da una commessa (generato automaticamente se non specificato), il lato "prodotto finito" della tracciabilità.
- **Lotti materiale** (`MaterialLot`): batch tracciabili di un materiale così come sono entrati in giacenza (ricezione ordine fornitore, giacenza iniziale dichiarata alla creazione del materiale, o carico manuale); tengono quantità residua e quantità iniziale, e il collegamento a fornitore/ordine d'acquisto quando noto.
- **Consumo lotti** (`MaterialLotConsumption`): collegamento tra una riga di distinta di prelievo e i lotti da cui è stata effettivamente prelevata (FIFO, dal lotto più vecchio), il lato "materiale in ingresso" della tracciabilità — insieme a `ProductLotNumber` permette la genealogia in entrambe le direzioni (da un lotto materiale a cosa è stato costruito, da una commessa a quali lotti materiale ha consumato).
- **Centri di lavoro** (`WorkCenter`): anagrafica opzionale di reparti/linee (codice, nome, capacità minuti/giorno, attivo/disattivo). Il campo "centro di lavoro" su `RoutingStep`/`WorkOrderOperation` resta testo libero (nessun obbligo di censire un centro per usare il sistema); quando un nome coincide con un `WorkCenter` registrato, il carico viene confrontato con la sua capacità.

### API gia disponibili

- `GET /health`
- `GET /api/health/status`
- `GET /api/materials`
- `GET /api/materials/{id}`
- `POST /api/materials`
- `DELETE /api/materials/{id}`: disattivazione logica
- `GET /api/users`
- `POST /api/users`: crea utente con ruolo e password impostati dall'Admin (prima creava utenti senza password, quindi inutilizzabili al login)
- `GET /api/areas`
- `POST /api/areas`
- `GET /api/suppliers`
- `POST /api/suppliers`
- `POST /api/withdrawal-slips`
- `GET /api/withdrawal-slips/{id}`
- `PUT /api/withdrawal-slips/{id}`: modifica note e righe, solo per distinte in bozza
- `POST /api/withdrawal-slips/{id}/close`: chiusura autenticata e scarico stock transazionale; blocca anche la chiusura di una distinta già annullata (bug corretto: prima lo permetteva, scaricando lo stock per errore)
- `POST /api/withdrawal-slips/{id}/ready`: passaggio Draft -> Ready senza mancanti
- `POST /api/auth/register`: il primissimo utente registrato sul database diventa Admin (bootstrap), i successivi Operator
- `POST /api/auth/login`
- `POST /api/auth/refresh`: rinnova access e refresh token (rotazione)
- `POST /api/auth/logout`: revoca il refresh token
- `GET /api/procurement/missing`
- `GET /api/procurement/low-stock`: materiali attivi sotto la scorta minima, con quantità suggerita e flag se già richiesti
- `POST /api/procurement/low-stock/scan`: crea materiali mancanti (Source `MinStock`) per i materiali sotto minimo non ancora richiesti
- `POST /api/procurement/purchase-orders`
- `GET /api/procurement/purchase-orders`
- `GET /api/procurement/purchase-orders/{id}`
- `PUT /api/procurement/purchase-orders/{id}`: modifica fornitore e righe, solo per ordini in bozza; riapre i materiali mancanti scollegati e ricollega quelli ancora referenziati
- `POST /api/procurement/purchase-orders/{id}/confirm`: passaggio Draft -> Confirmed
- `POST /api/procurement/purchase-orders/{id}/receive`: ricezione merce (anche parziale), scarico automatico del materiale mancante collegato e carico stock transazionale; ogni riga ricevuta genera anche un lotto materiale tracciabile (numero lotto opzionale, auto-generato se non indicato)
- `POST /api/procurement/purchase-orders/{id}/cancel`: annullamento ordine, riapertura dei materiali mancanti collegati
- `GET /api/supplier-catalog/search?q=...`
- `POST /api/supplier-catalog/import-csv`
- `POST /api/supplier-catalog/import-excel`: stessa logica del CSV, per file .xlsx
- `GET /api/products?activeOnly=&q=`
- `GET /api/products/{id}`: dettaglio con distinta base e ciclo di lavoro
- `POST /api/products`
- `PUT /api/products/{id}`
- `DELETE /api/products/{id}`: disattivazione logica
- `PUT /api/products/{id}/bom`: sostituisce l'intera distinta base (valida che tutti i codici materiale esistano e siano attivi)
- `POST /api/products/{id}/bom/import`: importa la distinta base da file Excel (.xlsx) o CSV (colonne materialCode, quantity, notes), stessa validazione e logica "replace all" dell'endpoint manuale sopra
- `PUT /api/products/{id}/routing`: sostituisce l'intero ciclo di lavoro (assegna automaticamente il numero di sequenza in base all'ordine ricevuto)
- `GET /api/work-orders?status=`
- `GET /api/work-orders/{id}`
- `POST /api/work-orders`: crea la commessa, assegna un numero di lotto prodotto (auto-generato se non specificato) e scatta una fotografia del ciclo di lavoro del prodotto come fasi della commessa
- `PUT /api/work-orders/{id}`: modifica quantità/area/riferimento/scadenza/note, solo per commesse in bozza
- `GET /api/work-orders/{id}/material-check`: confronta la distinta base (scalata per la quantità di commessa) con la giacenza attuale, riga per riga; non blocca nulla, serve per avvisare prima del rilascio
- `POST /api/work-orders/{id}/release?force=`: Draft -> Released; se la verifica materiali segnala una carenza, risponde 409 con il dettaglio a meno che `force=true`, nel qual caso rilascia comunque e lo registra nell'audit log
- `POST /api/work-orders/{id}/cancel`: bloccato se già completata o annullata
- `POST /api/work-orders/{id}/complete`: richiede tutte le fasi completate
- `POST /api/work-orders/{id}/operations/{operationId}/start`: qualsiasi utente autenticato (operatore di reparto); alla prima fase avviata la commessa passa automaticamente a InProgress
- `POST /api/work-orders/{id}/operations/{operationId}/complete`: qualsiasi utente autenticato; la risposta include per ogni fase i minuti effettivi e il rapporto stimato/effettivo (performance) una volta completata
- `POST /api/work-orders/{id}/generate-withdrawal-slip`: genera una distinta di prelievo dalla distinta base del prodotto, con quantità moltiplicate per la quantità di commessa; richiede un'area assegnata alla commessa
- `GET /api/work-orders/{id}/material-lots`: tracciabilità all'indietro — tutti i lotti materiale consumati dalle distinte di prelievo generate da questa commessa
- `GET /api/work-orders/dashboard?days=`: KPI aggregati sul periodo indicato (default 7 giorni) — commesse per stato, fasi completate, performance media stimato/effettivo, commesse completate, percentuale di consegne puntuali; primo passo verso una vista OEE, non ancora OEE completo (manca il tracciamento di fermi macchina e scarti/qualità)
- `GET /api/material-lots?materialCode=&onlyWithStock=`
- `GET /api/material-lots/{id}`: dettaglio lotto con la tracciabilità in avanti (quali distinte/commesse lo hanno consumato)
- `POST /api/material-lots`: carico manuale di un lotto (correzioni, campioni, sotto-assemblati interni), aumenta la giacenza del materiale come farebbe una ricezione ordine
- `GET /api/work-centers?activeOnly=`
- `POST /api/work-centers`
- `PUT /api/work-centers/{id}`
- `DELETE /api/work-centers/{id}`: disattivazione logica
- `GET /api/work-centers/load`: per ogni centro registrato, minuti in attesa (fasi Pending/InProgress su commesse Released/InProgress il cui campo "centro di lavoro" combacia per nome) confrontati con la capacità giornaliera, più i centri usati sul campo ma non ancora censiti; indicativo (giorni di arretrato), non una pianificazione a calendario

### Flusso verificato su Neon

1. Creazione area.
2. Creazione utente.
3. Creazione materiale.
4. Creazione distinta.
5. Confronto codice con catalogo Neon.
6. Rilevamento stock insufficiente.
7. Creazione automatica del materiale mancante.
8. Registrazione e login utente reale con generazione JWT.
9. Rilevamento materiale sotto scorta minima (`GET /api/procurement/low-stock`), creazione automatica del materiale mancante tramite scan (`POST /api/procurement/low-stock/scan`) con quantità suggerita = scorta minima - giacenza, e idempotenza (scan ripetuto non duplica la richiesta).
10. Applicazione policy di autorizzazione per ruolo verificata (403 per utente senza ruolo sufficiente).
11. Login dal client desktop WPF con utente reale, API locale collegata a Neon tramite User Secrets.

## Prototipo esistente

Il progetto contiene ancora il primo MVP Node/Express:

- `server.js`.
- `public/index.html`.
- `data/store.json`.
- Import PDF, fogli di calcolo e documenti.
- Import cataloghi Schneider/Pizzato tramite file.
- Ricerca rapida locale e fallback di ricerca esterna.

Il prototipo è utile come riferimento funzionale, ma non deve essere usato come database di produzione. La direzione ufficiale è `.NET API + Neon + client Windows`.

## Cosa manca

### Priorita 1: completare il backend operativo

- Autorizzazione per endpoint e ruoli: policy JWT presenti per Admin, Warehouse, Purchasing e la policy combinata `PurchasingOrWarehouse` (ricezione ordini, sottoscorte).
- Ruoli: amministratore, magazzino, acquisti, operatore.
- Associazione utenti-aree presente; controllo accesso per area sulle distinte ancora da completare.
- Bootstrap utente Admin implementato: il primo utente registrato diventa Admin automaticamente; `POST /api/users` permette ora a un Admin di creare utenti con ruolo e password specifici (prima creava solo Operator senza password).
- Elenco, modifica (solo in bozza) e annullamento delle distinte implementati.
- Ciclo ordini di acquisto Draft -> Confirmed -> PartiallyReceived/Received implementato e testato su Neon, con annullamento, modifica (solo in bozza) e collegamento ai materiali mancanti.
- Gestione sottoscorte (ordine minimo) implementata e testata su Neon: rilevamento materiali sotto `MinStock`, creazione automatica del materiale mancante alla chiusura di una distinta che porta un materiale sotto minimo, scan manuale (`POST /api/procurement/low-stock/scan`) per i casi non coperti da un prelievo (es. correzioni manuali di giacenza).
- Audit log scritto automaticamente per creazione, conferma, ricezione e annullamento ordini di acquisto, per lo scan sottoscorte, e ora anche per le distinte di prelievo (creazione, pronta, chiusura, annullamento) e la creazione utenti.
- Validazione e gestione degli errori uniforme; gestione errori non previsti centralizzata con ProblemDetails.
- Policy `Warehouse` aggiunta anche a `DELETE /api/materials/{id}` (prima qualsiasi utente autenticato poteva disattivare un materiale).
- Codici auto-generati di distinte e ordini fornitore (`DP-...`, `OD-...`) ora includono un suffisso casuale: il solo timestamp al secondo poteva generare codici duplicati se due venivano creati nello stesso secondo (violazione del vincolo di unicità), scoperto scrivendo i test di integrazione.
- Refresh token implementato: access token ridotto da 8 ore a 30 minuti, refresh token di 30 giorni con rotazione a ogni utilizzo e revoca su logout.
- Bug corretto scrivendo i test: aggiungere una riga a una distinta/ordine già esistente durante una modifica veniva registrato come "aggiornamento" invece che "nuova riga" da Entity Framework, causando un errore. Interessava solo il percorso di modifica, non quello di creazione.
- Bug correlato corretto scrivendo i test su prodotti: registrare esplicitamente la nuova riga sia sul `DbSet` sia sulla collezione di navigazione del genitore già tracciato (fix del bug precedente) duplicava la riga in memoria, perché Entity Framework la collega già da solo alla collezione quando viene aggiunta al `DbSet`. Ora, per il genitore già tracciato con la collezione caricata, basta l'aggiunta al `DbSet` (visto in `ProductsController.ReplaceBillOfMaterial`/`ReplaceRouting`).

### Priorita 2: import e cataloghi

- Import Excel portato nel backend .NET (`POST /api/supplier-catalog/import-excel`), stessa logica del CSV già disponibile.
- Import PDF non ancora implementato: l'estrazione affidabile di tabelle da PDF richiede un formato di riferimento fisso (i cataloghi Schneider/Pizzato non ne hanno uno standardizzato); da valutare insieme a un esempio di file reale prima di investirci.
- Definire il formato ufficiale dei cataloghi Schneider e Pizzato.
- Deduplicazione per codice produttore.
- Storico versioni del catalogo.
- Ricerca esterna controllata quando il codice non è catalogato.
- Gestione manuale dei codici non riconosciuti.

### Priorita 3: client Windows

- Login: implementato (attualmente disattivato temporaneamente con auto-login di test, vedi `SkipLoginForTesting` in `MainWindow.xaml.cs`).
- Controllo release GitHub e auto-update da asset ZIP: implementato (nessuna release pubblicata ancora, quindi il controllo restituisce "nessuna release trovata").
- Interfaccia con chrome personalizzato (barra del titolo su misura) e sidebar di navigazione a icone al posto delle tab orizzontali.
- Dashboard a schede: materiali, sotto scorta, materiali mancanti, distinte, ordini fornitore, aree, utenti.
- Ricerca materiali: implementata; creazione materiale da UI implementata.
- Lista materiali mancanti e sottoscorte, con scan sottoscorte da UI: implementata.
- Distinte di prelievo: creazione, modifica (solo in bozza), apertura per consultazione (dettaglio righe), segna pronta, chiudi, annulla da UI implementati.
- Ordini fornitore: creazione, modifica (solo in bozza), elenco, conferma, ricezione residuo, annullamento da UI implementati.
- Fornitori: creazione da UI implementata (endpoint mancante fino a questa sessione).
- Aree e utenti: creazione da UI implementata (creazione utente richiede ora ruolo e password).
- Importazione catalogo Excel da UI implementata (pulsante "Importa catalogo Excel" nella scheda Materiali).
- Sessione con refresh token automatico in background (rinnovo silenzioso prima della scadenza dell'access token).
- Importazione distinta base da file Excel/CSV: implementata (pulsante "Importa da file..." in `ProductDetailWindow`, chiama `POST /api/products/{id}/bom/import`).
- Stampe ed esportazione PDF/Excel delle liste: ancora da fare.
- Configurazione URL API: implementata. Impostazioni persistite in `%LOCALAPPDATA%\CrmMes\settings.json` (classe `ClientSettings`), finestra `SettingsWindow` (pulsante "Impostazioni server" nel pannello di login e nella sidebar) con verifica connessione prima di salvare. L'auto-avvio dell'API locale (`EnsureLocalApiAsync`) ora scatta solo se l'indirizzo configurato è `localhost`, non quando punta a un server remoto.
- Gestione offline/connessione assente: gestione base implementata — `RunBusyAsync` intercetta `HttpRequestException` e mostra un messaggio dedicato con l'indirizzo del server configurato, invece dell'errore generico; non è una coda di sincronizzazione offline (il client resta comunque online-only).
- Prodotti (distinta base e ciclo di lavoro) e Commesse: schede da UI implementate. Prodotti: creazione, modifica nome/descrizione, disattivazione, editor a righe dinamiche per distinta base e ciclo di lavoro (`ProductDetailWindow`, sostituisce l'intera distinta/ciclo ad ogni salvataggio, coerente con l'endpoint "replace all" lato API), più import da file. Commesse: creazione, modifica (solo in bozza), rilascio, annullamento da elenco; dettaglio commessa (`WorkOrderDetailWindow`) con avvio/completamento di ogni fase, completamento dell'intera commessa e generazione della distinta di prelievo dalla distinta base. Verificato end-to-end contro l'API reale (non solo i test): creazione prodotto, distinta base, ciclo di lavoro, commessa, rilascio, avanzamento fasi, completamento, generazione distinta con quantità scalate correttamente, e blocco della generazione su commessa completata.
- Lotti materiali e cruscotto: schede da UI implementate. "Lotti materiali": elenco, carico manuale (`CreateMaterialLotWindow`), dettaglio con tracciabilità in avanti (`MaterialLotDetailWindow`, mostra quali distinte hanno consumato il lotto). "Cruscotto": KPI del periodo (commesse per stato, fasi completate, performance media, consegne puntuali). Nel dettaglio commessa: numero di lotto prodotto in intestazione, colonne minuti effettivi/performance per fase, pulsante "Tracciabilità materiali" (lotti consumati dalla commessa), e al rilascio con materiali insufficienti una finestra di conferma per procedere comunque (`force=true` lato API).
- Centri di lavoro: scheda da UI implementata. Elenco centri registrati con creazione/disattivazione inline (codice, nome, capacità minuti/giorno), e tabella "carico" (minuti in attesa, operazioni aperte, giorni di arretrato indicativo) sotto, incluse le voci non censite usate solo come testo libero sul ciclo di lavoro.

### Priorita 4: produzione

- Hosting scelto: **Render.com, piano Free** (nessun costo; l'API "si spegne" dopo ~15 minuti di inattività e la prima richiesta dopo una pausa impiega 30-60 secondi per risvegliarsi — cold start, non un problema di affidabilità). Preparato `render.yaml` alla radice del repo (Blueprint): basta creare un account Render, collegare questo repository GitHub, e Render costruisce `CrmMes.Api/Dockerfile` automaticamente. I due secret (`NEON_DATABASE_URL`, `CRM_MES_JWT_KEY`) vanno impostati a mano nella dashboard Render (il blueprint li dichiara ma non li valorizza).
- `CrmMes.Api/Dockerfile` reso portabile: la porta di ascolto ora legge `$PORT` (con fallback a 8080 per l'uso locale via `docker-compose.yml`), perché Render assegna la porta dinamicamente. Non ancora testato con una build Docker reale in questo ambiente (Docker non installato qui); la build va verificata al primo deploy reale su Render.
- Il client desktop deve puntare all'URL pubblico una volta noto: usare "Impostazioni server" nel client (vedi Priorità 3) per configurarlo, nessuna modifica di codice necessaria.
- HTTPS e dominio: Render fornisce automaticamente un sottodominio HTTPS (`*.onrender.com`) per il piano Free; un dominio personalizzato richiede un piano a pagamento.
- Gestione segreti nel provider di hosting: da fare al momento del deploy (dashboard Render).
- Backup e monitoraggio Neon.
- Logging centralizzato: richieste HTTP già loggate (`UseHttpLogging`), manca un sink esterno (es. aggregatore log) per la produzione.
- Pipeline CI/CD: build + test automatici su GitHub Actions implementati (`.github/workflows/build.yml`); Render può fare auto-deploy ad ogni push su `main` una volta collegato il repository (impostazione nella sua dashboard, non nel codice).
- Test automatici API e integrazione database: implementati, 81 test su database Sqlite in-memory isolato (mai contro Neon).
- Installer Windows e aggiornamenti dell’applicazione.
- Pubblicazione release con asset `CrmMes.Desktop-win-x64.zip`.

## Stato sintetico

| Area | Stato | Note |
|---|---|---|
| Architettura backend | Completata | ASP.NET Core + Neon |
| Database | Operativo | Migrazione iniziale applicata |
| Modelli dati | Base completata | Mancano regole e vincoli avanzati |
| API materiali | Operativa | CRUD iniziale |
| API utenti/aree | Operativa con policy, bootstrap Admin e creazione utenti con ruolo/password | Manca controllo area sulle distinte per utenti non elevati in fase di lista |
| API distinte | Operativa, con audit log e modifica righe (solo in bozza) | Nessuna lacuna nota |
| Ordini fornitori | Draft, Confirmed, PartiallyReceived, Received, Cancelled, con modifica righe (solo in bozza) | Testato end-to-end su Neon |
| Sottoscorte / ordine minimo | Operativa | Rilevamento automatico alla chiusura distinta + scan manuale, testato su Neon e richiamabile dal client Windows |
| Cataloghi fornitori | CSV ed Excel operativi, ricerca operativa | Manca import PDF e connettori ufficiali Schneider/Pizzato |
| Import documenti | Nel prototipo Node | Da portare nel backend reale |
| Autenticazione | JWT, policy, bootstrap Admin e refresh token operativi | Nessuna lacuna nota |
| Client Windows | Dashboard a schede con creazione/modifica/gestione per materiali, distinte, ordini fornitore, fornitori, aree, utenti, prodotti, commesse, lotti materiali, cruscotto, centri di lavoro, import catalogo Excel, import distinta base da file, URL API configurabile | Mancano stampe/export PDF-Excel delle liste, gestione offline vera (coda di sincronizzazione) |
| Auto-update | Implementato | Richiede release GitHub con asset previsto |
| Nucleo produzione (MES) | Backend e client operativi: prodotti, distinta base (incl. import da file), ciclo di lavoro, commesse con fasi tracciate, generazione distinta di prelievo da commessa, tracciabilità lotti materiali (FIFO, genealogia in entrambe le direzioni), verifica disponibilità materiali con rilascio forzabile, performance per fase, cruscotto KPI, centri di lavoro con carico indicativo rispetto alla capacità; verificato end-to-end contro l'API reale su Neon | Manca tracciabilità a livello di singola matricola (solo lotto per l'intera commessa), pianificazione a calendario vera (il carico centri di lavoro è indicativo, non schedula per data), qualità/NCM, OEE completo (servono fermi macchina e scarti/qualità, il cruscotto attuale copre solo la componente Performance) |
| Test automatici | 81 test di integrazione API (xUnit, Sqlite in-memory) | Manca copertura sul client WPF |
| CI | Build + test su GitHub Actions ad ogni push/PR | Manca deploy automatico (Render può farlo una volta collegato il repo) |
| Deploy produzione | Provider scelto (Render.com, piano Free) e `render.yaml` pronto | Nessun deploy ancora eseguito: serve creare l'account Render e collegare il repository (azione dell'utente, non eseguibile da qui) |

## Prossimo incremento consigliato

1. Creare un account Render.com (piano Free) e collegare questo repository per il primo deploy reale dell'API, seguendo `render.yaml`; poi puntare il client all'URL pubblico ottenuto tramite "Impostazioni server".
2. Riattivare il login manuale nel client (`SkipLoginForTesting = false` in `MainWindow.xaml.cs`) prima di qualsiasi uso reale/condiviso dell'app.
3. Valutare l'import PDF con un esempio reale di catalogo fornitore, per definire un formato di riferimento prima di implementarlo.
4. Aggiungere test automatici anche sul client WPF, e più copertura sui casi limite dell'API (es. concorrenza su chiusura distinta/ricezione ordine).
5. Stampe ed esportazione PDF/Excel delle liste dal client, gestione offline vera (coda di sincronizzazione, oggi c'è solo un messaggio d'errore chiaro quando il server non è raggiungibile).
6. Funzionalità MES ancora fuori scope: pianificazione a calendario vera per i centri di lavoro (oggi solo un confronto carico/capacità indicativo, senza date), modulo qualità/NCM, OEE completo (servono fermi macchina con causali e scarti/qualità — il cruscotto attuale copre solo Performance), tracciabilità a livello di singola matricola oltre al lotto di commessa, rilevazione manodopera oltre ai timestamp di inizio/fine fase.
