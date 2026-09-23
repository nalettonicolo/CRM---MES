# Riepilogo sviluppi e stato del progetto

Aggiornato: 2026-09-23

Sintesi ad alto livello di cosa è stato costruito finora e cosa manca ancora. Per il dettaglio fase-per-fase con motivazioni tecniche vedi [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md); per la mappa di file e architettura vedi [PROJECT-MAP.md](PROJECT-MAP.md).

## Cosa è stato fatto

### Fondamenta e sicurezza
- Autenticazione JWT con refresh token (rotazione a ogni utilizzo, revoca su logout).
- Ruoli: `Admin`, `Warehouse`, `Purchasing`, `Operator`; bootstrap automatico del primo utente come Admin.
- Audit log su utenti, distinte, ordini d'acquisto, sottoscorte.
- Gestione errori centralizzata (ProblemDetails), 98 test di integrazione API su database Sqlite in-memory isolato, CI su GitHub Actions (build + test a ogni push/PR).

### Magazzino e acquisti
- Anagrafica materiali, distinte di prelievo con stati (`Draft`→`Ready`→`Closed`/`Cancelled`), scarico transazionale.
- Sottoscorte automatiche per ordine minimo, materiali mancanti collegati agli ordini fornitore.
- Fornitori e ordini d'acquisto (`Draft`→`Confirmed`→ricezione anche parziale →`Received`/`Cancelled`).
- Import cataloghi fornitori da CSV, Excel **e PDF** (quest'ultimo euristico/best-effort: non è mai stato validato su un catalogo reale Schneider o Pizzato, solo su PDF sintetici di test).

### Produzione (MES)
- Prodotti con distinta base e ciclo di lavoro propri.
- Commesse (`Draft`→`Released`→`InProgress`→`Completed`/`Cancelled`) con fasi fotografate dal ciclo di lavoro al momento della creazione.
- Generazione automatica della distinta di prelievo da commessa.
- Tracciabilità lotti materiali con consumo FIFO e genealogia bidirezionale (lotto materiale ↔ commessa); il lotto è per l'intera commessa, non per singola matricola.
- Verifica disponibilità materiali prima del rilascio (bloccante di default, forzabile con conferma esplicita).
- Performance per fase (minuti stimati vs effettivi).
- Centri di lavoro con capacità e calcolo del carico/arretrato.
- Pianificazione a calendario delle fasi in base alla capacità del centro di lavoro.
- **Fermi macchina con causale**: un solo fermo aperto per fase, blocca il completamento della fase finché non viene chiuso; il cruscotto calcola la Disponibilità (componente OEE).
- **Non conformità/scarti**: registrazione di difetti con descrizione libera, quantità scartata e note, collegata alla singola fase; registrabile sia a fase in corso sia dopo il completamento (un difetto può emergere al collaudo finale). Il cruscotto calcola la Qualità (scarti sulla quantità pianificata delle commesse completate nel periodo) e l'**OEE completo** (Disponibilità × Performance × Qualità).
- **Barcode/QR e terminale di reparto**: ogni commessa ha un'etichetta QR stampabile (codice commessa codificato in un QR, esportabile in PDF dalla scheda commessa) da allegare al lotto fisico. Il "Terminale di reparto" è una finestra semplificata e a caratteri grandi pensata per l'operatore in linea: si scansiona (o digita) il codice — un lettore barcode/QR USB funziona già come tastiera, non serve integrazione hardware dedicata — e si vede subito la fase attiva della commessa, con un unico grande pulsante per avviarla/completarla e accesso rapido a "Segnala fermo"/"Segnala non conformità", senza dover navigare il client completo.
- **Identificazione operatore via PIN** (appena aggiunto): un Admin assegna a ogni utente un PIN numerico (4-8 cifre, hash separato dalla password di login) dalla scheda Utenti. Il Terminale di reparto richiede questo PIN prima di mostrare qualunque commessa — "chi ha fatto cosa" è ora tracciato: avvio/completamento fase, apertura/chiusura fermo e segnalazione non conformità registrano il nome dell'operatore identificato, visibile nello storico fermi/NC e nell'elenco fasi. Le azioni dal client d'ufficio restano senza operatore associato (nessun PIN richiesto lì).
- Cruscotto KPI: commesse per stato, fasi completate, performance media, puntualità, disponibilità, qualità, OEE.

### Hosting e distribuzione
- API in produzione su Render.com (piano Free), deploy automatico a ogni push su `main`, database Neon Postgres.
- Workflow di keep-alive (ping ogni 10 minuti) per ridurre il cold-start del piano gratuito.
- Auto-update del client Windows da release GitHub.
- **Logging strutturato** (appena aggiunto): l'API usa Serilog, log in formato JSON su console (una riga per richiesta HTTP con metodo/percorso/stato/durata, non più righe sparse dei diagnostici interni di ASP.NET Core) invece del testo semplice precedente. Spedizione opzionale a **Grafana Cloud** (Loki) per conservazione e ricerca oltre la finestra limitata dei log di Render: attiva da sola se sono impostate le variabili d'ambiente `LOKI_URL` (+ `LOKI_USER`/`LOKI_PASSWORD` se il datasource lo richiede) su Render; senza, l'app funziona comunque, solo senza conservazione a lungo termine.

### Client Windows (WPF)
- Sidebar raggruppata per macro-aree (Magazzino, Acquisti, Produzione, Amministrazione), espandibile.
- Design system proprio ("Fusione"): nav scura + area contenuti chiara, tipografia editoriale, palette ridotta a un solo colore d'accento — sostituisce l'estetica SaaS generica iniziale dopo due round di revisione con l'utente.
- Impostazioni server riservate al solo Admin (richiesta di credenziali se un altro ruolo tenta di accedervi).
- Copertura CRUD completa per tutte le entità: materiali, sottoscorta, mancanti, distinte, ordini, aree, utenti, fornitori, prodotti, commesse, lotti materiali, centri di lavoro, fermi macchina, non conformità, etichette QR, terminale di reparto, cruscotto.
- Import distinta base da file e import catalogo fornitore da PDF direttamente da interfaccia.
- Esportazione Excel/PDF delle liste (al momento sulla scheda Materiali) ed etichette QR commessa in PDF.
- **Coda offline per il Terminale di reparto** (appena aggiunto): se l'API non è raggiungibile, avvio/completamento fase vengono salvati in una coda locale (`%LOCALAPPDATA%\CrmMes\offline-queue.json`) invece di fallire, con la barra "⚠ N azioni in coda" e un pulsante "Sincronizza ora"; una sincronizzazione automatica ogni 30 secondi (più un tentativo all'apertura della finestra) le rispedisce all'API non appena torna raggiungibile, rispettando l'ordine in cui sono state messe in coda. L'infrastruttura (`OfflineActionQueue`) è generica — qualunque finestra può registrare un nuovo tipo di azione — ma oggi è collegata solo alle due azioni dirette del terminale; fermi macchina e non conformità (aperti dal terminale tramite le loro finestre dedicate) restano online-only per ora. Verificato end-to-end nell'app reale: azione messa in coda a API spenta, poi sincronizzata correttamente al suo riavvio, con l'operatore corretto attribuito.
- 48 test automatici sul client (converter XAML, persistenza impostazioni, coda offline).
- Bug corretti scoperti in verifica GUI reale: il pannello Cruscotto non aveva scroll verticale (le metriche Disponibilità/Qualità/OEE restavano tagliate fuori a finestra non massimizzata); la finestra "Etichetta QR" aveva un'altezza fissa troppo bassa, tagliando fuori (invisibili, non solo compressi) il nome prodotto e il lotto sotto il codice; il messaggio del Terminale di reparto per una commessa con tutte le fasi completate diceva erroneamente "potrebbe non essere ancora rilasciata" (condizione logicamente impossibile in quel ramo); la finestra "Nuova commessa" (preesistente, mai toccata prima d'ora) aveva lo stesso problema, tagliando fuori il campo lotto, gli errori e i pulsanti Crea/Annulla.
- **Correzione sistemica del dimensionamento finestre**: dato che lo stesso tipo di bug (altezza fissa insufficiente per il contenuto reale) si è ripresentato quattro volte in punti diversi dell'app, le finestre-modulo semplici (Nuova area, Nuovo materiale, Nuovo prodotto, Nuovo fornitore, Nuovo utente, Nuovo lotto materiale, Nuova commessa, Verifica amministratore, Impostazioni, PIN terminale) ora usano `SizeToContent="Height"` invece di un valore indovinato a mano: si adattano sempre al contenuto reale, eliminando l'intera classe di bug invece di correggerne un'istanza alla volta.
- URL del server configurabile (non più vincolato a `localhost`).

## Cosa manca

- **Login reale disattivato**: `SkipLoginForTesting = true` in `MainWindow.xaml.cs` bypassa l'autenticazione per velocizzare lo sviluppo — **da rimettere a `false` prima di un uso reale o condiviso** (previsto prima della pubblicazione).
- **Qualità approssimata, non per pezzo**: lo scarto è registrato per fase e confrontato con la quantità pianificata dell'intera commessa (stesso tipo di semplificazione della tracciabilità lotti), non con un conteggio reale di pezzi buoni/scartati per singola unità prodotta.
- **Tracciabilità solo a livello di commessa**: un lotto per l'intera commessa, non per singola matricola/unità prodotta.
- **Import PDF cataloghi non validato nel mondo reale**: euristica generica (raggruppamento parole per riga/colonna), testata solo su PDF generati sinteticamente in fase di test — va riverificata al primo catalogo fornitore reale disponibile.
- **Distribuzione**: nessun installer Windows firmato digitalmente — serve un certificato di firma del codice (a pagamento, da acquistare); senza, resta solo l'auto-update da release GitHub.
- **Grafana Cloud non ancora collegato**: il logging centralizzato è pronto lato codice, ma serve creare l'account gratuito e impostare `LOKI_URL`/`LOKI_USER`/`LOKI_PASSWORD` su Render per attivarlo davvero.
- **Modalità offline solo sul Terminale di reparto**: coperte le due azioni dirette (avvia/completa fase); fermi macchina, non conformità e l'intero client d'ufficio restano online-only. L'infrastruttura è pronta per estenderla, ma non è stata estesa oltre lo scope richiesto.

## Riferimento rapido gap di mercato

Dei quattro gap segnalati come "ancora aperti" rispetto ai MES di mercato (Katana, MRPeasy e simili), **tutti e quattro sono ora chiusi** a un primo livello utilizzabile:

| Gap | Stato |
|---|---|
| Pianificazione a calendario | ✅ Chiuso |
| Fermi macchina con causale | ✅ Chiuso |
| Qualità / non conformità (NCM) | ✅ Chiuso (approssimazione: scarto per fase su quantità pianificata, non per singolo pezzo) |
| Barcode/QR e terminale shop floor | ✅ Chiuso (con identificazione operatore via PIN) |

Con la chiusura di Disponibilità e Qualità, il cruscotto ora calcola anche l'**OEE completo** (Disponibilità × Performance × Qualità), non solo la sua componente Performance.
