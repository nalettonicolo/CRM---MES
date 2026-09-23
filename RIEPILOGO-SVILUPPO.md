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
- **Non conformità/scarti** (appena aggiunto): registrazione di difetti con descrizione libera, quantità scartata e note, collegata alla singola fase; registrabile sia a fase in corso sia dopo il completamento (un difetto può emergere al collaudo finale). Il cruscotto calcola la Qualità (scarti sulla quantità pianificata delle commesse completate nel periodo) e ora l'**OEE completo** (Disponibilità × Performance × Qualità).
- Cruscotto KPI: commesse per stato, fasi completate, performance media, puntualità, disponibilità, qualità, OEE.

### Hosting e distribuzione
- API in produzione su Render.com (piano Free), deploy automatico a ogni push su `main`, database Neon Postgres.
- Workflow di keep-alive (ping ogni 10 minuti) per ridurre il cold-start del piano gratuito.
- Auto-update del client Windows da release GitHub.

### Client Windows (WPF)
- Sidebar raggruppata per macro-aree (Magazzino, Acquisti, Produzione, Amministrazione), espandibile.
- Design system proprio ("Fusione"): nav scura + area contenuti chiara, tipografia editoriale, palette ridotta a un solo colore d'accento — sostituisce l'estetica SaaS generica iniziale dopo due round di revisione con l'utente.
- Impostazioni server riservate al solo Admin (richiesta di credenziali se un altro ruolo tenta di accedervi).
- Copertura CRUD completa per tutte le entità: materiali, sottoscorta, mancanti, distinte, ordini, aree, utenti, fornitori, prodotti, commesse, lotti materiali, centri di lavoro, fermi macchina, non conformità, cruscotto.
- Import distinta base da file e import catalogo fornitore da PDF direttamente da interfaccia.
- Esportazione Excel/PDF delle liste (al momento sulla scheda Materiali).
- 42 test automatici sul client (converter XAML, persistenza impostazioni).
- Bug corretto in questo giro: il pannello Cruscotto non aveva uno scroll verticale, quindi le nuove metriche (Disponibilità, Qualità, OEE) restavano tagliate fuori dalla card a finestra non massimizzata — ora la card scorre.
- URL del server configurabile (non più vincolato a `localhost`).

## Cosa manca

- **Login reale disattivato**: `SkipLoginForTesting = true` in `MainWindow.xaml.cs` bypassa l'autenticazione per velocizzare lo sviluppo — **da rimettere a `false` prima di un uso reale o condiviso**.
- **Barcode/QR e terminale da reparto (shop floor)**: non iniziato. L'avvio/completamento fase richiede oggi il client desktop completo, non un terminale semplificato per l'operatore in linea.
- **Qualità approssimata, non per pezzo**: lo scarto è registrato per fase e confrontato con la quantità pianificata dell'intera commessa (stesso tipo di semplificazione della tracciabilità lotti), non con un conteggio reale di pezzi buoni/scartati per singola unità prodotta.
- **Tracciabilità solo a livello di commessa**: un lotto per l'intera commessa, non per singola matricola/unità prodotta.
- **Import PDF cataloghi non validato nel mondo reale**: euristica generica (raggruppamento parole per riga/colonna), testata solo su PDF generati sinteticamente in fase di test — va riverificata al primo catalogo fornitore reale disponibile.
- **Distribuzione**: nessun installer Windows firmato digitalmente; solo auto-update da release GitHub.
- **Logging di produzione**: le richieste HTTP sono loggate ma manca un sink esterno (es. servizio di log centralizzato) oltre ai log locali di Render.
- **Nessuna modalità offline**: il client resta online-only, senza coda di sincronizzazione in caso di API irraggiungibile.

## Riferimento rapido gap di mercato

Dei quattro gap segnalati come "ancora aperti" rispetto ai MES di mercato (Katana, MRPeasy e simili):

| Gap | Stato |
|---|---|
| Pianificazione a calendario | ✅ Chiuso |
| Fermi macchina con causale | ✅ Chiuso |
| Qualità / non conformità (NCM) | ✅ Chiuso (approssimazione: scarto per fase su quantità pianificata, non per singolo pezzo) |
| Barcode/QR e terminale shop floor | ⬜ Da iniziare |

Con la chiusura di Disponibilità e Qualità, il cruscotto ora calcola anche l'**OEE completo** (Disponibilità × Performance × Qualità), non solo la sua componente Performance.
