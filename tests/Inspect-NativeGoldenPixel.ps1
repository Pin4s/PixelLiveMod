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

function Method([string]$typeName, [string]$methodName, [string]$nested = '') {
    $type = $assembly.MainModule.Types | Where-Object Name -eq $typeName
    if ($nested) { $type = $type.NestedTypes | Where-Object Name -eq $nested }
    if (!$type) { throw "Missing native type $typeName/$nested" }
    $method = $type.Methods | Where-Object Name -eq $methodName
    if (!$method) { throw "Missing native method $typeName/$nested.$methodName" }
    return $method
}

function Contains($method, [string]$operand) {
    return @($method.Body.Instructions | Where-Object {
        $_.ToString().Contains($operand)
    }).Count -gt 0
}

$spawn = Method 'WorldManager' 'SetGoldenPixels'
$collision = Method 'PhysicsAgent' 'OnCollisionEnter2D'
$hit = Method 'PhysicsTile' 'GotHit'
$damage = Method 'WorldManager' 'DamageTile'
$collect = Method 'GoldenPixelManager' 'CollectedGoldenPixel'
$effects = Method 'GoldenPixelManager' 'MoveNext' '<PostGoldenPixelCollectionEffects>d__29'
$mined = Method 'PointsManager' 'PixelMined'
$endingStart = Method 'EndingManager' 'Start'
$cameraStart = Method 'CameraController' 'Start'
$victory = Method 'EndingManager' 'LevelCleared'
$endSequence = Method 'EndingManager' 'MoveNext' '<EndSequence>d__14'
$quit = Method 'GoldenPixelCollectedScreen' 'QuitToMenu'

$checks = @(
    @{ Name='Golden Pixel is tile type 12345 with indexed health and position'; Passed=(Contains $spawn '12345') -and (Contains $spawn 'Tile::currentHealth') -and (Contains $spawn 'goldenPixelPositions') },
    @{ Name='Ball collision reads PhysicsTile and calls GotHit'; Passed=(Contains $collision 'GetComponent<PhysicsTile>') -and (Contains $collision 'PhysicsTile::GotHit') },
    @{ Name='Native collision distinguishes Golden Ball color 7'; Passed=(Contains $collision 'PhysicsAgent::ballColor') -and (Contains $collision 'ldc.i4.7') },
    @{ Name='Golden tile requires goldenBall flag in GotHit'; Passed=(Contains $hit '12345') -and (Contains $hit 'goldenBall') -and (Contains $hit 'WorldManager::DamageTile') },
    @{ Name='Native damage performs collection and breaks tile'; Passed=(Contains $damage 'GoldenPixelManager::CollectedGoldenPixel') -and (Contains $damage 'WorldManager::BreakTile') },
    @{ Name='Collection starts separate effects and screen'; Passed=(Contains $collect 'PostGoldenPixelCollectionEffects') -and (Contains $effects 'GoldenPixelCollectedScreen::SetupCollectionScreen') },
    @{ Name='Mining completion event has ending and camera listeners'; Passed=(Contains $mined 'OnAllPixelsCleared') -and (Contains $endingStart 'EndingManager::LevelCleared') -and (Contains $cameraStart 'CameraController::InitEnd') },
    @{ Name='Native ending starts sequence without scene reload'; Passed=(Contains $victory 'EndingManager::EndSequence') -and !(Contains $endSequence 'SceneManager::LoadScene') },
    @{ Name='Collection screen menu exit is separate'; Passed=(Contains $quit 'SceneManager::LoadScene') }
)

foreach ($check in $checks) {
    if (!$check.Passed) { throw "Native Golden Pixel check failed: $($check.Name)" }
    "PASS $($check.Name)"
}
"Verified $($checks.Count) native Golden Pixel and victory facts."
