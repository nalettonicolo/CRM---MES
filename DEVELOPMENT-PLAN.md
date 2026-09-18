# Piano d'attacco sviluppo

Aggiornato: 2026-09-18

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

### Fase 6 - Produzione

Obiettivo: pubblicare il sistema in modo gestibile.

- Hosting API con HTTPS (HSTS già attivo lato codice, manca hosting/dominio reale).
- Segreti fuori dal repository.
- Backup e monitoraggio Neon.
- Logging centralizzato: richieste HTTP loggate (`UseHttpLogging`); manca un sink esterno per la produzione.
- Test automatici implementati (38 test di integrazione API); CI implementata (build + test su GitHub Actions ad ogni push/PR); manca ancora il deploy automatico (richiede una decisione sul provider di hosting).
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

Fondamenta enterprise completate: bootstrap Admin, refresh token, audit log esteso alle distinte, gestione errori centralizzata, logging HTTP, modifica righe per distinte/ordini in bozza, import catalogo Excel, 38 test di integrazione automatici e pipeline CI su GitHub Actions. Il client WPF copre creazione, modifica e gestione di stato per le entità principali. Prossimi passi: riattivare il login manuale prima di un uso reale, decidere il provider di hosting per preparare un deploy di produzione reale, valutare l'import PDF con un esempio concreto di catalogo fornitore.
