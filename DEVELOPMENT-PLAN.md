# Piano d'attacco sviluppo

Aggiornato: 2026-09-17

## Obiettivo

Portare il prototipo a un gestionale Windows utilizzabile in azienda, con API .NET, database Neon PostgreSQL, cataloghi fornitori e flussi tracciati.

## Ordine di esecuzione

### Fase 1 - Fondamenta sicure

Obiettivo: rendere affidabile l'accesso ai dati prima di ampliare le schermate.

- JWT con ruoli standard: `Admin`, `Warehouse`, `Purchasing`, `Operator`.
- Policy per operazioni amministrative, magazzino e acquisti.
- Associazione utenti-aree e controllo accessi alle distinte.
- DTO coerenti sugli endpoint principali; alcune risposte anagrafiche devono ancora essere uniformate.
- Audit log per login, modifiche anagrafiche, distinte e ordini.
- Test manuali verificati per `401`, ruoli e accesso autenticato; test automatici da aggiungere.

Risultato: ogni operazione ha un utente riconoscibile e un permesso verificabile.

### Fase 2 - Flusso distinta completo

Obiettivo: coprire il lavoro quotidiano del magazzino.

- Creazione distinta implementata; modifica ancora da implementare.
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
- Testato end-to-end su Neon: creazione ordine, conferma, ricezione (anche parziale), annullamento, collegamento a materiale mancante da sottoscorta.
- Da fare: modifica righe ordine dopo la creazione.

Risultato: tracciabilita completa dal materiale mancante alla ricezione.

### Fase 4 - Cataloghi e documenti

Obiettivo: importare dati reali Schneider, Pizzato e altri fornitori.

- Import CSV nel backend .NET; import Excel e PDF ancora da implementare.
- Template catalogo versionato.
- Deduplicazione per codice e fornitore.
- Ricerca locale veloce.
- Ricerca esterna solo come fallback esplicito.
- Import distinta da PDF/foglio di calcolo.

Risultato: meno inserimento manuale e codici riconosciuti automaticamente.

### Fase 5 - Client Windows

Obiettivo: rendere i flussi utilizzabili dagli operatori.

- Login e sessione JWT collegati all'API.
- Dashboard a schede implementata (materiali, sotto scorta, materiali mancanti, distinte, ordini fornitore, aree, utenti); layout professionale per ruolo ancora da rifinire.
- Ricerca materiali e disponibilita presenti nella shell base.
- Materiali mancanti, sottoscorte (con scan da UI) e ordini fornitore (elenco, conferma, ricezione, annullamento) implementati.
- Creazione distinta da UI ancora da implementare.
- Importazione file.
- Esportazione PDF/Excel.
- Configurazione API e messaggi offline.

Risultato: applicazione WPF nativa pronta per il lavoro quotidiano.

### Fase 6 - Produzione

Obiettivo: pubblicare il sistema in modo gestibile.

- Hosting API con HTTPS.
- Segreti fuori dal repository.
- Backup e monitoraggio Neon.
- Logging centralizzato.
- Test automatici e CI/CD.
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

Ciclo acquisti con conferma/ricezione e gestione sottoscorte per ordine minimo implementati e testati end-to-end su Neon (vedi Fase 3). Prossimo passo: portare le schermate materiali mancanti/sottoscorte e ordini fornitore sul client WPF, poi eseguire debugging strutturale e audit sicurezza, quindi rifare il layout WPF con dashboard e modali professionali.
