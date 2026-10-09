# Installer Nicolò MES — build locale

Compilato in locale il 9 ottobre 2026, versione `1.9.2`. Suite completa: programma Windows + server,
nello stesso installer — scegli i componenti nel wizard (per uso normale basta "Programma Nicolò MES").

## Cosa fa al primo avvio

1. Nel wizard, scegli i componenti: **solo "Programma Nicolò MES"** se il server ti serve già online
   (es. `https://crmmes-api.onrender.com/`); aggiungi anche "Server Nicolò MES" solo se vuoi un server
   in locale sul tuo PC (richiede amministratore: avvia l'installer con **tasto destro → Esegui come
   amministratore** fin dall'inizio, altrimenti l'elevazione a metà installazione fallisce).
2. Il programma, alla prima apertura, mostra la schermata di login. Da lì, **"Impostazioni server"**
   per inserire l'indirizzo giusto se diverso da `localhost:5092`.
3. Se l'account Admin del server collegato non ha ancora configurato l'azienda, al primo accesso vedrai
   il **pannello di configurazione iniziale** (ragione sociale, partita IVA, settore, moduli).

## Verifica di integrità

```powershell
(Get-FileHash .\NicoloMES-Setup.exe -Algorithm SHA256).Hash
```

Deve corrispondere al contenuto di `NicoloMES-Setup.exe.sha256`.

## Non firmato digitalmente

Windows mostra "editore sconosciuto" all'avvio (SmartScreen): previsto, il certificato di firma del
codice (G16) non è ancora stato acquistato. Clicca "Ulteriori informazioni" → "Esegui comunque".

**Nota (9 ottobre 2026):** un antivirus/Defender può trattenere per alcuni minuti le connessioni di
rete di un eseguibile nuovo e non firmato, durante la prima verifica di reputazione. Se il login dà
"Server non raggiungibile" nei primi minuti dopo l'installazione ma la rete risulta a posto (vedi
`scripts/diagnostica-connessione-server.ps1` nel repository), riprova dopo qualche minuto.

## Non è una release ufficiale

Non è pubblicato su GitHub Releases: non attiva l'aggiornamento automatico per chi ha già una versione
precedente installata. È per uso locale/di prova. Le release ufficiali restano i tag `v*` su GitHub.
