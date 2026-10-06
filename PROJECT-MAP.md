# Mappa progetto — Nicolò MES

Aggiornata: 2026-10-06 (pulizia cartelle, vedi sezione in fondo).

Questa è la mappa di **cosa c'è nel repository oggi**, non lo storico dell'MVP 2025. Lo stato funzionale (moduli, gap, versioni) sta in [STATO-PROGETTO.md](STATO-PROGETTO.md); il diario tecnico in [RIEPILOGO-SVILUPPO.md](RIEPILOGO-SVILUPPO.md).

## Cos'è

**Nicolò MES** (soluzione `CrmMes.sln`) è un gestionale di produzione su commessa, con magazzino, acquisti, vendite e documenti. Nato per un quadrista, si configura per più settori. Nome commerciale: Nicolò MES.

**Direzione ufficiale:** API ASP.NET Core 8 + PostgreSQL (Neon o server del cliente) + client Windows WPF + piattaforma web Blazor (`/app/`) + pagina tecnici (`/tecnici/`). Non usare il prototipo Node come database o come prodotto.

## Architettura

```mermaid
flowchart LR
    WPF[Client Windows WPF]
    WEB[Piattaforma web /app/]
    MOB[/tecnici/ telefono]
    API[CrmMes.Api ASP.NET Core 8]
    CORE[CrmMes.Core modelli e DbContext]
    DB[(PostgreSQL)]
    CON[CrmMes.Console licenze]
    GW[Gateway macchine OPC UA / MQTT]

    WPF --> API
    WEB --> API
    MOB --> API
    CON -.->|heartbeat licenza| API
    GW --> API
    API --> CORE
    CORE --> DB
```

| Progetto | Ruolo |
|---|---|
| `CrmMes.Api` | API, migrazioni EF, pagina `/tecnici/`, host della web `/app/` |
| `CrmMes.Core` | Modelli e `ApplicationDbContext` |
| `CrmMes.Desktop` | Programma Windows (ufficio e terminale di reparto) |
| `CrmMes.Web` | Piattaforma web Blazor WASM |
| `CrmMes.Licensing` | Catalogo moduli vendibili (allineato a `Sectors` via test) |
| `CrmMes.Console` | Console fornitore (licenze, canoni, teleassistenza) |
| `*.Tests` | xUnit / bUnit |

## Client e stesso linguaggio

Tre superfici, **stessi nomi di modulo** (`Sectors` / `ModuleCatalog`: "Listini Metel", "Service post-vendita", …) e **stessi stati**:

| Stato | Testo |
|---|---|
| Caricamento | `Caricamento...` |
| Errore di lettura | `Impossibile caricare …` + `Riprova` |
| Permesso negato | `Non disponibile per il tuo ruolo` / `Il tuo ruolo non ha i permessi per questa operazione.` |
| Server assente | `Server non raggiungibile. Controlla la connessione e riprova.` |

Menu web allineato al desktop: **Vendite**, **Magazzino**, **Acquisti**, **Produzione**, **Spedizioni**. Token colore: zinc + un cobalto (`#2F5BDA`), numeri tabulari.

- **Desktop**: anagrafiche, documenti, terminale di reparto, planning, stampe.
- **Web `/app/`**: consultazione e avanzamento da ufficio/tablet. Magazzino denso: materiali, distinte di prelievo, lotti, ricerca catalogo (import Metel se il modulo è acceso). Acquisti: fatture passive da XML FatturaPA e scadenziario pagamenti/incassi. Creazione di molte anagrafiche resta sul desktop.
- **`/tecnici/`**: rapportini, fasi, ore. Stessa palette.

## Moduli

Nucleo sempre acceso: materiali, prodotti, commesse, lotti, centri, utenti, cruscotto.

Moduli opzionali (chiavi in `CrmMes.Api/Services/Sectors.cs`): `sales`, `purchasing`, `planning`, `shopfloor`, `quality`, `maintenance`, `shipping`, `subcontracting`, `costing`, `invoicing`, `panel-verification`, `metel`, `lot-expiry`, `food-labels`, `haccp`, `site-work`, `engineering`, `machine-testing`, `service`, `energy-monitoring`.

`metel` è **disponibile**: import `POST /api/supplier-catalog/import-metel` (tracciato ANIE a record fissi tipo A, o CSV/TXT delimitato). Non serve un file produttore per usare il modulo; un file reale può servire solo per tarare varianti strane.

Settori (preset): costruzione macchine e impianti, quadri elettrici, meccanica, alimentare, impiantistica, manifattura generica.

## Autenticazione (stato attuale)

- JWT, refresh a rotazione, logout che revoca.
- Ruoli: Admin, Management, Warehouse, Purchasing, Sales, Operator.
- Registrazione pubblica **solo bootstrap** (primo utente = Admin).
- Login desktop: **sempre la schermata di accesso in Release**. In Debug, se sul PC di sviluppo sono impostate `CRMMES_DEV_EMAIL` e `CRMMES_DEV_PASSWORD`, il login può essere automatico; **non esiste più `SkipLoginForTesting`** e **nessuna credenziale sta nel codice**.
- Canali: desktop, web, mobile (pagina tecnici).

## Prototipo Node (non produzione)

Rimasto in repo come **riferimento funzionale** del primo MVP (distinte, import PDF/Excel locali):

- `server.js`, `public/index.html`, `data/store.json`, `package.json`

Non avviarlo per lavoro reale. Avvio prodotto: API .NET + client o `/app/`.

## Avvio sviluppo

API (User Secrets / `appsettings` per Postgres e JWT), poi client WPF oppure `https://…/app/`.

```bash
dotnet test CrmMes.sln
```

CI: `.github/workflows/build.yml`. Health: `GET /health`.

## Cosa non è più vero (documenti vecchi)

| Affermazione superata | Realtà |
|---|---|
| MVP Node + `store.json` | Solo prototipo |
| 81 test API, manca qualità/OEE | Suite completa; OEE, qualità, DDT, FatturaPA, ecc. sono nel prodotto |
| `SkipLoginForTesting = false` prima dell'uso | Flag rimosso; Release ha sempre il login |
| Metel "in arrivo" in attesa di un file reale | Modulo attivabile, parser e import coperti da test sintetici |


## Pulizia del 6 ottobre 2026

Verifica di cosa è necessario nella radice del repository.

| Cartella / file | Esito | Motivo |
|---|---|---|
| `CrmMes.*` (API, Core, Desktop, Web, Console, Licensing, test) | Tenute | Prodotto |
| `installer/`, `server/`, `scripts/` | Tenute | Installer, distribuzione server, gateway macchine |
| `.github/`, `.vscode/`, `.claude/` | Tenute | CI, editor, impostazioni locali (`.claude` non tracciata) |
| `public/`, `data/`, `package.json`, `package-lock.json` | **Rimosse** | Prototipo Node del 2025, non il prodotto. Recuperabili dalla storia git |
| `node_modules/`, `publish/`, `publish-server/`, `installer-output/`, `tmp/` | **Rimosse dal disco** | Generate dalla build o dal prototipo; ignorate da git, si rigenerano |
| `scripts/machine-gateway/__pycache__/` | **Rimossa** | Cache Python tracciata per errore |

Dopo la pulizia la soluzione `CrmMes.sln` compila senza errori.
