# Nicolò MES sul server dell'azienda

Il gestionale può girare in cloud (il servizio già attivo) oppure su un server dell'azienda cliente. Sul server
girano l'API, la piattaforma web (`/app/`) e la pagina dei tecnici (`/tecnici/`); i PC usano il programma
desktop collegato all'indirizzo del server.

Due modi di installarlo:

| | Windows Server (consigliato per chi ha già un server Windows) | Docker (server Linux o Windows con Docker) |
|---|---|---|
| Cosa serve | Windows Server 2016 o più recente (o Windows 10/11 Pro), PostgreSQL 16 o 17 | Docker con Docker Compose |
| Database | PostgreSQL installato sul server (o un PostgreSQL esistente) | Incluso, in un contenitore |
| Installazione | `NicoloMES-Server-Setup.exe` | `docker compose up -d --build` |
| Backup | Ogni notte alle 02:00, 30 giorni | Ogni notte alle 02:00, 30 giorni |
| Aggiornamento | Si riesegue il nuovo `NicoloMES-Server-Setup.exe` | `git pull` e di nuovo `docker compose up -d --build` |

## Windows Server

1. **Installa PostgreSQL** (una volta sola): da https://www.postgresql.org/download/windows/ scarica
   l'installer ufficiale (EDB) della versione 16 o 17. Durante l'installazione scegli e conserva la password
   dell'utente `postgres`: serve solo alla prima configurazione di Nicolò MES.
2. **Esegui `NicoloMES-Server-Setup.exe`** come amministratore (si trova tra i file della release). Copia il
   programma in `C:\Program Files\Nicolo MES Server` e apre la configurazione, che:
   - crea l'utente e il database `nicolomes` con una password generata che nessuno vede;
   - genera la chiave di firma degli accessi;
   - salva tutto in `C:\ProgramData\NicoloMES\server.json`, leggibile solo da amministratori e dal servizio;
   - crea il servizio di Windows **NicoloMES** (si avvia con Windows e si riavvia da solo se si ferma);
   - apre la porta 5092 nel firewall solo per la rete aziendale (non per le reti pubbliche);
   - aggiorna il database e avvia il servizio;
   - pianifica il backup notturno.
3. **Collega i PC**: nell'installer del programma desktop scegli "Server dell'azienda" e scrivi l'indirizzo
   mostrato alla fine della configurazione, ad esempio `http://SERVER-MES:5092/`.
4. **Primo accesso**: dal programma desktop o da `http://SERVER-MES:5092/app/` crea l'amministratore.

Nel menu Start, gruppo "Nicolò MES server": **Configura** (riesegue la configurazione, per esempio dopo aver
cambiato server del database), **Backup adesso**, **Cartella dati e log**, **Istruzioni**.

### Dove sono le cose

| Cosa | Dove |
|---|---|
| Programma | `C:\Program Files\Nicolo MES Server` |
| Configurazione (database, chiave, contatti assistenza) | `C:\ProgramData\NicoloMES\server.json` |
| Log del gestionale (uno al giorno, 30 giorni) | `C:\ProgramData\NicoloMES\logs\api-AAAAMMGG.log` |
| Esito dei backup | `C:\ProgramData\NicoloMES\logs\backup.log` |
| Backup | `C:\ProgramData\NicoloMES\backup` (e la copia in `Backup:CopyTo`, se impostata) |

### Copia dei backup su un NAS

In `server.json`, sezione `Backup`, imposta `CopyTo` con un percorso di rete raggiungibile dall'account SYSTEM
del server (ad esempio `\\\\NAS\\backup\\nicolomes`). Il backup viene copiato lì ogni notte e i file più vecchi
di `RetentionDays` giorni vengono eliminati in entrambe le posizioni.

### Ripristino di un backup

```
pg_restore --clean --if-exists -h localhost -U nicolomes -d nicolomes C:\ProgramData\NicoloMES\backup\nicolomes-AAAAMMGG-HHMMSS.dump
```

Prima ferma il servizio (`Stop-Service NicoloMES`), poi riavvialo (`Start-Service NicoloMES`). Conviene provare
il ripristino su un database di prova almeno una volta, prima di averne bisogno.

### Aggiornamento

Esegui il nuovo `NicoloMES-Server-Setup.exe`: ferma il servizio, sostituisce il programma, aggiorna il database
e lo riavvia. Configurazione, dati e backup restano. I PC con il programma desktop si aggiornano da soli.

### Disinstallazione

Da "App installate" di Windows. Vengono rimossi servizio, regola del firewall e backup pianificato; **i dati
restano** (database `nicolomes` e `C:\ProgramData\NicoloMES`), così una reinstallazione riparte da dove era.

## Docker

1. Copia `server/docker/.env.example` in `server/docker/.env` e compila `DB_PASSWORD` e `JWT_KEY` (i comandi per
   generarle sono nel file). Facoltativi: porta, cartella dei backup, contatti di assistenza.
2. Dalla cartella principale del progetto:
   ```
   docker compose -f server/docker/docker-compose.yml up -d --build
   ```
3. Il database si crea e si aggiorna da solo all'avvio. Il gestionale risponde sulla porta indicata (5092).

Aggiornare: `git pull` e di nuovo lo stesso comando. I dati stanno nel volume `db-data`, i backup nella
cartella `BACKUP_DIR`.

## Sicurezza

- In rete aziendale la connessione è in chiaro (http). Per accessi **da fuori** non aprire la porta su
  internet: usa una **VPN** (per esempio quella del firewall aziendale, o WireGuard/Tailscale), oppure metti
  davanti un proxy con certificato (https).
- Per usare direttamente un certificato, in `server.json` sostituisci l'endpoint `Http` con:
  ```json
  "Https": { "Url": "https://0.0.0.0:5093", "Certificate": { "Path": "C:\\ProgramData\\NicoloMES\\cert.pfx", "Password": "..." } }
  ```
  e apri la porta 5093 nel firewall.
- `server.json` contiene la password del database e la chiave di firma: non copiarlo fuori dal server.
- La verifica in due passaggi si può rendere obbligatoria per i ruoli che vedono dati riservati
  (Amministrazione → Canali di accesso).

## Teleassistenza

Il programma desktop ha la finestra **Teleassistenza** (anche dalla schermata di accesso, senza credenziali):
mostra i contatti di assistenza impostati nella configurazione, avvia la sessione remota con RustDesk e crea un
**pacchetto diagnostico** da inviare all'assistenza (versioni, stato del server, errori recenti; nessuna
password). Per l'assistenza al server stesso, installa RustDesk sul server o usa la VPN aziendale.
