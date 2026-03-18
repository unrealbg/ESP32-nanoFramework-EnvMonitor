# ESP32-MQTT Environmental Monitor

## Table of Contents

- [Introduction](#introduction)
- [Requirements](#requirements)
- [Tested Hardware](#tested-hardware)
- [Setup](#setup)
- [Usage](#usage)
- [Remote Management (TCP Listener & MQTT Commands)](#remote-management-tcp-listener--mqtt-commands)
- [Secure OTA Updates (nanoFramework ESP32)](#secure-ota-updates-nanoframework-esp32)
- [OTA External Modules & HealthModule (Step-by-Step)](#ota-external-modules--healthmodule-step-by-step)
- [Troubleshooting](#troubleshooting)
- [WebServer with API Endpoints](#webserver-with-api-endpoints)
- [Index Page](#index-page)
- [Relay Control](#new-feature-relay-control-with-toggle-button)
- [Additional Functionalities](#additional-functionalities)
- [Project Images](#project-images)
- [Contributing](#contributing)
- [Acknowledgements](#acknowledgements)
- [Changelog](#changelog)
- [License](#license)

## Introduction

This project integrates an ESP32 device with temperature and humidity sensors (DHT21, AHT10, SHTC3) using C# and the nanoFramework for development. It features MQTT client capabilities for real-time remote data communication and a WebServer component that provides RESTful API endpoints for sensor data access and device control via HTTP requests. The system is ideal for IoT applications requiring flexible communication and interaction interfaces.

Note: Only one sensor is active at a time. The sensor (DHT21, AHT10, or SHTC3) must be selected during the initial setup, and they do not work simultaneously.

Visit our demo portal at: [iot.unrealbg.com](https://iot.unrealbg.com)

I acknowledge that the code may not be perfect and there are certainly areas that can be improved. I am a self-taught programmer, and coding is my hobby. I welcome any constructive criticism and feedback.

## Requirements

- ESP32 development board
- DHT21, AHT10, or SHTC3 temperature and humidity sensor
- MQTT broker (e.g., Mosquitto)

### nanoFramework Firmware Version

- The required nanoFramework firmware version depends on the NuGet packages used in this project.
- To ensure compatibility, use the firmware version that matches the latest tested state of this repository.
- The last tested firmware version for this project was **1.12.4.289**.
- You can check your current firmware version using the **Device Explorer** in the nanoFramework extension for Visual Studio or by running:

```sh
nanoff --platform esp32 --target <YOUR_ESP32_TARGET> --serialport <YOUR_COM_PORT> --list
```

- If needed, update your firmware to the version used in this project:
  
```sh
nanoff --platform esp32 --target <YOUR_ESP32_TARGET> --serialport <YOUR_COM_PORT> --masserase --update --fwversion 1.12.4.14
```

- Important: Replace `<YOUR_ESP32_TARGET>` with the correct target for your device (e.g., ESP32_WROOM_32, ESP32_S3, ESP32_C3). You can find the available targets by running:

```sh
nanoff --platform esp32 --listtargets
```

- Also, replace `<YOUR_COM_PORT>` with the actual serial port where your ESP32 device is connected (e.g., `COM31` on Windows or `/dev/ttyUSB0` on Linux/macOS).
- If you decide to use a **newer firmware version**, you **must also update the corresponding NuGet packages** in your project to ensure compatibility. Check for updates in Visual Studio's **NuGet Package Manager** and make sure all dependencies align with the firmware version you are using.
- For detailed firmware update instructions, visit the [nanoFramework documentation](https://docs.nanoframework.net/content/getting-started-guides/getting-started-managed.html).

## Tested Hardware

This project has been tested and confirmed to work with the following hardware:

### ✅ Supported ESP32 Boards
- ESP32-S3  
- ESP32 Wroom32 DevKit
- ESP32-WROOM-32 DevKit (38 Pins)
- ESP-WROOM-32 ESP-32S
- Wemos/Lolin D32 ESP32

### ❌ Not Working With:
- ESP32-C3 (known compatibility issues)

### ✅ Supported Sensors:
- AHT10  
- AHT20 + BMP280  
- SHTC3  
- DHT21  

If you test the project with other hardware, feel free to contribute feedback!

## Setup

1. **NanoFramework Installation:**  
   Install the C# nanoFramework on your ESP32 device. Detailed instructions are available [here](https://docs.nanoframework.net/content/getting-started-guides/getting-started-managed.html).

2. **Sensor Connection:**  
   Connect the DHT21, AHT10, or SHTC3 sensor to your ESP32 device as per the sensor's documentation. Select the sensor in the code during setup.

3. **MQTT Broker Setup:**  
   Set up an MQTT broker (like Mosquitto) on your network. Note down the hostname and port number.

4. **Code Configuration:**  
   For nanoFramework it's best to keep secrets out of firmware. This project loads Wi‑Fi and MQTT settings from the device filesystem:

   Create `I:\config\device.config` on the device with `key=value` pairs:

   - `wifi.ssid=YourWifiSsid`
   - `wifi.password=YourWifiPassword`
   - `mqtt.broker=your-broker-hostname`
   - `mqtt.port=1883`
   - `mqtt.tls=false`
   - `mqtt.user=yourUser`
   - `mqtt.pass=yourPass`
   - `sensor.type=SHTC3` (or `DHT21`, `AHT10`)

   Device identity (name/location) remains in `DeviceSettings`.

   Web/TCP login credentials are stored separately in `I:\config\credentials.txt`:

   - Line 1: username
   - Line 2 (recommended): `sha256:<salt-hex>:<64-hex>`
     (legacy formats `sha256:<64-hex>` and plaintext are still accepted and will be migrated after first successful login)

5. **Code Deployment:**  
   Compile and upload the code to your ESP32 device.

## Usage

Once the setup is complete, the ESP32 device will start publishing temperature and humidity data from the selected sensor (DHT21, AHT10, or SHTC3) to an MQTT topic. Subscribe to this topic to receive real-time updates. The system also supports publishing custom messages to the MQTT topic.

## Remote Management (TCP Listener & MQTT Commands)

The device can be remotely managed over the network using the built-in TCP Listener. By default, the TCP service listens on port 31337. You can connect using a standard terminal (e.g., PuTTY, netcat, or telnet) to the device's IP address and port.

### TCP Listener

Once authenticated, you are presented with a command-line interface, allowing you to monitor device status, view sensor data, control the relay, manage logs, and perform administrative actions such as reboot or password change.

Below is an example session after logging in:

![TCPListener Session Example](https://vps.unrealbg.com/updates/tcp.png)

### TCP Commands

- **uptime**: Displays device uptime.
- **temp**: Shows the current temperature reading.
- **humidity**: Shows the current humidity reading.
- **publishtemp**: Publishes the current temperature to MQTT.
- **status**: Shows temperature, humidity, and uptime.
- **publishuptime**: Publishes device uptime to MQTT.
- **getipaddress**: Shows the device's IP address.
- **help**: Lists all available commands.
- **info**: Shows device details (device name, firmware version, IP, uptime, sensor interval).
- **ping `<IP>`**: Sends a ping request (currently unsupported).
- **setinterval `<milliseconds>`**: Sets the sensor read interval.
- **relay on|off|status**: Controls or checks relay status.
- **diagnostic**: Shows diagnostic information (free memory).
- **getlogs**: Retrieves the device logs.
- **clearlogs**: Clears all device logs.
- **changepassword**: Changes login credentials. Usage: changepassword <user> <pass>.
- **whoami**: Shows current user information.
- **exit**: Exits the TCP session.
- **reboot**: Reboots the device.

### Authentication
**Default credentials:**  
Username: `admin`  
Password: `admin`

---

### MQTT Commands

Publish commands to the respective MQTT topics below:

- **Relay Control (`home/<DeviceName>/switch`):**
  - `on`: Turns the relay ON.
  - `off`: Turns the relay OFF.
  - `status`: Publishes relay status.

- **System Commands (`home/<DeviceName>/system`):**
  - `uptime`: Publishes device uptime.
  - `reboot`: Reboots the device.
  - `getip`: Publishes the device's IP address.
  - `firmware`: Publishes firmware version.
  - `platform`: Publishes platform information.
  - `target`: Publishes target hardware information.
  - `getLogs`: Publishes recent device logs.
  - `clearLogs`: Clears device logs and publishes confirmation.

- **Error Logging (`home/<DeviceName>/errors`):**
  - Publish error messages for logging and debugging.

---

## Secure OTA Updates (nanoFramework ESP32)

Over-the-air updates let the device securely download, verify, and load application modules without reflashing the base firmware.

### Features
- HTTPS download with TLS 1.2 and CA-based server verification.
- Integrity check: SHA-256 of every downloaded file.
- Transactional writes with .bak rollback on failure.
- Dependency pre-load, then main app load and invoke.
- Status via MQTT and interactive control via TCP console.
- Small packager tool that emits manifest.json + hashes.

### Components
- Runtime:
  - OtaManager.cs — orchestrates manifest fetch, download, verify, write, load, and finalization.
  - OtaManifest.cs, OtaFile.cs, OtaUtil.cs, OtaCrypto.cs, Sha256Lite.cs
  - Config.cs — OTA configuration (paths, topics, behavior, entry point).
- Transport:
  - MQTT OTA control integrated with existing broker.
  - TCP console command ota (URL/status/reboot).
- Packaging:
  - OtaPackager — CLI to compute SHA-256 and generate `manifest.json` (and `HASHES.txt`).

---

### How it works

1) Device downloads `manifest.json` (HTTPS).
2) For each file:
   - Downloads bytes.
   - Verifies SHA-256 matches manifest.
   - Writes to `I:/data/app/<name>` with safe backup (`.bak`).
3) Loads all dependencies (.pe) except main.
4) Loads main app `.pe` and invokes entry method.
5) Writes version file and optionally reboots/cleans old files.

Status messages are published to MQTT during each step.

---

### Requirements

- **Time/TLS**
  - Device time must be synced (SNTP is used in the project; keep it enabled).
  - A trusted CA certificate for your HTTPS server must be available.
    - Preferred: embedded PEM in OtaCertificates.cs.
    - Override (if present): `I:\ota_root_ca.pem` (PEM). Use a CA/intermediate, not the leaf cert.

- **Storage**
  - OTA app dir: `I:/data/app`
  - Version file: `I:/data/app/CurrentVersion.txt`

- **Entry point in App.pe**
  - Public static parameterless method (default: `Entry.Start()`).
  - If different, set in Config.cs:
    - `EntryTypeName` (fully qualified if namespaced)
    - `EntryMethodName` (“Start” or “Main”)

---

### Configuration (OTA/Config.cs)

- **Identity and MQTT topics:**
  - `DeviceId` = `Settings.DeviceSettings.DeviceName`
  - Topics:
    - Cmd: `home/{DeviceId}/ota/cmd`
    - Status: `home/{DeviceId}/ota/status`
- **Storage:**
  - `AppDir = "I:/data/app"`
  - `VersionFile = "I:/data/app/CurrentVersion.txt"`
- **Behavior:**
  - `MainAppName = "App.pe"`
  - `RebootAfterApply = true`
  - `CleanAfterApply = true`
  - `EntryTypeName = "Entry"`
  - `EntryMethodName = "Start"`

---

### Packaging updates

Use the packager to generate `manifest.json` and file hashes for the .pe artifacts you’ll host.

- Inputs: folder with your .pe files (built for nanoFramework)
- Outputs: `manifest.json` and `HASHES.txt`
- Options:
  - `--main=App.pe` sets which file is the main (ensures it loads last).
  - `--base-url=https://host/path/` prefixes URLs in manifest.
  - `--include=App.pe;Lib.pe` explicitly includes only listed files.

Example:
```powershell
# From the repo root (adjust paths)
dotnet run --project .\ESP32-NF-MQTT-DHT\Tools\OtaPackager\OtaPackager.csproj `
  -- "E:\build\ota\pe" "1.0.3" "E:\build\ota\out" `
  --main=App.pe `
  --base-url=https://example.com/esp32/
```

This creates:
- `out\manifest.json`
- `out\HASHES.txt`
- Copies .pe files into `out\` (hashes printed to console and HASHES.txt)

Manifest example:
```json
{
  "version": "1.0.3",
  "files": [
    {
      "name": "Lib.pe",
      "url": "https://example.com/esp32/Lib.pe",
      "sha256": "<sha256-of-Lib.pe>"
    },
    {
      "name": "App.pe",
      "url": "https://example.com/esp32/App.pe",
      "sha256": "<sha256-of-App.pe>"
    }
  ]
}
```
Note: main app (`App.pe`) is listed last by the packager.

---

### Hosting

- Upload `manifest.json` and all listed `.pe` files to your HTTPS server.
- Serve .pe as binary:
  - Content-Type: `application/octet-stream`
  - Disable compression (no gzip/deflate) for `.pe`
- Ensure URLs in `manifest.json` match exactly (case-sensitive on many hosts).
- If using a CDN, purge caches or add version query strings.

---

### Triggering an update

**Option A — via MQTT**
- Topic: `home/{DeviceId}/ota/cmd`
- Payload: either a raw URL string or JSON with url
  - Raw string:
    - `https://example.com/esp32/manifest.json`
  - JSON:
    - `{"url":"https://example.com/esp32/manifest.json"}`

Progress/status is published to:
- Topic: `home/{DeviceId}/ota/status`
- JSON message: `{ "ts": "...", "state": "DOWNLOADING|VERIFYING|WRITTEN|APPLIED|REBOOT|..." , "msg": "..." }`

**Option B — via TCP console (port 31337)**
- Commands:
  - `ota status` — shows installed version and files in `I:/data/app`
  - `ota url <manifestUrl>` — runs the update flow
  - `ota reboot` — reboots the device
- Other TCP commands are listed in the welcome banner.

---

### Versioning and cleanup

- After a successful apply:
  - Version is written to `I:/data/app/CurrentVersion.txt`.
  - If `CleanAfterApply = true`, any old `.pe` (not in the manifest) and `*.bak` are removed.
  - If `RebootAfterApply = true`, the device reboots.

- **Update gating:**
  - The device compares the new `manifest.version` against `CurrentVersion.txt`. If not greater, it reports “UPTODATE” and skips.

---

### Troubleshooting (OTA)

- **SHA mismatch + tiny size (e.g., 231 bytes)**
  - The URL in `manifest.json` likely points to a non-.pe file (HTML/JSON/error/redirect).
  - Fix: ensure the file’s URL is the .pe, not `manifest.json` or an index page. Verify response is 200 OK and binary.
  - Cross-check: compare server file hash with `HASHES.txt`.

- **“Entry type/method not found”**
  - Set `EntryTypeName` and `EntryMethodName` in Config.cs to your actual entry (public static Start/Main).
  - Loader also tries common fallbacks and scans all types, but config is authoritative.

- **TLS failures**
  - Ensure time is synced (SNTP).
  - Provide correct CA PEM (Let’s Encrypt intermediate/root or your CA):
    - Preferred embedded PEM in OtaCertificates.cs.
    - Optional override at `I:\ota_root_ca.pem` (PEM format).

- **HTTP non-200 statuses**
  - The client requires `200 OK`. Fix server paths/auth and ensure no redirects are required.

- **CDN or caching issues**
  - Purge caches or add version query parameters to file URLs.

---

## OTA External Modules & HealthModule (Step-by-Step)

### Overview
- The project supports **external modules** delivered via **OTA** without rebuilding the base firmware.
- `ModuleManager` loads `.pe` files from `I:/data/app/modules` on boot and starts the modules.
- Two kinds of external modules are supported:
  - **Strong**: classes implementing `ESP32_NF_MQTT_DHT.Modules.Contracts.IModule`.
  - **Duck-typed**: classes with the following members **(no compile-time references to this project)**:
    - `public string Name { get; }`
    - `public void Start()`
    - `public void Stop()`
    - Optional DI hook: `public void Init(object serviceProvider)`
- The OTA Manager expects a JSON manifest over HTTPS. After OTA, modules found in `ModulesDir` are loaded on boot.

### Paths & Configuration
- `OTA.Config.AppDir`: `I:/data/app`
- `OTA.Config.ModulesDir`: `I:/data/app/modules`
- OTA behavior: `RebootAfterApply`, `CleanAfterApply`
- HTTPS Root CA: `I:\ota_root_ca.pem` or `Settings.OtaCertificates.RootCaPem`

### Example External Module: **HealthModule**
- Separate project: `HealthModule.Ext` (Class Library — .NET nanoFramework)
- `AssemblyName`: `HealthModule` → produces `HealthModule.pe`
- Duck-typed implementation (no compile-time dependency on the main project)
- Publishes JSON with `freeMemory` and `uptime` to MQTT topic: `home/{DeviceName}/health`.

### Building a `.pe` for an External Module
1) Create a **Class Library (.NET nanoFramework)** and set `AssemblyName` to e.g. `HealthModule`.  
2) Implement the duck-typed API (`Name`/`Start`/`Stop` and optionally `Init(object serviceProvider)` for DI).  
3) **Build** → take `HealthModule.pe` from `bin/Debug` or `bin/Release`.  
4) Compute **SHA-256** (lowercase hex) of the `.pe` file.

### Example OTA Manifest (module-only)
- File example: `ESP32-NF-MQTT-DHT/OTA/Manifests/health-manifest.json`
- Contents:
```json
{
  "version": "1.0.3",
  "files": [
    { "name": "modules/HealthModule.pe", "url": "https://your.cdn/ota/modules/HealthModule.pe", "sha256": "lowercase_sha256_here" }
  ]
}
```
Notes:
- `version` must be **greater** than the value stored in `I:/data/app/CurrentVersion.txt`.
- A manifest **without** `App.pe` is supported (module-only update). OTA will **not** try to start `App.pe` if it’s not in the manifest.

### Deploying OTA (Module)
1) Upload `HealthModule.pe` to a public **HTTPS** URL (valid root CA).  
2) Upload the manifest JSON to a public **HTTPS** URL.  
3) Trigger OTA:
   - **MQTT**: publish to `home/{DeviceName}/ota/cmd`  
     Payload: `{"url":"https://your.cdn/ota/health-manifest.json"}`
   - **TCP console**: `ota url https://your.cdn/ota/health-manifest.json`
4) The device downloads the `.pe`, validates SHA-256, writes it to `I:/data/app/modules`, and on **next boot** the module loads.

### Boot-Time Loading Flow
- Startup calls `ModuleManager.LoadFromDirectory(ModulesDir)`.
- For each `.pe`:
  - If a class implements `IModule`, it’s created via DI or with a parameterless ctor.
  - If **duck-typed**, reflection looks for members `Name`/`Start`/`Stop`.  
    If `Init(serviceProvider)` exists, the DI container is passed in.
- After registration, `Start()` is invoked for all modules.

### Expected Logs
- `Discovered and registered OTA modules: N` on boot.  
- `Starting module: ...` per module.  
- HealthModule publishes log line:  
  `[HealthModule] published to home/{Device}/health: {json}`
- For external `.pe` modules you may see `No debugging symbols available` — that’s normal.

### Troubleshooting (Modules)
- **OTA says “load failed — rolled back”**: ensure the version is higher, SHA-256 matches, and HTTPS CA is valid.  
- **Module not loading**: verify the `.pe` is in `I:/data/app/modules` and that the class exposes `Name/Start/Stop` (or implements `IModule`).  
- **No MQTT publish**: confirm MQTT connectivity. Duck-typed modules obtain the MQTT client via DI in `Init(serviceProvider)` → `IMqttClientService.get_MqttClient`.  
- **Reflection exception on startup**: avoid `Type.GetProperty` / `Type.GetMethod` with BindingFlags in nanoFramework. Enumerate `GetMethods()` and use getter names like `get_XXX`.  
- **Time is 1970**: wait for SNTP sync; a more robust SNTP with timeout/fallback is included.

### Security
- OTA uses **HTTPS**. Provide a valid **root CA** (`I:\ota_root_ca.pem` or `Settings.OtaCertificates.RootCaPem`) for the domain serving your `.pe` and manifest.  
- Recommendation: host the files on a trusted CDN and/or sign them.

### Developer Notes for Duck-Typed Modules (Tips)
- Use `get_XXX` for property getters, because `GetProperty` isn’t available in nanoFramework.  
- Avoid `Type.GetMethod` with `BindingFlags` (can throw `ArgumentException` on some platforms). Instead, enumerate `GetMethods()` and match by name/signature.  
- Need `DeviceName`? Look for `ESP32_NF_MQTT_DHT.Settings.DeviceSettings.get_DeviceName` or a static `DeviceName` field.  
- For DI access: in `Init(serviceProvider)`, call the provider’s `GetService(<FullType>)` via reflection to obtain services you need.

### Licensing
- Respect dependency licenses. Be cautious about reusing code and certificates.

## Troubleshooting

- **Bootloop Prevention:**   
   If the device repeatedly enters a bootloop, ensure that the correct GPIO pins are configured for your sensor(s) and relay. Verify the pin assignments in the code (e.g., in `BaseSensorService`, `AhtSensorService`, and `RelayService`) match your hardware setup. Incorrect pin assignments can cause improper initialization, leading to boot issues.

- **MQTT Connection Issues:**  
  Ensure the MQTT broker hostname and port number are correctly set in the code.

- **Sensor Data Accuracy:**  
  If temperature and humidity readings are inaccurate, verify the sensor connections and calibrations.

## WebServer with API Endpoints

This project includes a web server that serves API endpoints for structured HTTP request handling. It allows for remote sensor data retrieval and device control via a RESTful interface.

**Note:** For optimal web server performance, it is recommended to use ESP devices with more memory, such as **ESP32-S3**. The project has been tested and works without issues on ESP32-S3, thanks to its increased storage and processing resources.

### WebServer Features

- **API Endpoints**: API endpoints for interacting with the DHT21, AHT10, and SHTC3 sensors and device control.
- **Real-Time Data Access**: Fetch real-time temperature and humidity data.
- **Device Control**: Endpoints for controlling the ESP32 device functionalities.

### API Endpoints

1. **Temperature Data**:

   - Endpoint: `/api/temperature`
   - Method: `GET`
   - Description: Returns the current temperature reading from the selected sensor in JSON format.

2. **Humidity Data**:

   - Endpoint: `/api/humidity`
   - Method: `GET`
   - Description: Returns the current humidity reading in JSON format.

3. **Complete Sensor Data**:
   - Endpoint: `/api/data`
   - Method: `GET`
   - Description: Returns both temperature and humidity readings along with the sensor type in a structured JSON format.

## Index Page

An index page has been added for a user-friendly display of sensor data through a simple web interface. It dynamically updates the temperature, humidity, date, time, and sensor type using JavaScript.

### Index Page Content

The index page provides a clean and responsive UI for real-time monitoring:

- Displays temperature and humidity readings.
- Shows the current date, time, and sensor type.

## New Feature: Relay Control with Toggle Button

The project now includes a relay control feature accessible via the web interface. A button has been added to the index page to allow users to toggle the relay state between **ON** and **OFF**. The button’s label and color dynamically update based on the current relay state.

- **"Turn On"**: Green button appears when the relay is off.
- **"Turn Off"**: Red button appears when the relay is on.

### Relay Control API

You can control the relay using the following API endpoint:

1. **Toggle Relay**

   - Endpoint: `/api/toggle-relay`
   - Method: `POST`
   - Description: Toggles the relay state between ON and OFF. The response returns the updated relay state (`isRelayOn`).

2. **Check Relay Status**
   - Endpoint: `/api/relay-status`
   - Method: `GET`
   - Description: Returns the current state of the relay (ON or OFF) in JSON format.

### Updated Index Page

The web interface has been updated with a new **Relay Control** button. This button will dynamically update based on the state of the relay:

- **Turn On**: Green button displayed when the relay is off.
- **Turn Off**: Red button displayed when the relay is on.

### Example HTML:

```html
<button id="relayButton" class="btn-on" onclick="toggleRelay()">
  Loading...
</button>
async function toggleRelay() { const response = await fetch('/api/toggle-relay',
{ method: 'POST' }); const data = await response.json();
updateRelayButton(data.isRelayOn); } function updateRelayButton(isRelayOn) {
const relayButton = document.getElementById('relayButton'); if (isRelayOn) {
relayButton.textContent = 'Turn Off'; relayButton.className = 'btn-off'; } else
{ relayButton.textContent = 'Turn On'; relayButton.className = 'btn-on'; } }
```

## Additional Functionalities

### Relay Control

You can control a relay module to manage connected devices based on sensor data or MQTT commands. This enables automated environmental control or remote power management.

### SHTC3 Sensor Support

In addition to the DHT21 and AHT10 sensors, the project now supports the SHTC3 sensor, offering flexibility in sensor choice based on precision and calibration needs.

## Project Images

### Without Breadboard Power Supply

![Without Breadboard Power Supply](https://user-images.githubusercontent.com/3398536/201364419-9ba27b3e-6638-490f-90f5-0e380fbc2900.png)

### With Breadboard Power Supply

![With Breadboard Power Supply](https://user-images.githubusercontent.com/3398536/201362770-067d8fe3-254e-48e2-8cec-10766898c3e6.png)

### Additional Project Image

<img src="https://user-images.githubusercontent.com/3398536/200621001-ac09d95d-9f0f-4ef7-bf87-8b352f5f1a17.jpg" width="200" /> <img src="https://github.com/unrealbg/NF.Esp32.Mqtt.Dht21/assets/3398536/4cde056b-9c80-467d-a05f-481e5dae26ea" width="200" /> <img src="https://github.com/unrealbg/NF.Esp32.Mqtt.Dht21/assets/3398536/1e2c80ee-9d03-45d0-8034-ce2004eb4cf1" width="200" />

## Contributing

Contributions are welcome! Please follow these steps to contribute:

1. Fork the repository.
2. Create a new branch (`git checkout -b feature-branch`).
3. Make your changes.
4. Commit your changes (`git commit -m 'Add new feature'`).
5. Push to the branch (`git push origin feature-branch`).
6. Open a Pull Request.

## Acknowledgements

- [nanoFramework](https://www.nanoframework.net/)
- [Mosquitto MQTT](https://mosquitto.org/)
- Special thanks to everyone who contributed to this project.

## Changelog

### [v1.3.0] - 2025-08-15
- **OTA External Modules**: Support for loading `.pe` modules from `I:/data/app/modules` at boot.
- **Duck-Typed Modules**: Run modules with `Name/Start/Stop` (optional `Init(object serviceProvider)`).
- **HealthModule Example**: Publishes `uptime` and `freeMemory` to `home/{DeviceName}/health`.
- **Module-Only OTA**: Manifests without `App.pe` are now supported for safe module updates.

### [v1.2.0] - 2025-08-12
- Added secure OTA updates (HTTPS + CA validation, SHA-256 verification, transactional apply, MQTT/TCP control).
- Included OtaPackager CLI and OTA runtime components.

### [v1.1.0] - 2025-03-02
- **Sensor Services:**  
  - Introduced `BaseSensorService` to centralize sensor reading, error handling, and resource management.
  - Updated AHT10, AHT20+BMP280, DHT21, and SHTC3 sensor services to inherit from `BaseSensorService`.
- **MQTT Client:**  
  - Improved connection stability with dynamic ClientId generation, locking to prevent duplicate connection attempts, and exponential backoff with jitter.
- **Network & Internet:**  
  - Refactored `ConnectionService` with proper locking and enhanced IP validation.
  - Improved `InternetConnectionService` with resource cleanup and stop-signal checks.
- **Web Server & Relay:**  
  - Enhanced `WebServerService` and `RelayService` with thread-safe operations and proper resource disposal.

### [v1.0.2] - 2025-01-15
- Added support for the AHT20+BMP280 sensor.

### [v1.0.1] - 2024-12-13

- Added support for SHTC3 sensor.
- Improved MQTT connection stability.

### [v1.0.0] - 2024-11-01

- Initial release with DHT21 and AHT10 sensors support.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
