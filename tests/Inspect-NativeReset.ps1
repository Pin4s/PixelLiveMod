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

function Method([string]$typeName, [string]$methodName) {
    $type = $assembly.MainModule.Types | Where-Object Name -eq $typeName
    if (!$type) { throw "Missing native type $typeName" }
    $method = $type.Methods | Where-Object Name -eq $methodName
    if (!$method) { throw "Missing native method $typeName.$methodName" }
    return $method
}

function Calls($method, [string]$target) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -and $_.Operand.ToString().Contains($target)
    }).Count -gt 0
}

$mined = Method 'PointsManager' 'PixelMined'
$endingStart = Method 'EndingManager' 'Start'
$victory = Method 'EndingManager' 'LevelCleared'
$golden = Method 'GoldenPixelManager' 'CollectedGoldenPixel'
$goldenFinish = Method 'GoldenPixelManager' 'CollectSequenceFinished'
$prompt = Method 'SettingsManager' 'ResetPrompt'
$cancel = Method 'SettingsManager' 'GoBack'
$confirmed = Method 'SettingsManager' 'ResetAllProgress'
$reset = Method 'SaveManager' 'ResetAllProgress'
$delete = Method 'SaveManager' 'DeleteAllSaves'
$reload = Method 'SaveManager' 'SaveAndReloadScene'
$autosave = Method 'SaveManager' 'AutoSave'
$quickSave = Method 'SaveManager' 'QuickSaveGame'

$checks = @(
    @{ Name='PixelMined invokes OnAllPixelsCleared'; Passed=(Calls $mined 'Action`1<System.Int32>::Invoke') -and (Calls $mined 'EndingManager::ending') },
    @{ Name='EndingManager subscribes LevelCleared'; Passed=(Calls $endingStart 'EndingManager::LevelCleared') -and (Calls $endingStart 'PointsManager::add_OnAllPixelsCleared') },
    @{ Name='LevelCleared marks ending and starts EndSequence'; Passed=(Calls $victory 'EndingManager::ending') -and (Calls $victory 'EndingManager::EndSequence') },
    @{ Name='Golden Pixel collection does not call victory'; Passed=!(Calls $golden 'EndingManager::LevelCleared') -and !(Calls $goldenFinish 'EndingManager::LevelCleared') },
    @{ Name='ResetPrompt only opens confirmation'; Passed=(Calls $prompt 'GameObject::SetActive') -and !(Calls $prompt 'SaveManager::ResetAllProgress') },
    @{ Name='GoBack closes confirmation without reset'; Passed=(Calls $cancel 'GameObject::SetActive') -and !(Calls $cancel 'SaveManager::ResetAllProgress') },
    @{ Name='Confirmed settings action calls native full reset'; Passed=(Calls $confirmed 'SaveManager::ResetAllProgress') },
    @{ Name='Native full reset deletes saves then reloads scene'; Passed=(Calls $reset 'SaveManager::DeleteAllSaves') -and (Calls $reset 'SceneManager::LoadScene') },
    @{ Name='Save deletion targets persistent save directory'; Passed=(Calls $delete 'Application::get_persistentDataPath') -and (Calls $delete 'Directory::Delete') },
    @{ Name='Save/reload and autosave do not call full reset'; Passed=!(Calls $reload 'SaveManager::ResetAllProgress') -and !(Calls $autosave 'SaveManager::ResetAllProgress') -and !(Calls $quickSave 'SaveManager::ResetAllProgress') }
)

foreach ($check in $checks) {
    if (!$check.Passed) { throw "Native completion check failed: $($check.Name)" }
    "PASS $($check.Name)"
}
"Verified $($checks.Count) native victory/reset paths in the isolated game assembly."
