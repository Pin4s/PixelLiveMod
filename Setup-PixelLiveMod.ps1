param(
    [string]$GamePath,
    [string]$InstallPath,
    [switch]$NoLaunch,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$project = $PSScriptRoot
$gameName = '10 Million Pixels Demo.exe'
$connector = Join-Path $project 'tiktok-connector'

function Step([string]$message) { Write-Host "[PixelLiveMod] $message" }
function Assert-Success([string]$task) {
    if ($LASTEXITCODE -ne 0) { throw "$task falhou (exit code $LASTEXITCODE)." }
}
function Download([string]$url, [string]$path) {
    if (Test-Path -LiteralPath $path) { return }
    Step "Baixando $([IO.Path]::GetFileName($path))"
    $partial = "$path.download"
    try {
        Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
        Move-Item -LiteralPath $partial -Destination $path -Force
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}
function Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }
function Game-Architecture([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        [void]$stream.Seek(0x3c, [IO.SeekOrigin]::Begin)
        $header = $reader.ReadInt32()
        [void]$stream.Seek($header + 4, [IO.SeekOrigin]::Begin)
        $machine = $reader.ReadUInt16()
        if ($machine -eq 0x8664) { return 'x64' }
        if ($machine -eq 0x014c) { return 'x86' }
        throw "Arquitetura PE não reconhecida: $machine"
    } finally { $reader.Dispose() }
}
function Resolve-GameFolder([string]$inputPath) {
    if ([string]::IsNullOrWhiteSpace($inputPath)) {
        $inputPath = Read-Host 'Cole a pasta do jogo que contém 10 Million Pixels Demo.exe'
    }
    $resolved = (Resolve-Path -LiteralPath $inputPath).Path
    if (Test-Path -LiteralPath $resolved -PathType Leaf) { $resolved = Split-Path -Parent $resolved }
    if (!(Test-Path -LiteralPath (Join-Path $resolved $gameName))) {
        throw "Não encontrei $gameName em $resolved"
    }
    if (!(Test-Path -LiteralPath (Join-Path $resolved '10 Million Pixels Demo_Data\Managed\Assembly-CSharp.dll'))) {
        throw 'A pasta não contém os assemblies Unity Mono do jogo.'
    }
    return [IO.Path]::GetFullPath($resolved).TrimEnd('\')
}

try {
    $source = Resolve-GameFolder $GamePath
    if ([string]::IsNullOrWhiteSpace($InstallPath)) {
        $InstallPath = Join-Path $env:TEMP 'PixelLiveMod-10MillionPixels-Isolated'
    }
    $target = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
    if ($source.Equals($target, [StringComparison]::OrdinalIgnoreCase) -or
        $source.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $target.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A cópia isolada deve ficar fora da pasta do jogo original.'
    }
    $arch = Game-Architecture (Join-Path $source $gameName)
    Step "Origem: $source"
    Step "Cópia isolada: $target ($arch)"
    if ($DryRun) {
        Step 'Simulação concluída; nenhum arquivo foi alterado.'
        exit 0
    }
    if (Get-Process -Name '10 Million Pixels Demo' -ErrorAction SilentlyContinue) {
        throw 'Feche o jogo antes de preparar ou atualizar a cópia isolada.'
    }

    $toolRoot = Join-Path $env:LOCALAPPDATA 'PixelLiveMod\tools'
    $downloads = Join-Path $env:LOCALAPPDATA 'PixelLiveMod\downloads'
    New-Item -ItemType Directory -Path $toolRoot,$downloads,$target -Force | Out-Null

    # Keep build tools local to this user. Existing suitable installs are reused.
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $sdkReady = $false
    if ($dotnet) {
        $sdkReady = @(& $dotnet.Source --list-sdks 2>$null | Where-Object { $_ -match '^(8|9|10|[1-9][0-9])\.' }).Count -gt 0
    }
    if (!$sdkReady) {
        $dotnetHome = Join-Path $toolRoot 'dotnet'
        $installScript = Join-Path $downloads 'dotnet-install.ps1'
        Download 'https://dot.net/v1/dotnet-install.ps1' $installScript
        Step 'Instalando .NET SDK 10 localmente'
        & $installScript -Channel '10.0' -InstallDir $dotnetHome -NoPath
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Instalação do .NET SDK falhou.' }
        $env:DOTNET_ROOT = $dotnetHome
        $env:PATH = "$dotnetHome;$env:PATH"
        $dotnetExe = Join-Path $dotnetHome 'dotnet.exe'
    } else { $dotnetExe = $dotnet.Source }
    if (!(Test-Path -LiteralPath $dotnetExe)) { throw 'dotnet.exe não encontrado após a instalação.' }

    $node = Get-Command node -ErrorAction SilentlyContinue
    $npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
    $nodeReady = $false
    if ($node -and $npm) {
        $versionText = (& $node.Source --version).TrimStart('v')
        $nodeReady = ([version]$versionText).Major -ge 20
    }
    if (!$nodeReady) {
        if (![Environment]::Is64BitOperatingSystem) { throw 'Node.js 20+ requer Windows x64 neste instalador.' }
        Step 'Consultando a versão LTS oficial do Node.js'
        $versions = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json'
        $lts = $versions | Where-Object { $_.lts -ne $false -and $_.files -contains 'win-x64-zip' } | Select-Object -First 1
        if (!$lts) { throw 'Não encontrei um pacote Node.js LTS para Windows x64.' }
        $fileName = "node-$($lts.version)-win-x64.zip"
        $baseUrl = "https://nodejs.org/dist/$($lts.version)"
        $nodeZip = Join-Path $downloads $fileName
        Download "$baseUrl/$fileName" $nodeZip
        $checksums = (Invoke-WebRequest -Uri "$baseUrl/SHASUMS256.txt" -UseBasicParsing).Content
        $line = ($checksums -split "`n" | Where-Object { $_ -match "\s+$([regex]::Escape($fileName))\s*$" } | Select-Object -First 1)
        if (!$line) { throw 'Checksum oficial do Node.js não encontrado.' }
        $expected = ($line -split '\s+')[0].ToLowerInvariant()
        if ((Sha256 $nodeZip) -ne $expected) { throw 'Checksum do Node.js não confere.' }
        $nodeHome = Join-Path $toolRoot "node-$($lts.version)-win-x64"
        if (!(Test-Path -LiteralPath (Join-Path $nodeHome 'node.exe'))) {
            Step 'Extraindo Node.js LTS'
            Expand-Archive -LiteralPath $nodeZip -DestinationPath $toolRoot -Force
        }
        $env:PATH = "$nodeHome;$env:PATH"
        $npmExe = Join-Path $nodeHome 'npm.cmd'
    } else { $npmExe = $npm.Source }
    if (!(Test-Path -LiteralPath $npmExe)) { throw 'npm.cmd não encontrado após a instalação.' }

    Step 'Copiando o jogo para a pasta isolada'
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
    }

    $bepVersion = '5.4.23.5'
    $assetName = "BepInEx_win_${arch}_$bepVersion.zip"
    Step "Consultando BepInEx $bepVersion oficial"
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/BepInEx/BepInEx/releases/tags/v$bepVersion" -Headers @{ 'User-Agent' = 'PixelLiveMod-Setup' }
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if (!$asset) { throw "Asset $assetName não encontrado no release oficial." }
    $bepZip = Join-Path $downloads $assetName
    Download $asset.browser_download_url $bepZip
    if ($asset.digest -match '^sha256:([0-9a-fA-F]{64})$' -and (Sha256 $bepZip) -ne $Matches[1].ToLowerInvariant()) {
        throw 'Checksum do BepInEx não confere.'
    }
    Step "Instalando BepInEx $bepVersion na cópia isolada"
    Expand-Archive -LiteralPath $bepZip -DestinationPath $target -Force
    if (!(Test-Path -LiteralPath (Join-Path $target 'BepInEx\core\BepInEx.dll'))) {
        throw 'BepInEx.dll não apareceu na cópia isolada.'
    }

    Step 'Compilando o plugin C#'
    Push-Location $project
    try {
        & $dotnetExe build '.\PixelLiveMod.csproj' -c Release "-p:GameCopy=$target"
        Assert-Success 'Build do plugin'
    } finally { Pop-Location }
    $builtDll = Join-Path $project 'bin\Release\netstandard2.1\PixelLiveMod.dll'
    $plugins = Join-Path $target 'BepInEx\plugins'
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    $installedDll = Join-Path $plugins 'PixelLiveMod.dll'
    Copy-Item -LiteralPath $builtDll -Destination $installedDll -Force
    if ((Sha256 $builtDll) -ne (Sha256 $installedDll)) { throw 'A DLL instalada difere da compilada.' }

    Step 'Instalando pacotes e compilando o conector TikTok'
    Push-Location $connector
    try {
        & $npmExe ci
        Assert-Success 'npm ci'
        & $npmExe run build
        Assert-Success 'Build do conector'
    } finally { Pop-Location }
    if (!(Test-Path -LiteralPath (Join-Path $project '.env'))) {
        Copy-Item -LiteralPath (Join-Path $project '.env.example') -Destination (Join-Path $project '.env')
    }
    if (!(Test-Path -LiteralPath (Join-Path $connector 'config.json'))) {
        Copy-Item -LiteralPath (Join-Path $connector 'config.example.json') -Destination (Join-Path $connector 'config.json')
    }

    Step 'Pronto: plugin e conector compilados; DLL instalada na cópia isolada.'
    Step 'Para TikTok LIVE, configure TIKTOK_USERNAME em .env e execute npm start em tiktok-connector.'
    if (!$NoLaunch) {
        Step 'Abrindo o jogo modificado'
        Start-Process -FilePath (Join-Path $target $gameName) -WorkingDirectory $target -WindowStyle Normal
    }
} catch {
    Write-Error "Instalação interrompida: $($_.Exception.Message)"
    exit 1
}
