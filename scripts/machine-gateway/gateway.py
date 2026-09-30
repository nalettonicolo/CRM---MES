"""Gateway macchina -> Nicolò MES.

Legge lo stato e i contatori di una macchina via OPC UA (polling) oppure MQTT (sottoscrizione) e li
invia all'API del gestionale con il token della macchina. Tiene in coda su disco le letture quando la
rete o il server non rispondono e le rispedisce appena possibile, quindi non si perdono dati.

Uso:
    pip install asyncua paho-mqtt requests
    python gateway.py config.json

Il file di configurazione (vedi config.example.json) indica l'indirizzo dell'API, l'id della macchina,
il token (generato in Macchine > dettaglio > Collegamento macchina) e da dove leggere i valori.
Il token è un segreto: tenerlo solo nel file di configurazione del PC gateway, mai in chat o e-mail.
"""
import asyncio
import datetime as dt
import json
import os
import sys
import time

import requests

STATES = {"Running", "Idle", "Setup", "Stopped", "Alarm", "Off"}


def now_iso():
    return dt.datetime.now(dt.timezone.utc).isoformat()


class Sender:
    """Sends readings in batches; keeps unsent ones in a JSON file and retries."""

    def __init__(self, config):
        self.url = f"{config['api_url'].rstrip('/')}/api/machine-data/{config['equipment_id']}"
        self.headers = {"X-Machine-Token": config["token"], "Content-Type": "application/json"}
        self.queue_file = config.get("queue_file", "pending-readings.json")
        self.pending = json.load(open(self.queue_file, encoding="utf-8")) if os.path.exists(self.queue_file) else []
        self.last_sent_state = None
        self.last_heartbeat = 0.0
        self.heartbeat_seconds = config.get("heartbeat_seconds", 60)

    def add(self, reading):
        # Send on change, plus a heartbeat so the program knows the machine is still there.
        changed = reading["state"] != self.last_sent_state or reading.get("alarmCode")
        if changed or time.time() - self.last_heartbeat >= self.heartbeat_seconds:
            self.pending.append(reading)
            self.last_sent_state = reading["state"]
            self.last_heartbeat = time.time()
            self.flush()

    def flush(self):
        while self.pending:
            batch = self.pending[:500]
            try:
                response = requests.post(self.url, headers=self.headers, json=batch, timeout=30)
            except requests.RequestException as error:
                print(f"Server non raggiungibile, {len(self.pending)} letture in coda: {error}")
                break
            if response.status_code == 401:
                print("Token rifiutato: rigeneralo nel gestionale e aggiorna config.json.")
                break
            if response.status_code == 429:
                time.sleep(5)
                continue
            if response.status_code >= 400:
                print(f"Letture scartate dal server ({response.status_code}): {response.text}")
            self.pending = self.pending[len(batch):]
        with open(self.queue_file, "w", encoding="utf-8") as handle:
            json.dump(self.pending, handle)


def map_state(raw, mapping):
    """Machine-specific codes (e.g. 1=run, 2=alarm) to the program's states."""
    state = mapping.get(str(raw), raw)
    return state if state in STATES else "Stopped"


async def run_opcua(config, sender):
    from asyncua import Client

    source = config["opcua"]
    async with Client(url=source["endpoint"]) as client:
        nodes = {name: client.get_node(node_id) for name, node_id in source["nodes"].items()}
        while True:
            values = {name: await node.read_value() for name, node in nodes.items()}
            reading = {
                "timestamp": now_iso(),
                "state": map_state(values.get("state"), source.get("state_map", {})),
                "pieceCounter": int(values["pieces"]) if values.get("pieces") is not None else None,
                "scrapCounter": int(values["scrap"]) if values.get("scrap") is not None else None,
                "alarmCode": str(values["alarm"]) if values.get("alarm") not in (None, 0, "", "0") else None,
            }
            sender.add(reading)
            await asyncio.sleep(source.get("poll_seconds", 5))


def run_mqtt(config, sender):
    import paho.mqtt.client as mqtt

    source = config["mqtt"]
    latest = {"state": "Stopped"}

    def on_message(_client, _userdata, message):
        field = source["topics"].get(message.topic)
        if field is None:
            return
        value = message.payload.decode("utf-8").strip()
        latest[field] = value
        sender.add({
            "timestamp": now_iso(),
            "state": map_state(latest.get("state"), source.get("state_map", {})),
            "pieceCounter": int(float(latest["pieces"])) if latest.get("pieces") else None,
            "scrapCounter": int(float(latest["scrap"])) if latest.get("scrap") else None,
            "alarmCode": latest.get("alarm") if latest.get("alarm") not in (None, "", "0") else None,
        })

    client = mqtt.Client()
    if source.get("username"):
        client.username_pw_set(source["username"], source.get("password"))
    client.on_message = on_message
    client.connect(source["host"], source.get("port", 1883))
    for topic in source["topics"]:
        client.subscribe(topic)
    client.loop_forever()


def main():
    config = json.load(open(sys.argv[1] if len(sys.argv) > 1 else "config.json", encoding="utf-8"))
    sender = Sender(config)
    sender.flush()
    if "opcua" in config:
        asyncio.run(run_opcua(config, sender))
    elif "mqtt" in config:
        run_mqtt(config, sender)
    else:
        sys.exit("Configura una sorgente 'opcua' o 'mqtt' in config.json.")


if __name__ == "__main__":
    main()
