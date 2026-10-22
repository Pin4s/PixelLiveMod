param(
    [string]$GameDir = (Join-Path $env:TEMP 'PixelLiveMod-10MillionPixels-Isolated')
)

$cecil = Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll'
$game = Join-Path $GameDir '10 Million Pixels Demo_Data\Managed\Assembly-CSharp.dll'
if (!(Test-Path -LiteralPath $cecil) -or !(Test-Path -LiteralPath $game)) {
    throw 'The isolated game copy and BepInEx Mono.Cecil.dll are required.'
}
Add-Type -Path $cecil
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($game)

function Method($typeName, $methodName) {
    $type = $assembly.MainModule.Types | Where-Object Name -eq $typeName
    if (!$type) { throw "Missing native type $typeName" }
    $method = $type.Methods | Where-Object Name -eq $methodName
    if (!$method) { throw "Missing native method $typeName.$methodName" }
    return $method
}

function Calls($method, $target) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -and $_.Operand.ToString().Contains($target)
    }).Count -gt 0
}

$break = Method 'WorldManager' 'BreakTile'
$damage = Method 'WorldManager' 'DamageTile'
$collision = Method 'PhysicsAgent' 'OnCollisionEnter2D'
$hit = Method 'PhysicsTile' 'GotHit'
$lightning = Method 'LightningBall' 'StrikeTile'
$poison = $assembly.MainModule.Types | Where-Object Name -eq 'PoisonTile'
$iterator = $poison.NestedTypes | Where-Object { $_.Name.StartsWith('<Poisoning>') }
if (!$iterator) { throw 'Missing PoisonTile.Poisoning iterator' }
$moveNext = $iterator.Methods | Where-Object Name -eq 'MoveNext'

$checks = @(
    @{ Name='BreakTile calls PixelMined'; Passed=(Calls $break 'PointsManager::PixelMined') },
    @{ Name='BreakTile sets tile type to zero'; Passed=(@($break.Body.Instructions | Where-Object {
        $_.Operand -and $_.Operand.ToString().Contains('Tile::type') -and $_.OpCode.Name -eq 'stfld'
    }).Count -gt 0) },
    @{ Name='DamageTile calls BreakTile'; Passed=(Calls $damage 'WorldManager::BreakTile') },
    @{ Name='Ball collision calls GotHit'; Passed=(Calls $collision 'PhysicsTile::GotHit') },
    @{ Name='GotHit calls DamageTile'; Passed=(Calls $hit 'WorldManager::DamageTile') },
    @{ Name='GotHit starts poison'; Passed=(Calls $hit 'PoisonTile::StartPoisoning') },
    @{ Name='Lightning strike calls DamageTile'; Passed=(Calls $lightning 'WorldManager::DamageTile') },
    @{ Name='Poison coroutine calls DamageTile'; Passed=(Calls $moveNext 'WorldManager::DamageTile') },
    @{ Name='Poison coroutine propagates poison'; Passed=(Calls $moveNext 'PoisonTile::PoisonNewTile') },
    @{ Name='Poison child starts poisoning'; Passed=(Calls (Method 'PoisonTile' 'PoisonNewTile') 'PoisonTile::StartPoisoning') }
)

foreach ($check in $checks) {
    if (!$check.Passed) { throw "Native destruction check failed: $($check.Name)" }
    "PASS $($check.Name)"
}
"Verified $($checks.Count) native destruction paths in the isolated game assembly."
