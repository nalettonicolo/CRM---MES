# Strumento per i flussi di dati dal pannello Admin — progetto e scopo (v1)

Definito l'8 ottobre 2026 da un confronto con il titolare. Ambito: **tutti i moduli**, configurabile dall'Admin, trigger **su evento**, con la possibilità di avere un passo di **approvazione umana prima di applicare una modifica** (i "fermi intermedi" richiesti). Esempio guida del titolare: *"l'ufficio tecnico ha fatto una revisione → avvisa l'ufficio acquisti e manda il file, avvisa il caporeparto, avvisa il magazzino, avvisa gli operatori dedicati"*.

## Cosa è, in una frase

Un motore che, quando succede un evento scelto (es. "modifica tecnica applicata"), esegue in ordine una sequenza di passi configurata dall'Admin: avvisare un ruolo, oppure fermarsi e aspettare un'approvazione prima di andare avanti. Non è un ERP di workflow visuale con rami e incroci: quello è il passo 2, descritto sotto.

## V1 (questa consegna): cosa fa davvero

- **Trigger**: su evento. Un registro di eventi conosciuti (`CrmMes.Core/Flows/DataFlowEvents.cs`), come il registro delle schermate dello Strumento Layout. Primo evento cablato: `engineering.change.applied` (una modifica tecnica viene applicata, in `EngineeringController.Apply`).
- **Passi disponibili**: `NotifyRole` (crea una notifica in app per ogni utente di un ruolo, con un messaggio che può usare i dati dell'evento, es. `{{productCode}}`), `RequireApproval` (ferma l'esecuzione; notifica il ruolo indicato; l'esecuzione riprende solo con approvazione esplicita, o si ferma per sempre con il rifiuto).
- **Notifiche**: solo **in app** (non email: non esiste ancora un servizio di invio email nel sistema, serve una scelta del titolare su quale fornitore usare — vedi "cosa non c'è"). Una notifica per utente, non per ruolo: alla creazione si risolve il ruolo negli utenti che lo hanno in quel momento.
- **Pannello Admin (web)**: pagina `/flussi-dati` per creare/modificare/attivare un flusso (nome, evento scelto da un elenco, passi in sequenza), ed elenco delle esecuzioni, con quelle in attesa di approvazione da poter approvare o rifiutare.
- **Un solo evento cablato** in questa consegna, per provare il meccanismo end-to-end con un caso vero. Aggiungere un nuovo evento (es. "DDT emesso", "fattura scaduta") è una modifica piccola e localizzata una volta che il motore esiste: si aggiunge la voce al registro e una chiamata nel controller giusto.

## Cosa NON fa in v1 (deliberatamente)

- **Niente passi che toccano il magazzino o assegnano in automatico** ("scala da magazzino", "assegna in automatico"). Il titolare li ha chiesti, ma toccano direttamente le giacenze e le commesse: li aggiungo in un secondo passo, con test propri, dopo che il meccanismo di base (eventi, passi, approvazione) è verificato e usato almeno una volta sul caso reale.
- **Niente flussi con rami o incroci** ("flussi misti e incrociati"). V1 è una sequenza di passi, uno via l'altro. Rami (se X allora passo A altrimenti passo B) e incroci (un passo che aspetta due condizioni insieme) sono una versione 2 del motore, con un modello a grafo invece che a lista: si progetta meglio avendo in mano un caso vero che lo richiede.
- **Niente editor visuale a blocchi collegati.** Il pannello Admin v1 è una lista ordinata di passi da compilare, come già fatto per lo Strumento Layout e i Campi personalizzati — non un disegno di nodi e frecce.
- **Niente email o notifiche esterne.** Serve una decisione del titolare: quale servizio di invio (SMTP proprio, SendGrid, altro) e le sue credenziali. Finché non c'è, resta in app.
- **Niente programma desktop.** Come già fatto per il Collaudo e CE, parte dal web; il desktop si aggiunge se e quando serve davvero usarlo da lì.

## Perché questo ordine

Il rischio più alto non è la sequenza dei passi: è collegare un evento sbagliato a un'azione che cambia dati reali (magazzino, commesse) senza controllo. Per questo v1 si ferma a "avvisare" e "chiedere conferma", e lascia "cambiare qualcosa da solo" al passo successivo, quando il meccanismo di base avrà già girato su un caso reale.
