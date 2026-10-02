"""Simulatore demo macchina -> Nicolò MES.

Invia letture fittizie (cicli Running/Idle, contapezzi ed energia crescente) all'API del gestionale,
usando lo stesso token e indirizzo del gateway reale. Utile per dimostrazioni e prove senza OPC UA/MQTT.

Uso:
    pip install requests
    python demo_simulator.py config.json

Opzionale in config.json:
    "demo_interval_seconds": 10   — pausa tra un invio e l'altro (default 10)
    "demo_cycles": 0               — 0 = loop infinito; altrimenti numero di letture da inviare
"""
import datetime as dt
import json
import sys
import time

import requests

STATES = ("Running", "Running", "Idle", "Running", "Setup", "Running")


def now_iso():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "config.json"
    config = json.load(open(path, encoding="utf-8"))
    url = f"{config['api_url'].rstrip('/')}/api/machine-data/{config['equipment_id']}"
    headers = {"X-Machine-Token": config["token"], "Content-Type": "application/json"}
    interval = float(config.get("demo_interval_seconds", 10))
    max_cycles = int(config.get("demo_cycles", 0))

    pieces = 500
    energy = 50.0
    cycle = 0
    sent = 0

    print(f"Simulatore demo verso {url} (intervallo {interval}s). Ctrl+C per uscire.")
    while max_cycles == 0 or sent < max_cycles:
        state = STATES[cycle % len(STATES)]
        cycle += 1
        if state == "Running":
            pieces += 5 + (cycle % 3)
            energy += 0.08 + cycle * 0.01
        elif state == "Idle":
            energy += 0.02

        reading = {
            "timestamp": now_iso(),
            "state": state,
            "pieceCounter": pieces,
            "scrapCounter": max(0, pieces // 100),
            "energyKwh": round(energy, 3),
        }
        try:
            response = requests.post(url, headers=headers, json=[reading], timeout=30)
        except requests.RequestException as error:
            print(f"Server non raggiungibile: {error}")
            time.sleep(interval)
            continue

        if response.status_code == 401:
            print("Token rifiutato: rigeneralo nel gestionale e aggiorna config.json.")
            sys.exit(1)
        if response.status_code >= 400:
            print(f"Lettura scartata ({response.status_code}): {response.text}")
        else:
            sent += 1
            body = response.json()
            print(f"[{sent}] {state} pezzi={pieces} energia={energy:.3f} kWh (accettate={body.get('accepted')})")

        time.sleep(interval)


if __name__ == "__main__":
    main()
