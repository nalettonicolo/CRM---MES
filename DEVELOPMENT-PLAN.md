# Piano d'attacco sviluppo

Aggiornato: 2026-09-22

## Obiettivo

Portare il prototipo a un gestionale Windows utilizzabile in azienda, con API .NET, database Neon PostgreSQL, cataloghi fornitori e flussi tracciati.

## Ordine di esecuzione

### Fase 1 - Fondamenta sicure

Obiettivo: rendere affidabile l'accesso ai dati prima di ampliare le schermate.

- JWT con ruoli standard: `Admin`, `Warehouse`, `Purchasing`, `Operator`.
- Policy per operazioni amministrative, magazzino e acquisti.
- Bootstrap del primo utente Admin implementato: la registrazione pubblica assegna `Admin` al primissimo utente mai creato sul database, `Operator` ai successivi. Un Admin può poi creare utenti con ruolo e password specifici via `POST /api/users`.
- Associazione utenti-aree e controllo accessi alle distinte.
- DTO coerenti sugli endpoint principali; alcune risposte anagrafiche devono ancora essere uniformate.
- Audit log per creazione utenti, distinte (creazione/pronta/chiusura/annullamento), ordini di acquisto (creazione/conferma/ricezione/annullamento) e scan sottoscorte; manca ancora per il login.
- Gestione errori non previsti centralizzata (ProblemDetails RFC 9110), con dettaglio dell'eccezione visibile solo in ambiente Development.
- Test automatici di integrazione aggiunti: 38 test xUnit in `CrmMes.Api.Tests` (autenticazione, refresh token, bootstrap Admin, policy per ruolo, ciclo distinte con modifica, sottoscorte, ciclo ordini con modifica, import catalogo CSV/Excel), eseguiti contro un database Sqlite in-memory isolato tramite `WebApplicationFactory`, mai contro Neon. Eseguibili con `dotnet test`, integrati nella pipeline CI.
- Refresh token implementato: access token ridotto a 30 minuti, refresh token di 30 giorni con rotazione a ogni utilizzo (`POST /api/auth/refresh`) e revoca su logout (`POST /api/auth/logout`).

Risultato: ogni operazione ha un utente riconoscibile e un permesso verificabile.

### Fase 2 - Flusso distinta completo

Obiettivo: coprire il lavoro quotidiano del magazzino.

- Creazione e modifica (solo in stato bozza) della distinta implementate, con audit log.
- Verifica catalogo e disponibilita stock.
- Stati: `Draft`, `Ready`, `Closed`, `Cancelled`.
- Chiusura transazionale con scarico stock.
- Gestione materiali mancanti presente come coda; riapertura controllata ancora da implementare.
- Sottoscorte per ordine minimo implementata: alla chiusura di una distinta, se lo scarico porta un materiale sotto `MinStock`, viene creato automaticamente un materiale mancante (`Source = MinStock`, quantita suggerita = minimo - giacenza) se non ne esiste gia uno aperto per lo stesso codice. Endpoint `GET /api/procurement/low-stock` per consultare i materiali sotto minimo e `POST /api/procurement/low-stock/scan` per generare le richieste anche per i casi non originati da un prelievo.
- Storico operazioni.

Risultato: una distinta passa dalla richiesta alla chiusura senza interventi manuali sul database.

### Fase 3 - Acquisti e fornitori

Obiettivo: trasformare i mancanti in un processo d'acquisto controllato.

- API fornitori e creazione ordine `Draft` implementate.
- Stati implementati: `Draft`, `Confirmed`, `PartiallyReceived`, `Received`, `Cancelled` (stato `Sent` omesso: non esiste ancora un'azione di invio effettivo al fornitore, verrà aggiunto quando servirà).
- Conferma ordine (`POST /api/procurement/purchase-orders/{id}/confirm`), ricezione anche parziale con aggiornamento stock transazionale (`POST .../receive`) e annullamento (`POST .../cancel`) implementati.
- Collegamento ordine-materiale mancante implementato: una riga ordine puo referenziare un `MissingMaterial`, che passa a `Ordered` alla creazione dell'ordine, `Resolved` alla ricezione completa della riga e torna `Open` se l'ordine viene annullato prima della ricezione.
- Testato end-to-end su Neon: creazione ordine, conferma, ricezione (anche parziale), annullamento, modifica righe (solo in bozza), collegamento a materiale mancante da sottoscorta.

Risultato: tracciabilita completa dal materiale mancante alla ricezione.

### Fase 4 - Cataloghi e documenti

Obiettivo: importare dati reali Schneider, Pizzato e altri fornitori.

- Import CSV ed Excel nel backend .NET (stessa logica di upsert condivisa); import PDF ancora da implementare, richiede prima un esempio reale di catalogo per definire un formato di riferimento (l'estrazione tabellare da PDF senza formato fisso è inaffidabile).
- Template catalogo versionato.
- Deduplicazione per codice e fornitore.
- Ricerca locale veloce.
- Ricerca esterna solo come fallback esplicito.
- Import distinta da PDF/foglio di calcolo.

Risultato: meno inserimento manuale e codici riconosciuti automaticamente.

### Fase 5 - Client Windows

Obiettivo: rendere i flussi utilizzabili dagli operatori.

- Login e sessione JWT collegati all'API (attualmente bypassato con auto-login di test per velocizzare lo sviluppo: `SkipLoginForTesting` in `MainWindow.xaml.cs`, da rimettere a `false` prima di un uso reale).
- Interfaccia con chrome personalizzato e sidebar di navigazione a icone (non più tab orizzontali).
- Dashboard implementata per materiali, sotto scorta, materiali mancanti, distinte, ordini fornitore, aree, utenti.
- Ricerca materiali e disponibilita presenti nella shell base.
- Operazioni di scrittura implementate da UI: creazione e modifica (solo in bozza) di distinta di prelievo e ordine fornitore; creazione materiale, area, utente (con ruolo e password), fornitore; azioni di stato su distinte (pronta/chiudi/annulla) e ordini (conferma/ricevi/annulla); apertura distinta per consultazione dettaglio righe; import catalogo Excel da UI.
- Sessione con rinnovo automatico del token in background (refresh token).
- Importazione distinta da file esterno.
- Esportazione PDF/Excel delle liste.
- Configurazione API e messaggi offline.

Risultato: applicazione WPF nativa pronta per il lavoro quotidiano.

### Fase 5bis - Nucleo produzione (MES)

Obiettivo: portare il gestionale al livello degli strumenti MES di mercato (ISA-95: ordini di produzione, distinta base, ciclo di lavoro), restando volutamente generico per settore invece di legarsi solo al quadro elettrico, così da poter essere personalizzato per qualunque tipo di manifattura.

- Anagrafica prodotto (`Product`): cosa si produce, con distinta base e ciclo di lavoro propri.
- Distinta base (`BillOfMaterialItem`): righe materiale+quantità collegate al prodotto, sullo stesso pattern per codice materiale già usato dalle distinte di prelievo; sostituzione completa validata contro il catalogo materiali attivo.
- Ciclo di lavoro (`RoutingStep`): fasi ordinate con centro di lavoro a testo libero (deliberatamente neutro, non specifico al settore elettrico) e minuti stimati; sostituzione completa con numerazione di sequenza automatica.
- Commesse (`WorkOrder`): job di produzione con stati `Draft` -> `Released` -> `InProgress` -> `Completed` (più `Cancelled`); alla creazione le fasi del ciclo di lavoro del prodotto vengono fotografate come `WorkOrderOperation` proprie della commessa, così che una modifica successiva al ciclo del prodotto non alteri retroattivamente una commessa già in corso.
- Avanzamento fasi (`start`/`complete` per operatore di reparto, qualunque ruolo autenticato): la prima fase avviata porta automaticamente la commessa a `InProgress`; il completamento della commessa richiede tutte le fasi `Done`.
- Generazione distinta di prelievo da commessa (`POST /api/work-orders/{id}/generate-withdrawal-slip`): scala le quantità della distinta base per la quantità di commessa, richiede un'area assegnata.
- 17 nuovi test di integrazione (prodotti + commesse), portando il totale a 55; bug scoperto e corretto scrivendo i test di sostituzione distinta base/ciclo di lavoro: la doppia registrazione (sul `DbSet` e sulla collezione di navigazione) duplicava le righe in memoria perché Entity Framework collega già da solo la nuova riga alla collezione del genitore tracciato.
- Migrazione `AddProductionCore` applicata a Neon.
- UI client WPF: scheda "Prodotti" (elenco, creazione, modifica nome/descrizione, disattivazione) con `ProductDetailWindow` per l'editor a righe dinamiche di distinta base e ciclo di lavoro (stesso pattern aggiungi/rimuovi riga già usato per le distinte di prelievo, applicato due volte nella stessa finestra); scheda "Commesse" (elenco, creazione, modifica in bozza, rilascio, annullamento) con `WorkOrderDetailWindow` per l'avanzamento delle singole fasi, il completamento della commessa e la generazione della distinta di prelievo.
- Verificato end-to-end contro l'API reale collegata a Neon (non solo i test automatici): creazione prodotto, distinta base, ciclo di lavoro, commessa con fasi fotografate correttamente, rilascio, avvio/completamento fasi con passaggio automatico a InProgress, completamento commessa, generazione distinta con quantità scalate per la quantità di commessa, e blocco della generazione su commessa già completata (409).

Risultato: il gestionale copre anche il cuore della produzione (cosa costruire, con cosa, in che fasi, chi sta lavorando su cosa), non solo magazzino e acquisti, con un client Windows che lo rende utilizzabile dagli operatori.

### Fase 5ter - Tracciabilità lotti, disponibilità materiali, performance

Obiettivo: chiudere i tre scostamenti più citati dalla ricerca di mercato sui MES per piccoli produttori (Katana, MRPeasy e simili): tracciabilità lotti, verifica disponibilità materiali prima del rilascio, avanzamento/OEE in tempo reale.

- Tracciabilità lotti materiali (`MaterialLot`, `MaterialLotConsumption`): un lotto per ogni ingresso di giacenza (ricezione ordine fornitore con numero lotto opzionale, giacenza iniziale dichiarata alla creazione del materiale, carico manuale per correzioni/campioni/sotto-assemblati); allo scarico di una distinta di prelievo il consumo avviene FIFO dal lotto più vecchio, registrando quanto è stato preso da ciascuno (best-effort: se il ledger dei lotti di un materiale non copre l'intera quantità, es. giacenza precedente all'introduzione della feature, il resto resta senza lotto attribuito — `Material.Stock` resta comunque l'unica fonte autorevole della giacenza). Il lato "prodotto finito" della tracciabilità è `WorkOrder.ProductLotNumber`, un numero di lotto per l'intera commessa (auto-generato se non specificato); tutte le unità di una stessa commessa condividono un lotto, la tracciabilità per singola matricola non è ancora stata costruita. Genealogia interrogabile in entrambe le direzioni: da un lotto materiale a cosa è stato consumato (`GET /api/material-lots/{id}`), da una commessa a quali lotti ha consumato (`GET /api/work-orders/{id}/material-lots`).
- Verifica disponibilità materiali (`GET /api/work-orders/{id}/material-check`): confronta la distinta base scalata per la quantità di commessa con la giacenza attuale, riga per riga. Il rilascio (`POST .../release`) esegue lo stesso controllo: se manca qualcosa risponde 409 con il dettaglio, a meno che il chiamante non passi `force=true`, nel qual caso rilascia comunque e lo annota nell'audit log — scelta deliberata di avvisare invece di bloccare rigidamente, per non irrigidire un flusso che in pratica a volte deve procedere comunque.
- Performance per fase (`WorkOrderOperation.ActualMinutes`/`PerformanceRatio`, calcolati al volo da `StartedAt`/`CompletedAt`/`EstimatedMinutes`, nessuno schema nuovo) e un cruscotto aggregato (`GET /api/work-orders/dashboard?days=`) con commesse per stato, fasi completate, performance media, commesse completate e percentuale di consegne puntuali sul periodo. Dichiaratamente **non** OEE completo: manca il tracciamento dei fermi macchina con causale (Availability) e degli scarti/qualità (Quality) — qui c'è solo la componente Performance (stimato/effettivo), più throughput e puntualità.
- 16 nuovi test di integrazione, portando il totale a 71. Bug scoperto scrivendo il test sul consumo FIFO: `OrderBy` prima di `GroupBy` non è garantito sopravvivere alla traduzione SQL + materializzazione in Entity Framework Core — l'ordinamento va rifatto lato client dopo aver materializzato la lista (`ToListAsync` poi `GroupBy`/`OrderBy` in memoria), non prima.
- Migrazione `AddMaterialLotTraceability` applicata a Neon.
- UI client WPF: nuove schede "Lotti materiali" (elenco, carico manuale con `CreateMaterialLotWindow`, dettaglio/genealogia con `MaterialLotDetailWindow`) e "Cruscotto" (le stesse metriche del dashboard API, come tessere). Nel dettaglio commessa: numero di lotto in intestazione, colonne minuti effettivi/performance per fase, pulsante "Tracciabilità materiali", e sul rilascio con materiali insufficienti una finestra di conferma per procedere comunque (che richiama l'API con `force=true`).
- Verificato end-to-end contro l'API reale su Neon: lotto iniziale creato alla creazione materiale, verifica disponibilità che segnala correttamente la carenza, rilascio bloccato senza `force` e riuscito con `force=true`, calcolo performance su una fase completata, cruscotto che riflette lo stato reale del database.

Risultato: i tre gap più citati dalla ricerca di mercato sono chiusi a un primo livello utilizzabile. Restano fuori scope, esplicitamente rimandati: tracciabilità a livello di singola matricola, centri di lavoro con capacità e pianificazione vera, qualità/non conformità, OEE completo (fermi macchina + scarti), rilevazione manodopera oltre ai timestamp di inizio/fine fase.

### Fase 6 - Produzione

Obiettivo: pubblicare il sistema in modo gestibile.

- Hosting API con HTTPS (HSTS già attivo lato codice, manca hosting/dominio reale).
- Segreti fuori dal repository.
- Backup e monitoraggio Neon.
- Logging centralizzato: richieste HTTP loggate (`UseHttpLogging`); manca un sink esterno per la produzione.
- Test automatici implementati (71 test di integrazione API); CI implementata (build + test su GitHub Actions ad ogni push/PR); manca ancora il deploy automatico (richiede una decisione sul provider di hosting).
- Auto-update da release GitHub implementato.
- Installer Windows e firma digitale ancora da implementare.

Risultato: sistema distribuibile e manutenibile.

## Regola di avanzamento

Ogni incremento deve includere:

1. modifica minima nel modulo proprietario;
2. migrazione database se cambia lo schema;
3. test API o test di integrazione;
4. aggiornamento della mappa progetto;
5. build senza errori o warning.

## Prossimo incremento

Fondamenta enterprise completate: bootstrap Admin, refresh token, audit log esteso alle distinte, gestione errori centralizzata, logging HTTP, modifica righe per distinte/ordini in bozza, import catalogo Excel, pipeline CI su GitHub Actions. Nucleo produzione (MES) completo backend + client: prodotti, distinta base, ciclo di lavoro, commesse con fasi tracciate, generazione distinta di prelievo da commessa, tracciabilità lotti materiali con genealogia bidirezionale, verifica disponibilità materiali con rilascio forzabile, performance per fase e cruscotto KPI — generico per settore; 71 test di integrazione automatici più verifica end-to-end manuale contro l'API reale su Neon. Il client WPF ora copre creazione, modifica e gestione di stato per tutte le entità, incluse prodotti, commesse, lotti materiali e cruscotto. Prossimi passi: riattivare il login manuale prima di un uso reale, decidere il provider di hosting per preparare un deploy di produzione reale, valutare l'import PDF con un esempio concreto di catalogo fornitore, aggiungere test automatici sul client WPF; più avanti, se richiesto: centri di lavoro con capacità/pianificazione vera, qualità/NCM, OEE completo (fermi macchina + scarti), tracciabilità per singola matricola.
