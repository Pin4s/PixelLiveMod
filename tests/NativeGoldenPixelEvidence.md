# Golden Pixel contact and native ending

Inspected the isolated game's `Assembly-CSharp.dll` with Mono.Cecil. Run
`powershell -NoProfile -ExecutionPolicy Bypass -File tests\Inspect-NativeGoldenPixel.ps1`
to repeat the structural checks for this game build.

- `WorldManager.SetGoldenPixels()` creates tiles of type `12345`. Their
  `currentHealth` is a one-based index into `goldenPixelPositions`; a pooled
  `PhysicsTile` exposes the tile's world grid coordinates to collisions.
- `PhysicsAgent.OnCollisionEnter2D()` handles objects tagged `Tile`, reads
  their `PhysicsTile`, and calls `PhysicsTile.GotHit()`. It passes the
  `goldenBall` flag only for native ball color 7. `PhysicsTile.GotHit()` returns
  without damaging a type `12345` tile when that flag is false. The Golden
  Ball's homing behavior is separate from this collision gate.
- For a Golden Ball, `WorldManager.DamageTile()` checks the indexed position,
  calls `GoldenPixelManager.CollectedGoldenPixel()`, updates the native Golden
  Pixel count/effect, and calls `BreakTile()`. `CollectedGoldenPixel()` starts
  its own visual/audio coroutine. After the native three-second breaking time,
  the game presents `GoldenPixelCollectedScreen`, including a keep-mining
  option. Collecting a Golden Pixel alone does not call the ending method.
- `WorldManager.BreakTile()` calls `PointsManager.PixelMined()`. When the native
  total reaches its target, `OnAllPixelsCleared` runs. `CameraController` and
  `EndingManager` subscribe to that event. `EndingManager.LevelCleared()` sets
  `ending` and starts `EndSequence()`. The leaderboard already resets after
  `LevelCleared()`, including stale-owner invalidation.
- `EndSequence()` shows the native ending text after its timed effects. It
  does not reload the scene or erase saves. The Golden Pixel collection screen
  has a separate user-invoked `QuitToMenu()` action that loads the main menu.

The mod observes only a direct `PhysicsAgent` collision with a validated
Golden Pixel tile. A non-Golden Ball then uses `WorldManager.DamageTile()` to
run the native collection path. Once the tile is gone, the mod invokes the
existing `OnAllPixelsCleared` delegate so the native camera and ending
subscribers run. If mining the Golden Pixel already caused the native ending,
the mod does not invoke the event again. A once-per-scene gate prevents
additional contacts from starting another ending; scene load rearms it.

The native collection screen and native ending sequence can both be active
after a direct Golden Pixel win. This follows the game's two independent
sequences; the mod does not change their visual or audio behavior. Check their
presentation in the manual validation below.

## Manual validation

The isolated game shares `%USERPROFILE%\AppData\LocalLow\BATTLE LAB\10 Million
Pixels Demo` with the original installation. Back up that whole directory
before any real win test. Do not use `Reset All Data` as a test shortcut.

1. Close the game before replacing the isolated plugin DLL. Launch the isolated
   copy with a disposable game state and locate a Golden Pixel.
2. Contact it directly with Orange, Green, Yellow, Pink, Inferno, a native
   Golden Ball, and any other available native ball in separate rounds. The
   Golden Pixel should be collected once, the native camera/ending sequence
   should start, and the podium should show `Aguardando destruidores...`.
   Observe both native ending and collection screens during their timed effects.
3. Test an ordinary tile and a special tile other than the Golden Pixel.
   Neither should start the ending. Test a Yellow ray, Green poison, or Inferno
   blast reaching near the Golden Pixel without ball contact; none should win.
4. Let two balls contact the Golden Pixel as closely together as practical.
   Confirm one ending and one leaderboard reset. After a fresh scene loads,
   verify a new gift starts scoring from zero and another direct Golden Pixel
   contact can end that round.

Automated tests cover the color-independent target rule, exclusions,
once-per-round gate, native IL structure, and the existing leaderboard reset.
They do not simulate Unity's collision dispatch or execute a real victory.
