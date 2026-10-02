# Handoff per Claude — prossimo gate G9

Aggiornato: **2 ottobre 2026**. Hai accesso diretto al repo, al database Neon (via `dotnet ef` / `NEON_DATABASE_URL`) e agli strumenti locali. Segui l’ordine sotto. Non inventare lavoro parallelo.

## Contesto in una frase

Nicolò MES (CrmMes): WPF + API ASP.NET 8 + Blazor `/app/` + `/tecnici/`, DB Neon, deploy Render su push `main`. Il blocco G8 + OEE da macchina + origine UE + MRP base + tema aziendale è stato pubblicato su `main` il 02/10/2026; migrazioni Neon verificate applicate e suite completa 586/586.

## Non rifare (già fatto in codice + Neon)

| Cosa | Evidenza |
|---|---|
| G8 fatture passive + scadenziario | `PayablesController`, web/desktop, `AddPayables` su Neon, `PayablesTests` 5/5 |
| OEE cruscotto da MachineEvents | `WorkOrdersController` dashboard + campi `OeeSource` / Hybrid / Machine |
| Dichiarazione origine UE software | `software-origin` API + `/origine-software`, `AddSoftwareOriginDeclaration` su Neon |
| G10 MRP base | `MrpController`, `/mrp`, `MrpTests` |
| Aspetto grafico azienda | `UiTheme`, `/aspetto`, `AppearanceWindow`, `AddUiTheme` su Neon, `UiThemeTests` 5/5 |
| Docs di sessione | `RIEPILOGO-SVILUPPO.md`, questo file, pezzi di `STATO-PROGETTO` / `ANALISI-MERCATO-MES` |

Se l’utente chiede “procedi dai contro”, **salta** i punti sopra.

---

## Cosa farai tu (in quest’ordine)

### 0. All’apertura sessione (sempre)
1. Leggi questo file + `git status -sb`.
2. Non fare `git checkout --` su `MainWindow.xaml` / `Styles.xaml` senza stash: contengono UI (scadenziario, fatture passive, Metel, aspetto) non interamente in HEAD.
3. Non pushare e non commitare finché l’utente non lo chiede esplicitamente.

### 1. Quando l’utente dice di pubblicare / commit / push
1. Chiedi (o conferma) se includere **tutto** il working tree o solo il blocco 02/10 (G8+OEE+origine+MRP+aspetto). Il tree ha anche molto altro (pagine web consultazione, Metel, StockLedger, font, installer…).
2. Prima del commit: `dotnet test` sulle suite rilevanti (almeno Api.Tests filtrati Payables/UiTheme/SoftwareOrigin/Mrp + un giro ragionevole se tocchi tutto).
3. Commit con messaggio chiaro (stile repo: italiano, cosa/perché).
4. Push `main` → Render ridistribuisce. Verifica che non restino migrazioni non applicate (quelle tre del 02/10 **sono già su Neon**).
5. Allinea i documenti se mentono (“pubblicato su main” in `GATES.md` G8 è **falso** finché non c’è il push: correggilo al momento del push).

### 2. Prossimo sviluppo prodotto (dopo publish, o se l’utente dice “continua i contro”)
Uno step alla volta, con test + (se serve) migrazione Neon **prima** del push:

1. **G9 — SdI automatico**
   Interfaccia stati di invio + adattatore intermediario (stub/config se non c’è contratto). Non mescolare con altro. XML FatturaPA in uscita esiste già.

2. **G11 — Ubicazioni magazzino + inventario barcode**
   Solo dopo G9 o se l’utente lo prioritizza.

3. **G12 — Capacità finita** centri di lavoro.

4. **Web in scrittura** anagrafiche/documenti (oggi molte pagine web sono sola lettura).

5. **MRP avanzato** (opzionale dopo G10 base): crea PO dalle proposte, lead time, multi-livello.

### 3. Cose che NON sono codice (non “risolverle” da solo)
- Hosting Render a pagamento, certificato firma, Stripe keys, eliminazione branch Neon `test-multisettore`, password account di sviluppo esposto in passato, macchina pilota OPC/MQTT in reparto.
- Per queste: documenta cosa manca / istruzioni; aspetta il titolare.

### 4. Campo macchine
Gateway in `scripts/machine-gateway` e OEE cruscotto sono pronti. Serve prova su macchina reale: al massimo documenta checklist; non fingere letture di produzione.

---

## Come lavorare qui

- Lingua UI e messaggi: **italiano**.
- Pattern esistenti: controller API + test in `CrmMes.Api.Tests`, pagine Blazor in `CrmMes.Web/Pages`, desktop WPF.
- Migrazioni: `dotnet ef migrations add … --project CrmMes.Api` poi `dotnet ef database update --project CrmMes.Api` su Neon **prima** del push se la migrazione è breaking/nuove tabelle usate subito.
- Encoding: su Windows non usare `Set-Content` PowerShell su XAML UTF-8 (corrompe `Nicolò`); preferisci tool di edit o Python `encoding='utf-8'`.
- Aggiorna a fine step: `GATES.md` (checkbox + EVIDENCE), `STATO-PROGETTO.md`, `RIEPILOGO-SVILUPPO.md`, e **riscrivi questo HANDOFF** con lo stato nuovo.

---

## Verifiche rapide utili

```text
dotnet test CrmMes.Api.Tests --filter "FullyQualifiedName~UiThemeTests|FullyQualifiedName~PayablesTests|FullyQualifiedName~SoftwareOriginTests|FullyQualifiedName~MrpTests"
```

Aspetto: Admin → web `/aspetto` o desktop Amministrazione → Aspetto grafico → preset Carbone → Salva.

---

## Decisione già confermata

L’utente ha scelto la pubblicazione dell’intero working tree e ha chiesto di procedere poi con G9. Per G9 non mescolare altri gate; non pubblicare il nuovo lavoro senza una richiesta esplicita.
