# Policy di conservazione e cancellazione dei dati personali — bozza

**Bozza tecnica, non consulenza legale.** Scritta per dare una struttura di partenza, basata sul [Codice di condotta Assosoftware](https://lentepubblica.it/wp-content/uploads/2024/12/Codice-di-condotta-per-il-trattamento-dei-dati-personali-effettuato-dalle-imprese-di-sviluppo-e-produzione-di-software-gestionale.pdf) per i software gestionali (in vigore da novembre 2024). Prima di adottarla, un legale o un DPO deve rivederla: i tempi di conservazione indicati sono un punto di partenza ragionevole, non un obbligo di legge verificato caso per caso. Riferimento: punto 6 di [ANALISI-MERCATO-MES.md](ANALISI-MERCATO-MES.md), sezione 8.

## 1. Perché serve

Il Codice di condotta non fissa tempi di conservazione fissi per i software gestionali: per principio (art. 5.1.e GDPR, "minimizzazione nel tempo") la durata dipende dalla finalità del trattamento, decisa caso per caso dal titolare. Questo documento propone una durata per ciascuna categoria di dato che Nicolò MES tratta, da far approvare o correggere.

## 2. Categorie di dati personali trattati dal sistema

| Categoria | Dove si trova | Persone coinvolte |
|---|---|---|
| Account utente (nome, email) | `Users` | Dipendenti e collaboratori dell'azienda cliente |
| Contatti cliente (nome, email, telefono) | `Customers` | Persone di riferimento presso i clienti — la ragione sociale e la partita IVA non sono dati personali |
| Contatti fornitore | `Suppliers` | Persone di riferimento presso i fornitori |
| Firma a schermo (rapportino, dichiarazione) | `SiteReports`, documenti collaudo | Chi firma (cliente o tecnico) |
| Dati di fatturazione (SDI, FatturaPA) | `Invoices`, `CustomerFiscalData` | Titolare effettivo o referente fiscale, quando è una persona fisica |
| Registro operazioni (audit log) | `AuditLogs` | Chi ha eseguito un'azione nel sistema |
| Presenze | `Attendance` | Dipendenti |

## 3. Tempi di conservazione proposti

| Categoria | Proposta | Motivo |
|---|---|---|
| Account utente, dopo la cessazione del rapporto | 12 mesi, poi anonimizzazione | Tempo ragionevole per chiudere pratiche amministrative residue (vedi §4) |
| Documenti fiscali (fatture, DDT) | 10 anni | Art. 2220 c.c., obbligo di conservazione della documentazione contabile |
| Contatti cliente/fornitore attivi | Per la durata del rapporto commerciale + 10 anni dall'ultima fattura | Segue la conservazione fiscale: i contatti restano legati ai documenti |
| Registro operazioni (audit log) | 2 anni dall'azione | Bilancia la tracciabilità per sicurezza con la minimizzazione; da confermare con un legale per i settori regolamentati (es. dispositivi medici, se mai rilevante) |
| Presenze | 5 anni | Prassi comune per le verifiche del lavoro; da confermare con un consulente del lavoro |

## 4. Diritto all'oblio (cancellazione su richiesta)

**Principio:** non si cancella mai la riga di un record citato da altri dati (commesse, documenti, registro operazioni) — romperebbe quella storia. Si **anonimizza**: si sostituiscono i dati personali con valori non riconducibili alla persona, mantenendo l'Id e i collegamenti.

**Implementato** (9 ottobre 2026): `POST api/users/{id}/anonymize`, solo Admin. Sostituisce nome ed email dell'utente, rimuove PIN e 2FA, disattiva l'account e revoca le sessioni aperte. Pagina web Utenti → pulsante "Anonimizza (GDPR)", con conferma. Operazione registrata nel registro operazioni, irreversibile.

**Non ancora implementato:**
- Anonimizzazione dei contatti cliente/fornitore (nome, email, telefono di una persona di riferimento) — da fare quando serve davvero una richiesta in questo senso: oggi `Customers`/`Suppliers` sono quasi sempre dati di un'azienda, non di una persona fisica.
- Cancellazione effettiva (non anonimizzazione) per i dati che nessuna legge obbliga a conservare — da valutare caso per caso con un legale, perché il confine con l'obbligo fiscale non è sempre netto.

## 5. Misure tecniche già in atto

- Password e PIN cifrati (hash), mai in chiaro.
- 2FA disponibile, obbligabile per ruolo dall'Admin.
- Controllo degli accessi per ruolo su ogni endpoint che legge dati personali.
- Registro operazioni su 49 controller su 51 (i 2 restanti sono di sola lettura).
- Audit log con indici per interrogarlo in fretta in caso di richiesta di accesso ex art. 15 GDPR.
- Segreti (connessione al database, chiavi) fuori dal codice, nelle variabili d'ambiente del server.

## 6. Cosa resta da decidere (titolare + legale/DPO)

1. Confermare o correggere i tempi di conservazione della tabella al punto 3.
2. Decidere se e quando estendere l'anonimizzazione ai contatti di clienti e fornitori.
3. Scrivere l'informativa privacy da mostrare ai dipendenti e, se richiesto, ai clienti che firmano un documento a schermo.
4. Verificare se il registro di trattamento (art. 30 GDPR) è già previsto altrove in azienda, o se va costruito a partire da questo documento.
