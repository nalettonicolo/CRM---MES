# Gestionale Distinte di Prelievo

La mappa aggiornata di cio che e gia costruito e di cio che manca si trova in [PROJECT-MAP.md](PROJECT-MAP.md).

Sistema gestionale MVP per la gestione di distinte di prelievo, materiali mancanti, aree riservate agli utenti e conferma ordini fornitore.

## Funzionalità principali

- creazione di aree e utenti
- assegnazione di aree riservate per singolo utente
- lanci di distinta di prelievo per area
- inserimento di codici materiale e quantità
- caricamento di PDF della distinta esterna
- rilevamento automazione di codici mancanti
- generazione di alert per ufficio acquisti
- creazione e conferma di ordini fornitore
- dashboard di riepilogo e log eventi

## Avvio locale

```bash
npm install
npm start
```

Aprire il browser su http://localhost:3000

## Struttura logica

- utenti e aree: accessi e autorizzazioni
- distinte: prelievi attivi e chiusi
- materiali mancanti: codici non presenti in magazzino o non riconosciuti
- ordini fornitore: richieste con stato draft/confirmed
- regole: logica base per il trattamento dei materiali mancanti

## Nota

Questo è un MVP per dimostrare il flusso operativo e può essere esteso con autenticazione, database reale, gestione documentale e integrazione con ERP.
