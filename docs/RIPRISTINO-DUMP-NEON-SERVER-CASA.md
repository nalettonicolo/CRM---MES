# Ripristino di un dump di Neon sul server di casa

Guida passo passo per: **verificare** un dump di Neon, **ripristinarlo in un database NUOVO** sul server di casa e **avviare l'API** su quel database. I database `mes` e `mes_copia` non vengono mai toccati.

> Nota: questa guida non è stata provata su un server. I comandi vanno lanciati un passo alla volta, controllando il risultato prima di passare al successivo. Le parti tra `<...>` sono da sostituire con i valori reali (non sono nel repository).

## 0. Cosa c'è nel repository (e cosa no)

- `DISASTER-RECOVERY.md`: come nasce il dump (`.github/workflows/backup.yml`, ogni notte, formato `custom`, `--no-owner --no-privileges`, nome `crmmes-backup-AAAAMMGG-HHMMSS.dump`).
- `docker-compose.yml` (radice): avvia **solo l'API** e la punta a un database esterno tramite `NEON_DATABASE_URL`. Non crea nessun database. È quello usato in questa guida.
- `server/docker/docker-compose.yml`: avvia API **più un Postgres proprio** (`nicolomes`) e il backup notturno. **Non** serve qui: creerebbe un database separato invece di usare quello di casa.
- Una "variante" del compose specifica per il server di casa **non c'è nel repository**: si usa il compose della radice, cambiando solo il file `.env` (passo 4).

## 1. Verificare il dump scaricato

Scarica l'artefatto dalla run "Backup database" (scheda Actions su GitHub; resta 90 giorni). Se è uno `.zip`, estrailo in una cartella vuota: il dump è il file `.dump` dentro.

### 1.1 Dimensione

```
ls -l crmmes-backup-*.dump
```

- Un dump vuoto o troncato è molto più piccolo del solito: confrontalo con i dump precedenti.
- Se la dimensione è molto diversa da quella dei giorni prima, non usarlo prima di aver capito perché.

### 1.2 Completezza

```
pg_restore --list crmmes-backup-AAAAMMGG-HHMMSS.dump > elenco.txt
grep -c "TABLE DATA" elenco.txt
```

- Il comando `--list` legge tutto l'indice del file: se il file è troncato o corrotto dà errore. È lo stesso controllo del workflow di backup.
- `TABLE DATA` deve comparire una volta per ogni tabella con dati. Il numero deve coincidere con le tabelle di Neon (stessa query del passo 1.4).
- Versione di `pg_restore`: il workflow usa il client **PostgreSQL 18**. Un `pg_restore` più vecchio può rifiutare il file con errore di versione. Controlla con `pg_restore --version` sul server di casa; se è più vecchio del 18, installa il client 18 prima di continuare.

### 1.3 Checksum e copia in un secondo posto

```
sha256sum crmmes-backup-AAAAMMGG-HHMMSS.dump > crmmes-backup-AAAAMMGG-HHMMSS.dump.sha256
```

Copia **dump e file `.sha256`** in un secondo posto (altro disco, NAS o altra macchina), poi controlla la copia:

```
cd <cartella-della-copia>
sha256sum -c crmmes-backup-AAAAMMGG-HHMMSS.dump.sha256
```

Deve rispondere `OK`. Se risponde `FAILED`, la copia è danneggiata: rifalla.

### 1.4 Righe per tabella su Neon (da confrontare dopo il ripristino)

Collegati a Neon in sola lettura e lancia questa SELECT, salvando il risultato in un file:

```sql
SELECT table_name,
       (xpath('/row/c/text()',
              query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text::int AS righe
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;
```

Meglio farlo subito dopo aver preso il dump: se Neon continua a scrivere, i numeri cambiano e le differenze piccole sono normali.

## 2. Ripristinare in un database NUOVO

Regole che proteggono `mes` e `mes_copia`:

- Il nome del database nuovo è **diverso** da `mes` e `mes_copia` (qui: `mes_ripristino`).
- Non si usa mai `--clean` e mai `--create` (potrebbero cancellare o ricreare database esistenti).
- Ogni comando indica **esplicitamente** `-d mes_ripristino`.

### 2.1 Controllo prima di creare

```
psql -h <host-server-casa> -U <utente-admin> -d postgres -c "\l"
```

Verifica che `mes_ripristino` **non** esista già. Se esiste, scegli un altro nome (per esempio `mes_ripristino_2`): non cancellarlo.

### 2.2 Creazione e ripristino

```
psql -h <host-server-casa> -U <utente-admin> -d postgres -c "CREATE DATABASE mes_ripristino;"

pg_restore -h <host-server-casa> -U <utente-admin> -d mes_ripristino \
  --no-owner --no-privileges --exit-on-error \
  crmmes-backup-AAAAMMGG-HHMMSS.dump
```

- `--no-owner --no-privileges`: come il dump, niente proprietari o permessi di Neon.
- `--exit-on-error`: si ferma al primo errore invece di lasciare un database a metà.
- Se si ferma: lascia `mes_ripristino` com'è, leggi l'errore e **non** rilanciare sopra lo stesso database. Per riprovare usa un altro nome.

### 2.3 Verifica del risultato

1. Rilancia la SELECT del passo 1.4 **su `mes_ripristino`** e confronta riga per riga con il file salvato.
2. Controlla che `mes` e `mes_copia` siano ancora nell'elenco (`\l`) e non siano cambiati.

## 3. Chiavi e password

### 3.1 CRM_MES_JWT_KEY (chiave di firma degli accessi)

Dove la legge il codice (`CrmMes.Api/Program.cs`, righe 96-103): prima `Jwt:Key`, poi la variabile `CRM_MES_JWT_KEY`. Se manca (fuori da Development) o ha **meno di 32 caratteri**, l'API non parte e dà l'errore "Configurare Jwt:Key o CRM_MES_JWT_KEY con almeno 32 caratteri".

- Generala con (come in `server/docker/.env.example`): `openssl rand -base64 48`
- Mettila **solo** nel file `.env`, mai nel repository né per email.
- Se cambi la chiave, tutti gli accessi già fatti non valgono più e gli utenti devono rientrare. Le password degli utenti non sono legate alla chiave.
- Conserva la chiave in un posto sicuro, separato dal dump.

### 3.2 Password del database

Per il database di casa serve un utente **dedicato a `mes_ripristino`** con una password sua, non quella di un altro database. La password non va nel repository.

- Generala con `openssl rand -base64 32`, evitando i caratteri `;` e `"` (rompono la stringa di connessione: lo dice anche `server/docker/.env.example`).
- Le password degli utenti dell'applicazione sono nel database ripristinato: restano quelle di prima.

## 4. Avviare l'API con il compose della radice

Il compose della radice legge due variabili dal file `.env` (stessa cartella del compose):

| Variabile | Cosa metterci |
|---|---|
| `NEON_DATABASE_URL` | stringa di connessione del database **nuovo** (nome storico, ma vale per qualunque Postgres) |
| `CRM_MES_JWT_KEY` | la chiave del passo 3.1 |

Esempio di `.env` (modello in `.env.example`):

```
NEON_DATABASE_URL=Host=<indirizzo-server-casa>;Port=5432;Database=mes_ripristino;Username=<utente-dedicato>;Password=<password>
CRM_MES_JWT_KEY=<chiave-da-almeno-32-caratteri>
```

Punti da controllare:

- `Host` non può essere `localhost`: dentro il contenitore `localhost` è il contenitore stesso. Usa l'indirizzo del server di casa raggiungibile dal contenitore.
- Il server Postgres deve accettare connessioni da quell'indirizzo (configurazione del server: non è nel repository).
- Senza `SSL Mode=Require` la connessione è in chiaro: va bene solo in rete di casa. L'esempio di `.env.example` ha `SSL Mode=Require` per Neon: toglilo se il server di casa non usa SSL.
- Il codice (`Program.cs`, righe 203-205) sceglie la stringa in quest'ordine: `NEON_DATABASE_URL`, poi `DATABASE_URL`, poi `ConnectionStrings:DefaultConnection`. Se nel terminale hai già una `NEON_DATABASE_URL` o `DATABASE_URL` che punta a Neon, controlla che non vinca sul file `.env`: `docker compose config` mostra i valori che userà.
- Se nessuna è impostata, l'API si ferma con "Database non configurato. Imposta NEON_DATABASE_URL oppure ConnectionStrings:DefaultConnection.".

Avvio, dalla cartella principale del progetto:

```
docker compose config          # controlla i valori (la password compare in chiaro: non incollarlo in chat)
docker compose up --build
```

Il compose della radice **non** attiva `Database__AutoMigrate`, quindi non modifica il database all'avvio. Se il dump è più vecchio del codice, mancheranno delle migrazioni: l'API parte ma può dare errori. In quel caso scrivimelo e valuteremo come applicarle (l'opzione `--migrate` esiste in `Program.cs`, riga 224, ma il compose della radice non la usa).

## 5. Come provarlo

1. In un altro terminale: `curl http://<server>:8080/ping` deve rispondere `{"status":"ok"}` (non usa il database).
2. `curl http://<server>:8080/health` deve rispondere `"status":"healthy"` (usa il database).
3. Apri `http://<server>:8080/app/` e prova ad accedere con un utente esistente.
4. Controlla che nei log del contenitore non ci siano errori di connessione.
5. Per fermare: `docker compose down` (non tocca il database).

## Rischi da tenere presenti

- Il dump è una fotografia: i dati scritti su Neon dopo non ci sono.
- Il `.env` contiene segreti: è ignorato da git, ma non copiarlo in giro.
- Un utente Postgres con troppi permessi potrebbe toccare altri database: usa un utente che ha accesso **solo** a `mes_ripristino`.
