# Mantis Lite API Simulator

This project provides a **simulated Mantis Lite API** for developers to test integrations **without needing the camera or production system**.

The simulator generates realistic telemetry data and images so client software can be developed and validated before connecting to a real device.

---

# Features

- Live **1-second telemetry endpoint**
- Historical **1-second data queries**
- Simulated **camera image stream (6 FPS)**
- **Simulation modes** for testing edge cases
- **Status endpoint** for debugging
- Pre-seeded history buffer

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
  "DateTime": "03/26/2026 01:46:27 PM",
  "NHVcz": 995.3,
  "NHVdil": 925.6,
  "DRE": 97.7,
  "SI": 0.82,
  "FF": 13.3,
  "FH": 0.059,
  "Flame_Stability": 94,
  "Distance": 210,
  "Ambient_Temp": 20,
  "RH": 70,
  "Flare_Type": 2,
  "Frame_Rate": 719,
  "SN_Ratio": 0.814,
  "DQI_Flag": 1,
  "Sensor_Temp": 26.5,
  "Data_Cubes": 1,
  "Edge_Pixels": 154,
  "Flame_Pixels": 760,
  "Apparent_Temp": 1005.4,
  "Visible_Emissions": 0,
  "Pilot_Status": 1,
  "UTC": "2026-03-26 17:46:27",
  "LocationName": "LOCATION 1"
}
```

Notes:

- Data updates **once per second**
- Multiple requests within the same second return the same sample
- `DateTime` is a formatted local timestamp
- `UTC` is provided as a UTC reference timestamp
- In `missing_live_data` mode, `/api/live1sec` may appear frozen temporarily

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
| `dqiOnly` | Optional filter (1 = return only rows where `DQI_Flag == 1`) |

Example request:

```bash
curl "http://127.0.0.1:8477/api/history1sec?from=2026-03-26T17:44:00Z&to=2026-03-26T17:46:00Z"
```

Example request with filtering:

```bash
curl "http://127.0.0.1:8477/api/history1sec?from=2026-03-26T17:44:00Z&to=2026-03-26T17:46:00Z&dqiOnly=1"
```

Example response:

```json
{
  "from": "2026-03-26T17:44:00Z",
  "to": "2026-03-26T17:46:00Z",
  "dqiOnly": 0,
  "count": 121,
  "points": [
    {
      "DateTime": "03/26/2026 01:44:00 PM",
      "NHVcz": 1001.5,
      "NHVdil": 932.8,
      "DRE": 98.1,
      "SI": 0.79,
      "FF": 12.7,
      "FH": 0.056,
      "Flame_Stability": 91,
      "Distance": 210,
      "Ambient_Temp": 20,
      "RH": 70,
      "Flare_Type": 2,
      "Frame_Rate": 719,
      "SN_Ratio": 0.812,
      "DQI_Flag": 1,
      "Sensor_Temp": 27.1,
      "Data_Cubes": 3,
      "Edge_Pixels": 151,
      "Flame_Pixels": 784,
      "Apparent_Temp": 1016.3,
      "Visible_Emissions": 0,
      "Pilot_Status": 1,
      "UTC": "2026-03-26 17:44:00",
      "LocationName": "LOCATION 1"
    }
  ]
}
```

Notes:

- Simulator seeds **~15 minutes of history on startup**
- New data is added every second
- Stores up to **24 hours of data**
- In `missing_live_data` mode, `/api/live1sec` may appear frozen for a few seconds while history continues recording normally
- All query parameters (`from`, `to`) must be provided in **UTC**
- The `UTC` field in responses is the authoritative timestamp
- `DateTime` is a formatted local display value and should not be used for calculations

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

- Updates **6 times per second** in normal mode
- Rotating **A–F letters** to show frame changes
- Includes timestamp overlay
- Rotating **A–F letters**
- In `frame_drop` mode, updates may drop to **0–2 FPS**

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
| `missing_live_data` | `/api/live1sec` skips a few seconds of visible updates while `/api/history1sec` still records them |
| `frame_drop` | Image stream temporarily drops to about 0–2 FPS, then returns to 6 FPS |
| `mixed_dqi` | Good and bad `dqi` values are mixed together in the telemetry stream |

PowerShell example:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:8477/api/simulate" `
  -ContentType "application/json" `
  -Body '{"mode":"missing_live_data"}'
```

curl example:

```bash
curl.exe -X POST http://127.0.0.1:8477/api/simulate  -H "Content-Type: application/json"  -d "{"mode":"missing_live_data"}"
```

Return to normal:

```bash
curl.exe -X POST http://127.0.0.1:8477/api/simulate  -H "Content-Type: application/json"  -d "{"mode":"normal"}"
```
Mode notes:
- `missing_live_data` is useful for testing **backfill logic**
- `frame_drop` is useful for testing **image timeout or reconnect logic**
- `mixed_dqi` is useful for testing **DQI filtering logic**

---

# Status Endpoint

Returns debugging information.

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
  "latestTs": "2026-03-26 17:46:27",
  "historyPoints": 902,
  "imageRateHz": 6,
  "liveSuppressed": false
}
```
This endpoint helps confirm:
- simulator is running
- timestamps are updating
- history is accumulating
- current simulation mode
- whether live updates are currently being suppressed in `missing_live_data` mode
---
# Data Shape Reference

| Field | Type | Description |
|---|---|---|
| DateTime | string | Local Formatted Timestamp (Display Only) |
| NHVcz | number | Combustion-Zone Heating Value |
| NHVdil | number | Dilution Heating Value |
| DRE | number | Destruction and Removal Efficiency |
| SI | number | Smoke Index |
| FF | number | Flame Footprint |
| FH | number | Fractional Heat Release |
| Flame_Stability | integer | Flame Stability |
| Distance | number | Distance from the Camera to the Flame (User Input) |
| Ambient_Temp | number | Ambient Temperature (User Input and Defaulted to 20 on Permanent Installs) |
| RH | number | Relative Humidity (User Input and Defaulted to 70 on Permanent Installs) |
| Flare_Type | integer | Value Representing Type of Flare (0 = Unassisted, 1 = Air Assisted, 2 = Steam Assisted, 3 = Pressure Assisted) |
| Frame_Rate | integer | Frame Rate of Sensor (700-720 Expected)|
| SN_Ratio | number | Signal to Noise Ratio |
| DQI_Flag | integer | Internal Data Quality Indicator (1 = Valid, 0 = Invalid) |
| Sensor_Temp | number | Internal Temp of Sensor |
| Data_Cubes | integer | Number of Data Cubes in the 1 Sec Window (0-6 Expected) |
| Edge_Pixels | integer | Number of Pixels Touching the Edge |
| Flame_Pixels | integer | Number of Flame Pixels |
| Apparent_Temp | number | Apparent Temperature |
| Visible_Emissions | integer | Visible Emissions Indicator (1 = On, 0 = Off) |
| Pilot_Status | integer | Pilot Status Indicator (1 = On, 0 = Off) |
| UTC | string | UTC Timestamp (Authoritative Time Reference) |
| LocationName | string | User Input Text Field |

---

# Notes

- Telemetry updates at **1 Hz**
- Images update up to **6 Hz**
- History stored in memory only
- Designed for **integration testing without hardware**

---

# Purpose

This simulator allows developers to fully test integrations with the **Mantis Lite API** without requiring camera hardware or a production system.
