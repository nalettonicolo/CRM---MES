# Collegare una macchina al gestionale

Serve per l'interconnessione richiesta da Industria 4.0 e Transizione 5.0: la macchina scambia dati in automatico con il gestionale (stato, pezzi, allarmi), senza trascrizioni a mano.

## Come funziona
1. Nel gestionale: **Macchine**, doppio click sulla macchina, **Collega macchina**. Compaiono l'indirizzo e un token: il token si vede **una volta sola**, copialo subito.
2. Su un PC della rete di reparto (o un mini PC vicino alla macchina) installa Python 3 e le librerie: `pip install asyncua paho-mqtt requests`.
3. Copia `config.example.json` in `config.json` e inserisci l'id della macchina, il token e la sorgente dei dati:
   - **OPC UA** (macchine recenti, controllori Siemens, Fanuc, Heidenhain...): indirizzo `opc.tcp://...` e gli identificativi dei nodi di stato, contapezzi, scarti e allarme; li fornisce il costruttore o si leggono con un client OPC UA (UaExpert).
   - **MQTT** (sensori IoT, retrofit su macchine vecchie): broker e topic di stato, pezzi e allarme. Rinomina `_mqtt_example` in `mqtt` e togli la sezione `opcua`.
   - `state_map` traduce i codici della macchina negli stati del gestionale: Running, Idle, Setup, Stopped, Alarm, Off.
4. Avvia `python gateway.py config.json`. Per farlo partire da solo all'accensione: Utilità di pianificazione di Windows, oppure un servizio systemd su Linux.

Il gateway invia un dato a ogni cambio di stato più un segnale ogni minuto. Se la rete cade, tiene le letture in `pending-readings.json` e le rispedisce appena può.

## Cosa si vede nel gestionale
Per ogni macchina e per ogni giornata: minuti in marcia, ferma, in allarme, in attrezzaggio; pezzi prodotti (calcolati dal contapezzi, anche se si azzera); disponibilità; ultimi allarmi. Un silenzio di oltre 15 minuti conta come "nessun dato", mai come marcia.

## Sicurezza
- Ogni macchina ha il suo token, salvato nel gestionale solo in forma cifrata (hash). Generarne uno nuovo revoca il vecchio; **Scollega** lo revoca del tutto.
- Il token dà accesso solo all'invio dei dati di quella macchina, non al resto del gestionale.
- Tieni il token solo nel `config.json` del PC gateway. Non va inviato in chat o per e-mail.
- Il gateway esce solo verso l'indirizzo HTTPS del gestionale: non serve aprire porte verso l'esterno.

## Documentazione per la perizia Transizione 5.0
Il collegamento documenta i requisiti di interconnessione: scambio automatico di dati con il gestionale e registrazione di stati e produzione. La perizia tecnica resta a carico di un ingegnere o perito iscritto all'albo.
