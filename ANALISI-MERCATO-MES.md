# Analisi di mercato dei MES e gestionali — confronto con Nicolò MES

Aggiornata al **2 ottobre 2026** (sera). Allineata a [STATO-PROGETTO.md](STATO-PROGETTO.md), [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md) e ricerca di mercato ripresa in questa data.
Punto di vista: commerciale, commercialista, fatturazione, capocantiere, responsabile tecnico, capo reparto, ingegneri (gestionale / meccanico / informatico / 4.0–5.0), cybersecurity, data analyst, graphic designer.

---

## 1. In sintesi

- **Il mercato.** In Italia i MES restano pezzo centrale della digitalizzazione di fabbrica: studio AlixPartners–Qualitas (marzo 2025) → **~120 M€ entro il 2027**, CAGR **5,2%** (2022–2027). Europa occidentale CAGR **6,7%** (~**887 M€** al 2027); globale ~**3 Md€** al 2027 (CAGR ~5,6%). Mercato italiano **frammentato (~23 fornitori)**: consolidamento, uscita dal “fatto in casa”, cloud, IoT/edge, AI.
- **Cosa distingue i prodotti.** Connettività macchina reale, dati in tempo reale, integrazione ERP/contabilità, documentazione di **interconnessione** per la perizia dell’iperammortamento. I MES “solo reparto” vincono sul campo; chi ha anche commerciale e fiscale italiano riduce i pezzi da integrare.
- **Posizionamento Nicolò.** Un solo prodotto: produzione (MES) + commerciale + documenti italiani (DDT, FatturaPA) + moduli di settore (61439, HACCP, rapportini, FAT/SAT/CE, service, energia, ufficio tecnico) + MRP, magazzino, qualità. Desktop + web + telefono. I MES PMI tipici restano sul reparto e demandano un ERP esterno.
- **Momento commerciale 2026–2028.** Iperammortamento (L. 199/2025): maggiorazione **180% / 100% / 50%** su scaglioni fino a 20 M€, investimenti **1/1/2026–30/9/2028**. Software Allegato V agevolabile se **interconnesso**. Dal DL 38/2026 il vincolo **origine UE sui beni è stato soppresso** (retroattivo); restano obbligatori interconnessione, perizia e comunicazioni GSE. I canoni SaaS restano zona grigia: meglio vendere/licenziare in modo capitalizzabile quando possibile.

---

## 2. Mappa del mercato (MES + gestionali)

| Fascia | Esempi | Per chi | Prezzo indicativo (2026) | Punto forte | Limite per una PMI |
|---|---|---|---|---|---|
| MES enterprise | Siemens Opcenter, SAP DM, Rockwell/Plex, AVEVA, Critical Manufacturing | Grandi gruppi | Decine–centinaia di k€ + canoni | Profondità, PLC/SCADA, APS, AI | Costo, mesi di progetto, integratori |
| MES italiani PMI | Bravo, siMES (SiVaF), Opera MES, MES Factory, NET@PRO, moduli TeamSystem/Zucchetti | PMI manifatturiere | Bravo Plus ~**290 €/risorsa**/anno (min. 10); siMES da ~**3.500 €/macchina** + canone | OEE da macchina, IoT, connettori ERP, marketing 4.0/5.0 | Quasi sempre **solo reparto**: DDT/FatturaPA/clienti nell’ERP |
| ERP / gestionali IT | TeamSystem, Zucchetti, Danea, Fatture in Cloud + add-on | PMI di ogni settore | Canoni per utente | Fiscale IT, fatturazione, contabilità | Produzione e campo deboli o assenti |
| MRP / ERP cloud esteri | MRPeasy, Katana, Odoo MRP | Piccole aziende | Decine–centinaia $/mese | Avvio rapido, distinte, magazzino | Fiscale IT debole, poche macchine |
| MES no-code | Tulip e simili | Chi ha UT interno | Centinaia $/mese | App di reparto su misura | Va costruito tutto |
| Verticali cantieri/impianti | TeamSystem Cantieri, Antos, D-TEC, mInterventi | Impiantisti | Canoni utente | Computo, ticket, app tecnici | Niente fabbrica |
| Verticali food | Plex/AVEVA food, FoodDocs, SafetyChain | Alimentare | SaaS → progetti | HACCP, audit | Spesso esteri; fiscale IT assente |

**Lettura competitiva.** Bravo = costi di commessa + IoT add-on + ERP esterno. siMES = AI e retrofit macchine. Opera / NET@PRO = modularità produzione + connettori. I gestionali italiani (TeamSystem/Zucchetti) vincono sul fiscale e perdono sul shop-floor. **Nessuno nella fascia PMI mette insieme FatturaPA nativa + DDT + MES di fabbrica + settori IT** come Nicolò: il confronto tipico resta “MES + ERP già in casa”, non “un solo prodotto”.

---

## 3. Cosa chiede il mercato nel 2026

1. Interconnessione vera (OPC UA, MQTT, Modbus…) e prove per la perizia.
2. Energia / misure utili alla documentazione agevolabile.
3. Schedulazione a capacità finita (+ in prospettiva copilota AI con umano in loop — Gartner MES Guide 2026).
4. Istruzioni digitali alla postazione.
5. Cloud **e** on-premise; mobile.
6. Time-to-value 2–4 mesi per PMI ~20 macchine (licenza spesso la parte minore del TCO).
7. Sicurezza (2FA, audit; NIS2 per clienti strutturati).
8. Un solo posto dove chiudere il ciclo ordine → produzione → DDT → fattura (soprattutto sotto le 50–100 persone).

---

## 4. Confronto funzione per funzione

Legenda: ✅ c’è · 🟡 parziale · ❌ manca · 📦 codice pronto / da pubblicare su `main`

| Area | Nicolò MES (02/10/2026 sera) | MES PMI IT | MRP cloud | ERP IT | Enterprise |
|---|---|---|---|---|---|
| Distinte, cicli, commesse, fasi | ✅ | ✅ | ✅ | 🟡 | ✅ |
| Terminale reparto PIN (+ offline) | ✅ | ✅ | 🟡 | ❌ | ✅ |
| Filtro “mio reparto” | ✅ | ✅ | ❌ | ❌ | ✅ |
| Tracciabilità lotti / richiamo | ✅ | ✅ | 🟡 | 🟡 | ✅ |
| Matricole / unità | ✅ | 🟡 | 🟡 | ❌ | ✅ |
| OEE (fasi + macchina se ci sono letture) | ✅ | ✅ da macchina | ❌ | ❌ | ✅ |
| Gateway OPC/MQTT | ✅ predisposto; campo reale ❌ | ✅ | ❌ | ❌ | ✅ |
| Energia / progetti perizia | ✅ software; contatore reale ❌ | ✅ | ❌ | ❌ | ✅ |
| Capacità finita | ✅ (📦) | 🟡/✅ | 🟡 | ❌ | ✅ |
| Ufficio tecnico / revisioni | ✅ | 🟡 | ❌ | ❌ | ✅ |
| FAT/SAT + fascicolo + CE/UE | ✅ | 🟡 | ❌ | ❌ | ✅ |
| Service post-vendita | ✅ | 🟡 | ❌ | 🟡 | ✅ |
| Qualità NC + piani | ✅ | ✅ | 🟡 | 🟡 | ✅ |
| Taratura strumenti + CAPA | ✅ base (📦) | 🟡 | ❌ | ❌ | ✅ |
| Manutenzione | ✅ | ✅ | ❌ | 🟡 | ✅ |
| MRP multi-livello + crea PO | ✅ (📦) | 🟡 via ERP | ✅ | 🟡 | ✅ |
| Ubicazioni / inventario | ✅ (📦) | 🟡 | ✅ | 🟡 | ✅ |
| Clienti / preventivi | ✅ | ❌ (ERP) | ✅ | ✅ | 🟡 |
| DDT / conto lavoro | ✅ | ❌ | 🟡 | 🟡 | 🟡 |
| FatturaPA XML validata | ✅ | ❌ (ERP) | ❌ | ✅ | ❌ |
| Invio SdI automatico | 🟡 stub+stati (📦); intermediario reale ❌ | ❌ | ❌ | ✅ (spesso) | ❌ |
| Passive + scadenziario | ✅ | ❌ | 🟡 | ✅ | ❌ |
| Margini di commessa | ✅ | 🟡 | 🟡 | 🟡 | ✅ |
| Presenze / timbrature | ✅ base (📦) | 🟡 | ❌ | ✅ | ✅ |
| Web + telefono | ✅ in crescita (📦 board, pack, qualità…) | ✅ | ✅ | ✅ | ✅ |
| Cloud o server cliente | ✅ entrambi | ✅ | solo cloud | ✅ | ✅ |
| 2FA | ✅; SSO ❌ | 🟡 | ✅ | 🟡 | ✅ |
| API documentate (Swagger) | ✅ (📦) | 🟡 | ✅ | 🟡 | ✅ |
| Assistente IA | ❌ | 🟡 | 🟡 | 🟡 | ✅ |
| Multilingua / valute | ❌ | 🟡 | ✅ | 🟡 | ✅ |
| Temi grafici azienda | ✅ | 🟡 | 🟡 | 🟡 | ✅ |
| Pack settore (EPLAN, DM37, SAL, 3.1…) | ✅ base (📦) | 🟡 | ❌ | 🟡 | 🟡 |

---

## 5. Settore per settore (vs verticali)

### 5.1 Quadri elettrici
**Abbiamo:** 61439, Metel, EPLAN→BOM, lista cavi, DDT/fattura, costi.
**Resta vs specialisti:** etichette cavi/morsetti dedicate, Allegati legati alla dichiarazione in un click.

### 5.2 Meccanica / carpenteria
**Abbiamo:** cicli, conto lavoro, OEE, manutenzione, cert 3.1 su lotto, taratura strumenti.
**Resta:** DNC diretto, nesting, preventivo meccanico “avanzato”.

### 5.3 Costruzione macchine
**Abbiamo:** UT, FAT/SAT, fascicolo, CE/UE, service, energia.
**Resta:** PDF rapporto collaudo autonomo, firma qualificata, QR matricola → storico completo.

### 5.4 Impiantistica
**Abbiamo:** rapportini firmati, Metel, DM 37/08 generate, SAL, calendario squadre (base).
**Resta:** computo metrico pieno, Angaisa, magazzino furgone.

### 5.5 Alimentare
**Abbiamo:** FEFO, allergeni, SSCC, richiamo, HACCP, nutrizionale, bilance (letture software).
**Resta:** bilance hardware, IFS/BRC, ricetta con resa/cali.

### 5.6 Manifattura generica
Nucleo MES + MRP + ubicazioni + capacità finita + commerciale IT. Gap sentiti: **campo macchine**, **SdI intermediario**, **referenze**.

---

## 6. Pro e contro di Nicolò MES

### Pro — perché un cliente PMI lo sceglierebbe

1. **Tutto in uno italiano.** Dal preventivo alla FatturaPA (XML validato), con DDT e conto lavoro: niente “MES + ERP” obbligatorio.
2. **Shop-floor vero.** Terminale PIN (anche offline), fasi, fermi, NC, OEE, matricole, istruzioni/disegni alla postazione.
3. **Settori nativi.** 61439, HACCP/allergeni/SSCC, rapportini firmati, FAT/SAT/CE, service, Metel, pack EPLAN/DM37/SAL/3.1 — non add-on generici.
4. **Acquisti e magazzino.** MRP multi-livello con proposte e creazione ordini; ubicazioni e inventario; lotti e FEFO.
5. **Pianificazione.** Board settimanale (anche web) + capacità finita centri di lavoro.
6. **Qualità e HR minimi.** CAPA, taratura strumenti, timbrature — oltre le sole NCM di reparto.
7. **Agevolazioni 2026–2028.** Gateway interconnessione + energia + dichiarazione origine software già pronti per la narrazione di perizia (anche se il vincolo UE fiscale è stato tolto, resta un segnale di prodotto italiano).
8. **Canali multipli.** Desktop Windows, web `/app`, `/tecnici` da telefono; temi grafici aziendali; cloud o server cliente; 2FA; Swagger; auto-update.
9. **Costo di ingresso competitivo** rispetto a Bravo Plus (min. 10 risorse × 290 €) o a progetti enterprise, se il modello commerciale resta snello.
10. **Profondità tecnica dimostrabile.** Suite test ampia (623), CI, migrazioni, audit — non un prototipo Excel.

### Contro — dove perde o è fragile (aggiornato sera 02/10)

1. **Deploy ancora da fare.** Il codice dei contro è in tree + Neon; **manca il push** su `main`/Render (lo fai tu).
2. **SdI intermediario commerciale.** Il provider HTTP è pronto (`Sdi:Provider=http`); serve ancora **contratto + URL/chiavi** del provider (Aruba, …).
3. **Macchina fisica in officina.** Demo-feed e simulatore Python chiudono la demo commerciale; per la perizia serve un collegamento reale.
4. **SSO IdP di produzione.** Plumbing + login esterno ok; manca il mapper OIDC completo verso Azure/Google (abilitare solo dietro IdP fidato).
5. **IA “di marketing”.** Assistente stub/OpenAI presente; non è predittivo/APS come siMES.
6. **G16 / fiducia.** Hosting a pagamento, referenze pubbliche, firma .exe, Stripe — solo titolare.
7. **Contabilità completa.** Fino a fattura e scadenziario; non sostituisce TeamSystem/Zucchetti prima nota.
8. **Verticali da specialista.** Base EPLAN/DM37/SAL/3.1 sì; computo/DNC/IFS non da verticalista puro.

### Pro — aggiornamento sera

- Intermediario SdI collegabile via HTTP; demo campo senza hardware; SSO base; locale/valuta; assistente IA; Swagger; CAPA/taratura/presenze; MRP con crea PO; G13 persistenza; planning web.


---

## 7. Verdetto competitivo (una frase per fascia)

| Contro chi | Verdetto |
|---|---|
| MES PMI (Bravo, siMES, Opera…) | **Vince** su commerciale/fiscale IT e settori; **perde** su IoT plug-and-play e brand di campo finché non c’è una macchina pilota. |
| ERP italiani | **Vince** su produzione e shop-floor; **perde** su contabilità completa e rete commerciale. |
| MRP cloud (Katana, MRPeasy) | **Vince** su Italia (DDT/FatturaPA/HACCP/61439); **perde** su UX cloud-native e internazionalizzazione. |
| Enterprise | Non compete sullo stesso budget; può essere **complementare** solo se il cliente non vuole un progetto da 6–18 mesi. |

**Posizione consigliata in vendita:** *“Il gestionale di fabbrica italiano che fa anche fattura e DDT — non un MES che ti chiede di tenere l’ERP a parte.”*
**Condizione per non perdere la trattativa tecnica:** push in produzione + una demo con macchina reale + intermediario SdI (anche solo su un cliente pilota).

---

## 8. Priorità dopo questa analisi

| # | Azione | Chi | Impatto |
|---|---|---|---|
| 1 | Commit/push working tree | Dev | Alto — allinea marketing e prodotto |
| 2 | Contratto intermediario SdI | Titolare | Alto — chiude il ciclo fiscale |
| 3 | Macchina + contatore pilota | Campo | Molto alto — credibilità vs MES di campo |
| 4 | Hosting a pagamento + 1–2 referenze (G16) | Titolare | Alto — fiducia |
| 5 | Pacchetto avvio 30 giorni (template + formazione) | Commerciale | Medio-alto — time-to-value |
| 6 | SSO / IA / i18n | Codice pronto; l’IdP OIDC di produzione è configurazione titolare | — |

---

## 9. Cosa è cambiato rispetto all’analisi di stamattina

Chiusi in codice (Neon ok, push pending): MRP multi-livello + crea PO; G13 persistenza; planning web; CAPA/taratura; presenze; Swagger; suite 623/623.
Normativa: vincolo **origine UE** sull’iperammortamento **eliminato** (DL 38/2026); restano interconnessione e perizia.
Aperti strutturali: deploy, SdI reale, campo, G16, IA/SSO/i18n.

---

## Fonti

### Mercato
- [AlixPartners–Qualitas — MES Italia 120 M€ al 2027](https://www.alixpartners.com/newsroom/mercato-dei-mes-in-forte-crescita-in-italia-studio-alixpartners-e-qualitas/)
- [Industria Italiana — evoluzione MES](https://www.industriaitaliana.it/manufacturing-execution-systems-alixpartners-digitalizzazione-manifattura/)
- [Qualitas — trend MES](https://www.qualitas.it/blog/un-mercato-in-crescita-i-trend-del-mes)
- [Lookin — MES software 2026 costi PMI](https://lookin.cloud/blog/mes-software-produzione)
- [Gartner MES Guide 2026 — lettura Siemens](https://blogs.sw.siemens.com/opcenter/2026-gartner-market-guide-for-manufacturing-execution-systems-how-ai-is-shaping-mes-and-where-we-believe-opcenter-adds-customer-value/)

### Incentivi
- [MIMIT — Iperammortamento / Transizione 5.0](https://www.mimit.gov.it/it/incentivi/nuovo-piano-transizione-5-0-iperammortamento)
- [Heuris — iperammortamento e MES 2026](https://www.heuris.it/iperammortamento-mes-2026)
- [Guida operativa GSE / interconnessione](https://www.iperammortamenti.it/risorse/iperammortamento-guida-operativa)
- [Eliminazione vincolo origine UE (DL 38/2026)](https://www.studiofantiselleri.it/iperammortamento-2026-decreto-fiscale-beni-agevolabili/)

### Concorrenti
- [Bravo — prezzi Plus/Pro](https://www.bravomanufacturing.it/confronta-prezzi/)
- [siMES / SiVaF](https://www.sivaf.it/software-mes-per-pmi-infallibile-controllo-produzione/)
- [Opera MES](https://www.operames.it/opera-mes/)
- [TeamSystem produzione/MES](https://www.teamsystem.com/aziende/enterprise/funzionalita/produzione-mes-ts-enteprise/)

Stato prodotto: [STATO-PROGETTO.md](STATO-PROGETTO.md) · Gate: [GATES.md](GATES.md) · Handoff: [HANDOFF-CLAUDE.md](HANDOFF-CLAUDE.md).
