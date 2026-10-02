# Nicolò MES

Gestionale di produzione (MES) con magazzino, acquisti, vendite e documenti. Configurabile per quadri elettrici, meccanica, macchine e impianti, alimentare, impiantistica.

La mappa del repository è in [PROJECT-MAP.md](PROJECT-MAP.md). Lo stato di moduli e gap è in [STATO-PROGETTO.md](STATO-PROGETTO.md).

## Direzione ufficiale

**API .NET 8 + PostgreSQL + client Windows + piattaforma web `/app/` + pagina tecnici `/tecnici/`.**

Il prototipo Node (`server.js`, `public/`, `data/store.json`) è solo un riferimento storico. Non è il prodotto e non è il database.

## Avvio

Soluzione Visual Studio / `dotnet` su `CrmMes.sln`. L'API serve anche la web. Il client Windows si collega all'URL configurato in Impostazioni server.

```bash
dotnet test CrmMes.sln
```

## Funzionalità (nucleo)

- anagrafiche, distinte di prelievo, lotti, sottoscorta
- prodotti, cicli, commesse, terminale di reparto
- acquisti, cataloghi, **listini Metel** (modulo)
- preventivi, DDT, fattura elettronica (moduli)
- qualità, manutenzione, cantiere, collaudo CE, service, energia (moduli di settore)

## Login

In produzione si accede sempre con email e password (e 2FA se obbligatoria). Nelle build Debug il PC di sviluppo può usare le variabili d'ambiente `CRMMES_DEV_EMAIL` / `CRMMES_DEV_PASSWORD` per un accesso automatico locale: le credenziali non sono nel codice.
