# Native victory and Reset All Data evidence

Inspected the isolated game's `Assembly-CSharp.dll` with Mono.Cecil. Run
`powershell -NoProfile -ExecutionPolicy Bypass -File tests\Inspect-NativeReset.ps1`
to check the relevant method calls against that assembly.

## Victory

- `PointsManager.PixelMined()` increments `totalPixelsMined`. When it reaches
  `totalPixelsFromStart` and `EndingManager.ending` is false, it invokes
  `OnAllPixelsCleared`.
- `EndingManager.Start()` subscribes `LevelCleared(int)` to that event.
  `LevelCleared` sets `EndingManager.ending = true` and starts `EndSequence()`.
  The sequence hides gameplay UI, runs the ending effects, and displays the
  ending text. The leaderboard hooks `LevelCleared` after the native method
  returns, so the native victory action has already been accepted.
- `WorldManager.DamageTile()` calls `GoldenPixelManager.CollectedGoldenPixel`
  only for the special golden tile conditions. That method starts a separate
  collection effects coroutine. `CollectSequenceFinished()` shows a screen
  that explicitly reports pixels still left. Golden Pixel collection therefore
  is not the victory signal.
- `SaveManager.SaveAndReloadScene()` quick saves and loads the current scene;
  it has no victory call. Scene loads are not leaderboard reset hooks.

## Reset All Data

- `SettingsManager.ResetPrompt()` only activates `resetConfirmation`.
  `SettingsManager.GoBack()` deactivates it; neither calls the save reset.
- The confirmation action `SettingsManager.ResetAllProgress()` invokes
  `SaveManager.ResetAllProgress()` directly. The leaderboard hooks the latter
  at entry, after the confirmation path has chosen to run the effective reset.
- Native `SaveManager.ResetAllProgress()` calls `DeleteAllSaves()` and reloads
  the active scene. `DeleteAllSaves()` deletes the `save` directory under
  `Application.persistentDataPath`. The mod does not call either method or
  change their native behavior.
- `AutoSave()`, `QuickSaveGame()`, and `SaveAndReloadScene()` do not call the
  full reset and have no leaderboard reset hook.

## Manual validation

The isolated copy shares `Application.persistentDataPath` with the original
installation. Back up the whole
`%USERPROFILE%\AppData\LocalLow\BATTLE LAB\10 Million Pixels Demo` directory
before launching it. Never use the Reset All Data confirmation on valuable
progress unless deleting that progress is intended.

1. With the game closed, compile and install the DLL into the isolated copy.
   Launch that copy and use test TikTok gifts to populate more than five
   viewers. Confirm the five visible rows and normal ongoing scoring.
2. Open Settings → Reset All Data, then cancel. Confirm the same scores remain.
3. In a disposable save, confirm Reset All Data. Confirm the podium shows
   `Aguardando destruidores...` and remains visible in playable scenes.
   Send another gift and confirm its first destroyed pixel scores as 1.
4. In a separate disposable run, win by clearing all pixels through normal
   gameplay. Confirm the podium clears once when the native ending begins,
   remains empty through the ending/any scene reload, and a later valid gift
   starts at zero.
5. While scores are present, exercise pause, menus, quick save, autosave,
   ordinary reload, participant reorder, and ball expiry. Confirm none clears
   the podium. If an old ball or poison effect resolves after a reset, confirm
   it cannot restore an old viewer's score.

The automated service test covers full ranking removal, fresh scoring,
duplicate victory callbacks, and stale owner rejection. The Mono.Cecil check
covers native routing. The actual Unity menu and rendered podium need the
manual checks above; destructive menu actions are never invoked by tests.
