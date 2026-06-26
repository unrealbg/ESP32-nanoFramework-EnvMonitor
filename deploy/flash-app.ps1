param(
    [string]$SerialPort = "COM8",
    [string]$Target = "ESP32_S3",
    [string]$Configuration = "Release",
    [string]$Address = "0x1B0000",
    [ValidateSet("nanodevice", "bootloader")]
    [string]$Mode = "nanodevice",
    [string]$FileDeployment = "",
    [int]$WaitForDeviceSeconds = 20,
    [switch]$SkipFileDeployment,
    [switch]$NoReset,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "ESP32-NF-MQTT-DHT\ESP32-NF-MQTT-DHT.nfproj"
$outputBin = Join-Path $repoRoot "ESP32-NF-MQTT-DHT\bin\$Configuration\ESP32_NF_MQTT_DHT.bin"

function Format-CommandArgument {
    param([string]$Argument)

    if ($Argument -match '\s') {
        return '"' + $Argument.Replace('"', '\"') + '"'
    }

    return $Argument
}

function Get-MSBuildPath {
    $searchRoots = @(
        "C:\Program Files\Microsoft Visual Studio",
        "C:\Program Files (x86)\Microsoft Visual Studio"
    )

    foreach ($root in $searchRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $match = Get-ChildItem -Path $root -Filter MSBuild.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\MSBuild\Current\Bin\MSBuild.exe" } |
            Select-Object -First 1 -ExpandProperty FullName

        if ($match) {
            return $match
        }
    }

    throw "MSBuild.exe was not found in the standard Visual Studio install locations."
}

function New-DefaultFileDeploymentJson {
    param(
        [string]$DeployDirectory,
        [string]$Port
    )

    $entries = @(
        @{
            DestinationFilePath = "I:\config\device.config"
            SourceFilePath = Join-Path $DeployDirectory "device.config"
        },
        @{
            DestinationFilePath = "I:\config\credentials.txt"
            SourceFilePath = Join-Path $DeployDirectory "credentials.txt"
        },
        @{
            DestinationFilePath = "I:\irc_root_ca.pem"
            SourceFilePath = Join-Path $DeployDirectory "irc_root_ca.pem"
        }
    )

    foreach ($entry in $entries) {
        if (-not (Test-Path $entry.SourceFilePath)) {
            throw "Required deployment file missing: $($entry.SourceFilePath). Create it from the matching .template file in deploy\."
        }
    }

    $payload = [ordered]@{
        serialport = $Port
        files = $entries
    }

    $jsonPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nf-filedeployment-" + [Guid]::NewGuid().ToString("N") + ".json")
    $payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    return $jsonPath
}

function Wait-ForNanoDevice {
    param(
        [string]$Port,
        [int]$TimeoutSeconds
    )

    if ($TimeoutSeconds -le 0) {
        return $true
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $portPattern = "@\s+" + [regex]::Escape($Port) + "\b"

    while ([DateTime]::UtcNow -lt $deadline) {
        $output = (& nanoff --nanodevice --listdevices 2>&1) | Out-String
        if ($output -match $portPattern) {
            Write-Host "nanoFramework device found on $Port."
            return $true
        }

        Write-Host "Waiting for nanoFramework device on $Port..."
        Start-Sleep -Seconds 2
    }

    return $false
}

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

$msbuildPath = Get-MSBuildPath

Write-Host "Rebuilding $projectPath"
& $msbuildPath $projectPath /t:Rebuild /p:Configuration=$Configuration

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $outputBin)) {
    throw "Deployment image not found after build: $outputBin"
}

Write-Host "Deployment image ready: $outputBin"

$includeFileDeployment = ($Mode -eq "nanodevice") -and (-not $SkipFileDeployment)
if ($includeFileDeployment) {
    if ([string]::IsNullOrWhiteSpace($FileDeployment)) {
        $FileDeployment = New-DefaultFileDeploymentJson -DeployDirectory $PSScriptRoot -Port $SerialPort
    }
    elseif (-not (Test-Path $FileDeployment)) {
        throw "File deployment JSON not found: $FileDeployment"
    }
}

if ($Mode -eq "bootloader") {
    $deployArgs = @(
        "--target", $Target,
        "--serialport", $SerialPort,
        "--deploy",
        "--image", $outputBin,
        "--address", $Address
    )

    if (-not $NoReset) {
        $deployArgs += "--reset"
    }

    if (-not $SkipFileDeployment) {
        Write-Warning "File deployment is skipped in bootloader mode. Run again with -Mode nanodevice after nanoCLR is running to upload I:\config files."
    }
}
else {
    $deployArgs = @(
        "--nanodevice",
        "--serialport", $SerialPort,
        "--deploy",
        "--image", $outputBin
    )

    if (-not $NoReset) {
        $deployArgs += "--reset"
    }
}

if ($BuildOnly) {
    if ($Mode -eq "bootloader") {
        Write-Host "BuildOnly specified. Run this when the device is in ESP32 bootloader mode:"
    }
    else {
        Write-Host "BuildOnly specified. Run these to deploy over nanoCLR wire protocol:"
        if ($includeFileDeployment) {
            Write-Host ("nanoff --filedeployment " + (Format-CommandArgument $FileDeployment))
        }
    }

    Write-Host ("nanoff " + (($deployArgs | ForEach-Object { Format-CommandArgument $_ }) -join " "))
    exit 0
}

Write-Host "Starting nanoff deploy..."
if ($Mode -eq "nanodevice") {
    if (-not (Wait-ForNanoDevice -Port $SerialPort -TimeoutSeconds $WaitForDeviceSeconds)) {
        throw "No nanoFramework device responded on $SerialPort. Close Visual Studio Device Explorer/debug sessions or any serial monitor, press RESET, verify the COM port with 'nanoff --listports', then retry. If nanoCLR firmware is missing, run this script once with '-Mode bootloader' first, then run it again normally to upload I:\config files."
    }

    if ($includeFileDeployment) {
        Write-Host "Deploying device files before application reset: $FileDeployment"
        & nanoff --filedeployment $FileDeployment

        if ($LASTEXITCODE -ne 0) {
            throw "nanoff file deployment failed with exit code $LASTEXITCODE. Close Visual Studio Device Explorer/debug sessions or any serial monitor, press RESET, check '-SerialPort $SerialPort', and retry."
        }

        Start-Sleep -Seconds 2
        if (-not (Wait-ForNanoDevice -Port $SerialPort -TimeoutSeconds $WaitForDeviceSeconds)) {
            throw "Device files were deployed, but the nanoFramework device did not come back on $SerialPort before application deployment. Press RESET and rerun the script; files are already on I:\config."
        }
    }
}

Write-Host "Deploying managed application..."
& nanoff @deployArgs

if ($LASTEXITCODE -ne 0) {
    if ($Mode -eq "nanodevice") {
        throw "nanoff deploy failed with exit code $LASTEXITCODE while using nanoCLR wire protocol. Close Visual Studio Device Explorer/debug sessions or any serial monitor, press RESET, check '-SerialPort $SerialPort', and retry. If the device is currently in ESP32 bootloader mode, reset it back into nanoCLR mode; file deployment cannot run in bootloader mode."
    }

    throw "nanoff deploy failed with exit code $LASTEXITCODE. Put the board in ESP32 bootloader mode and run again."
}
