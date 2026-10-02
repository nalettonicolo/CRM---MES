# Gates: chiusura dei punti deboli dell'analisi di mercato

OWNS: CrmMes.Api/**, CrmMes.Core/**, CrmMes.Desktop/**, CrmMes.Web/**, CrmMes.Api.Tests/**, CrmMes.Desktop.Tests/**, CrmMes.Web.Tests/**, scripts/**, STATO-PROGETTO.md, RIEPILOGO-SVILUPPO.md, ANALISI-MERCATO-MES.md

Scope: tutti i contro e le mancanze elencati dal titolare dopo l'analisi di mercato, ciascuno sviluppato, provato e pubblicato; quelli che dipendono da un acquisto o da un contratto del titolare restano come consegna motivata.

- [x] G1: selettore "mio reparto / tutto" nel terminale di reparto e nella pagina tecnici
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~CompanyStructureTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: fatto in prodotto (filtro reparto/DepartmentFilter + UI terminale/tecnici); test CompanyStructureTests

- [x] G2: autenticazione a due fattori (TOTP) attivabile per utente, obbligatoria dove l'admin la impone, con codici di recupero
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~TwoFactorTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: fatto in prodotto (TwoFactor + AccountSecurity); test TwoFactorTests

- [x] G3: ufficio tecnico: revisioni di distinte e cicli, allegati versionati, modifiche tecniche approvate che aggiornano le commesse aperte
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~EngineeringTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: fatto in prodotto (EngineeringController + web ufficio tecnico); test EngineeringTests

- [x] G4: istruzioni di lavoro, disegni e programmi CNC della fase consultabili al terminale e sul web
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~EngineeringTests.NewDocumentVersion -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: fatto in prodotto (documenti per fase al terminale e web commessa)

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
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 5" (API); suite completa 623/623; migrazione AddPayables verificata su Neon produzione; G8 pubblicato su main

- [x] G9: invio allo SdI tramite intermediario, pronto al collegamento (interfaccia e stati di invio)
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~SdiTransmissionTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 6" (SdiTransmissionTests + HttpSdiProviderTests); provider HTTP configurabile, stub solo in Development, registrazione manuale; migration verificate su Neon; suite completa 623/623. Push/deploy in corso

- [x] G10: calcolo dei fabbisogni sulle distinte con proposte d'ordine
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~MrpTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 sera "Non superati: 0. Superati: 3"; multi-livello (depth 8), lead time, POST create-orders → Draft PO; BOM accetta codici prodotto sottoassieme; web `/mrp` con crea ordini

- [x] G11: magazzino con ubicazioni e inventario
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~LocationTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 2"; api/locations + inventario open/lines/close; web `/ubicazioni`; migrazione AddSdiAndWarehouseLocations su Neon

- [x] G12: schedulazione a capacità finita dei centri di lavoro
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~FiniteSchedulingTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 "Non superati: 0. Superati: 3"; FiniteCapacityScheduler + POST work-orders/{id}/schedule + GET work-centers/capacity-plan

- [x] G13: pacchetti di settore: import distinta EPLAN, lista fili, DM 37/08, SAL, calendario squadre, certificati 3.1, tabella nutrizionale, bilance
  CHECK: dotnet test CrmMes.Api.Tests --filter FullyQualifiedName~SectorPackTests -nologo -v q
  EXPECT: Non superati:\s+0\. Superati:\s+[1-9]
  EVIDENCE: 02/10/2026 sera "Non superati: 0. Superati: 11"; 8 pack Available; import EPLAN→BOM, wire/nutrition documenti, DM37 generate, SAL ProgressCertificate, cert-31 su MaterialLot, scales ScaleReading; web `/pacchetti-settore`. Hardware bilance reali resta campo

- [x] G14: le tre suite di test verdi dopo tutto il lavoro
  CHECK: dotnet test CrmMes.sln -nologo -v q
  EXPECT: Non superati:\s+0\.
  EVIDENCE: 02/10/2026 sera API 408 + Desktop 122 + Web 75 + Console 18 = 623, Non superati: 0

- [x] G15: client completo non solo Windows: tutte le aree disponibili anche sulla piattaforma web
  EVIDENCE: 02/10/2026 sera: create clienti/materiali; ubicazioni; pack; SdI; board planning scrivibile; piano capacità; strumenti/CAPA/presenze; docs API. Terminale shop-floor resta /tecnici + desktop

- [ ] G16: hosting a pagamento e prodotto con referenze (dipendono dal titolare: acquisto del piano, primi clienti)
  EVIDENCE: pending — non automatizzabile in codice
