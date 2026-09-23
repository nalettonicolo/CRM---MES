# Backup e disaster recovery

Stato del database di produzione (Neon, progetto "MES", piano Free) e procedura di ripristino testata il 2026-09-23.

## Livelli di backup attivi

1. **Point-in-time recovery nativo di Neon**: 6 ore di storico WAL (`history_retention_seconds: 21600`), sempre attivo, nessuna azione richiesta. Copre errori scoperti entro poche ore (es. una cancellazione sbagliata appena fatta).
2. **Dump logico giornaliero** (`.github/workflows/backup.yml`): ogni notte alle 03:00 UTC esegue `pg_dump` sul database di produzione e conserva il file come artifact di GitHub Actions per 90 giorni. È l'unico backup oltre la finestra di 6 ore, perché il piano Free di Neon **non supporta gli snapshot programmati** (verificato via API: `backup schedule creation is not enabled for this project`).
3. **Snapshot manuali on-demand**: disponibili anche sul piano Free (a differenza di quelli programmati). Utile prima di un'operazione rischiosa (es. una migrazione dati).

### Da fare per attivare il dump giornaliero

Aggiungere il secret del repository `NEON_DATABASE_URL` (Settings → Secrets and variables → Actions) con la stessa connection string usata da Render/dallo sviluppo locale (variabile `NEON_DATABASE_URL` o `DATABASE_URL`). Senza questo secret il workflow fallisce esplicitamente con un messaggio chiaro, invece di fallire in silenzio.

## Limite noto del piano Free

Con solo 6 ore di PITR e nessuno snapshot automatico, un problema scoperto dopo più di un giorno può essere recuperato solo dall'ultimo dump giornaliero (fino a 24 ore di dati persi nel caso peggiore). Passare a un piano Neon a pagamento estenderebbe il PITR fino a 30 giorni e abiliterebbe gli snapshot automatici — una decisione di costo, non tecnica.

## Procedura di ripristino

### Da un dump giornaliero (perdita di dati oltre le 6 ore, o corruzione)

1. Scaricare l'artifact più recente dalla run del workflow "Backup database" (tab Actions su GitHub).
2. Creare un nuovo branch Neon vuoto per il ripristino (**non ripristinare direttamente sul branch di produzione** finché il dump non è verificato).
3. `pg_restore --clean --if-exists -d "<connection string del branch di test>" crmmes-backup-YYYYMMDD-HHMMSS.dump`
4. Verificare i dati sul branch di test (conteggi righe, dati recenti) prima di promuoverlo.
5. Solo dopo la verifica, puntare `DATABASE_URL` (Render + sviluppo locale) al branch verificato, oppure ripristinare il dump sopra il branch di produzione stesso se si accetta l'interruzione.

### Da un errore recente (entro 6 ore) via point-in-time recovery Neon

1. Dashboard Neon → Branches → **Restore** sul branch di produzione, scegliendo il timestamp prima dell'errore.
2. Verificare i dati prima di confermare.

### Da uno snapshot manuale

1. `restore_snapshot` con `target_branch_id` **omesso** per crearne uno **nuovo** isolato (mai passare l'id del branch di produzione come target senza aver prima verificato i dati — vedi nota sotto).
2. Verificare i dati sul nuovo branch.
3. Solo dopo la verifica, promuoverlo o copiarne i dati sopra la produzione.

> **Nota operativa importante** (imparata il 2026-09-23 durante il primo test): `restore_snapshot` senza `target_branch_id`, con `finalize` lasciato al default, **non crea una copia isolata "di sola lettura"** — sposta l'endpoint di calcolo stabile (quello a cui si collegano Render e lo sviluppo locale) sul branch appena ripristinato e rinomina i branch, come se il ripristino fosse già stato promosso a produzione. Per un test di verifica che non deve toccare la produzione, va sempre passato esplicitamente `finalize: false`, oppure va verificato subito dopo quale branch porta il nome "production" e l'endpoint attivo, prima di considerare l'operazione "solo di prova".

## Test eseguito il 2026-09-23

Snapshot manuale creato dal branch di produzione, ripristinato su un branch separato, e confrontato con la produzione: utenti, commesse, materiali, corrieri e spedizioni combaciavano esattamente (6 utenti, 8 commesse, 8 materiali, 0/0 corrieri/spedizioni). Il meccanismo di ripristino funziona; il branch di test è stato poi eliminato.
