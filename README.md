# Fake Mantis Lite API Simulator

This project provides a **simulated Mantis Lite API** for developers to test integrations **without needing the camera or production system**.

The simulator generates realistic telemetry data and images so client software can be developed and validated before connecting to a real device.

---

# Features

- Live **1-second telemetry endpoint**
- Historical **1-second data queries**
- Simulated **camera image stream (6 FPS)**
- **Simulation modes** for testing edge cases
- **Status endpoint** for debugging

---

# Requirements

- .NET 7 SDK or newer  
- Windows recommended (uses `System.Drawing`)

Check installed version:

```bash
dotnet --version
```

---

# Running the Simulator

From the project directory run:

```bash
dotnet run
```

The API will start on:

```
http://127.0.0.1:8477
```

Verify the server is running:

```bash
curl http://127.0.0.1:8477/
```

Expected response:

```
Fake Mantis Lite API is running.
```

---

# API Endpoints

| Endpoint | Description |
|--------|--------|
| `/` | Health check |
| `/api/live1sec` | Latest telemetry sample |
| `/api/history1sec` | Historical telemetry |
| `/api/image/latest.jpg` | Latest camera frame |
| `/api/simulate` | Force test scenarios |
| `/api/status` | Debug information |

---

# Live Telemetry

Returns the most recent **1-second telemetry sample**.

```
GET /api/live1sec
```

Example request:

```bash
curl http://127.0.0.1:8477/api/live1sec
```

Example response:

```json
{
  "ts": "2026-03-11T15:45:33.3343151Z",
  "empty": false,
  "dqi": 1,
  "cubes": 4,
  "nhvDil": 936.4,
  "nhvCz": 1002.1,
  "si": 0.81,
  "ci": 1.09,
  "vis": false,
  "ff": 12.4,
  "hr": 0.055,
  "pilot": true,
  "dre": 98.7,
  "maxT1": 745.6,
  "maxT2": 751.2,
  "fr": 719,
  "sensorT": 27.8,
  "flamePx": 804,
  "edgePx": 153,
  "fs": 92,
  "intTime": 300,
  "cubePct": 67
}
```

Notes:

- Data updates **once per second**
- Multiple requests within the same second return the same sample
- Timestamps are always **UTC**

---

# Historical Data

Returns telemetry between two timestamps.

```
GET /api/history1sec
```

Parameters

| Parameter | Description |
|---|---|
| `from` | Start timestamp (UTC) |
| `to` | End timestamp (UTC) |
| `dqiOnly` | Optional filter (1 = return only rows where `dqi == 1`) |

Example request:

```bash
curl "http://127.0.0.1:8477/api/history1sec?from=2026-03-11T15:44:00Z&to=2026-03-11T15:46:00Z"
```

Notes:

- Simulator seeds **15 minutes of history at startup**
- New points added every second
- Up to **24 hours of history stored**

---

# Image Stream

Returns the most recent simulated camera frame.

```
GET /api/image/latest.jpg
```

Example:

```bash
curl http://127.0.0.1:8477/api/image/latest.jpg --output frame.jpg
```

Image behavior:

- Updates **6 times per second**
- Includes timestamp overlay
- Rotating **A–F letters** to show frame changes

---

# Simulation Modes

Allows forcing specific test scenarios.

```
POST /api/simulate
```

Available modes:

| Mode | Description |
|----|----|
| `normal` | Default randomized simulation |
| `empty` | No flame detected |
| `bad_dqi` | Data quality failure |
| `high_flame` | Extreme flame conditions |
| `sensor_error` | Sensor malfunction |

PowerShell example:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:8477/api/simulate" `
  -ContentType "application/json" `
  -Body '{"mode":"empty"}'
```

curl example:

```bash
curl.exe -X POST http://127.0.0.1:8477/api/simulate \
 -H "Content-Type: application/json" \
 -d "{\"mode\":\"empty\"}"
```

Return to normal simulation:

```bash
curl.exe -X POST http://127.0.0.1:8477/api/simulate \
 -H "Content-Type: application/json" \
 -d "{\"mode\":\"normal\"}"
```

---

# Status Endpoint

Returns debugging information about the simulator.

```
GET /api/status
```

Example:

```bash
curl http://127.0.0.1:8477/api/status
```

Example response:

```json
{
  "mode": "normal",
  "latestTs": "2026-03-11T16:02:41Z",
  "historyPoints": 902,
  "imageRateHz": 6
}
```

This endpoint helps confirm:

- simulator is running
- timestamps are updating
- history is accumulating
- current simulation mode

---

# Notes

- All timestamps are **UTC**
- Telemetry updates **1 Hz**
- Images update **6 Hz**

---

# Purpose

This simulator allows developers to fully test integrations with the **Mantis Lite API** without requiring camera hardware or a production system.
