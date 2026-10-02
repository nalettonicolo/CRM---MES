# Installer Nicolò MES

## Cosa c’è adesso

| File | Ruolo |
|------|--------|
| `NicoloMES.iss` | **Installer unificato (suite)**: programma, server, o entrambi. Produce `NicoloMES-Setup.exe` (nome usato dall’auto-update). |
| `NicoloMES-Server.iss` | Solo server (mantenuto per chi vuole ancora lo script separato). |
| `legacy/` | Copia storica degli script Client e Server **prima** dell’unificazione. Non cancellare. |
| `archive/` | Snapshot locali di build vecchie (exe + publish). Non è il canale ufficiale: le release restano su GitHub. |

## Versioni GitHub (tag)

Le versioni pubblicate sono i tag `v1.0.0` … `v1.8.0` (e successivi).
L’elenco locale è in `archive/VERSIONI-GIT.txt`.
**Non cancellare i tag**: i clienti con versioni precedenti aggiornano da soli dalla release più recente.

## Aggiornamento automatico

`UpdateService` scarica sempre `NicoloMES-Setup.exe` + `.sha256` dalla latest release, con `/UPDATE=1`.
In quel modo la suite installa **solo il programma**, senza toccare un eventuale server.

## Build locale

```powershell
.\scripts\Build-Installers.ps1 -Version 1.9.0
```

Richiede Inno Setup 6+ (`ISCC.exe`) e produce i file in `installer-output/`.

## Pipeline

`.github/workflows/release.yml` su tag `v*`: test → publish client → publish server → suite → (opzionale) server-only legacy → GitHub Release.
