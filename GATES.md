# Gates: chiusura dei punti deboli dell'analisi di mercato

OWNS: CrmMes.Api/**, CrmMes.Core/**, CrmMes.Desktop/**, CrmMes.Web/**, CrmMes.Api.Tests/**, CrmMes.Desktop.Tests/**, CrmMes.Web.Tests/**, scripts/**, STATO-PROGETTO.md, RIEPILOGO-SVILUPPO.md, ANALISI-MERCATO-MES.md

Scope: tutti i contro e le mancanze elencati dal titolare dopo l'analisi di mercato, ciascuno sviluppato, provato e pubblicato; quelli che dipendono da un acquisto o da un contratto del titolare restano come consegna motivata.

- [ ] G1: selettore "mio reparto / tutto" nel terminale di reparto e nella pagina tecnici
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~CompanyStructureTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G2: autenticazione a due fattori (TOTP) attivabile per utente, obbligatoria dove l'admin la impone, con codici di recupero
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~TwoFactorTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G3: ufficio tecnico: revisioni di distinte e cicli, allegati versionati, modifiche tecniche approvate che aggiornano le commesse aperte
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~EngineeringTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G4: istruzioni di lavoro, disegni e programmi CNC della fase consultabili al terminale e sul web
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~EngineeringTests.NewDocumentVersion -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [x] G5: collaudo macchine FAT/SAT con checklist, fascicolo tecnico e dichiarazione CE (Reg. UE 2023/1230)
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~MachineTestingTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 01/10/2026 "Non superati: 0. Superati: 4" (API); bUnit MachineTestingWebTests 3/3; suite complete 329/119/53/18 verdi

- [x] G6: service post-vendita: macchine installate con matricola e garanzia, richieste di assistenza, interventi, storico
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~ServiceTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 01/10/2026 "Non superati: 0. Superati: 5" (API); bUnit ServiceWebTests 2/2; suite complete 334/119/57/18 verdi

- [x] G7: monitoraggio energetico: kWh per macchina dai dati macchina, progetti con report ex ante / ex post per la perizia (kWh per commessa resta da fare)
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~EnergyTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 01/10/2026 "Non superati: 0. Superati: 6" (API); bUnit EnergyWebTests 2/2; suite complete 340/119/59/18 verdi

- [x] G8: fatture passive importate da XML FatturaPA e scadenziario incassi e pagamenti con solleciti
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~PayablesTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 5" (API); suite completa 586/586; migrazione AddPayables verificata su Neon produzione; G8 pubblicato su main

- [ ] G9: invio allo SdI tramite intermediario, pronto al collegamento (interfaccia e stati di invio)
  EVIDENCE: pending

- [x] G10: calcolo dei fabbisogni sulle distinte con proposte d'ordine
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~MrpTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 1" (API); esplosione BOM commesse Draft/Released/InProgress − stock − ordini aperti; web `/mrp`. Resta: creazione automatica ordini fornitore dalle proposte, lead time, MRP multi-livello

- [ ] G11: magazzino con ubicazioni e inventario
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~LocationTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G12: schedulazione a capacità finita dei centri di lavoro
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~FiniteSchedulingTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G13: pacchetti di settore: import distinta EPLAN, lista fili, DM 37/08, SAL, calendario squadre, certificati 3.1, tabella nutrizionale, bilance
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~SectorPackTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: pending

- [ ] G14: le tre suite di test verdi dopo tutto il lavoro
  CHECK: dotnet test CrmMes.sln -nologo -v q
  EXPECT: Non superati:\s+0\.
  EVIDENCE: pending

- [ ] G15: client completo non solo Windows: tutte le aree disponibili anche sulla piattaforma web
  EVIDENCE: pending

- [ ] G16: hosting a pagamento e prodotto con referenze (dipendono dal titolare: acquisto del piano, primi clienti)
  EVIDENCE: pending
