# DEV — project structure, commands, build details

_Reference moved out of CLAUDE.md to keep it short. Read when touching code/build. Current progress lives in `md/STATUS.md`; rules and numbers in `md/DESIGN.md`._

## Assembly boundaries (`Assets/_Project/Scripts/`)
| asmdef | Refs | Rule |
|---|---|---|
| `AllIn.Core` | — | Pure C#, **No Engine References**. Loop rules, numbers (`Rules.cs`), time, state, save data classes, PRNG, `Texts.cs` (all strings) |
| `AllIn.Holdem` | Core | Pure C#. `Holdem/Core` = engine, `Holdem/Brain` = AI |
| `AllIn.Game` | Core, Holdem | Unity presentation, input, save IO. **Never re-derive rules here** — ask Core |
| `AllIn.Core.Tests`, `AllIn.Holdem.Tests` | | NUnit `[Test]` only (no `[UnityTest]`). Same files run in Unity Test Runner and the dotnet harness. Unity's NUnit is 3.5: no `Assert.Multiple` |
| `AllIn.Game.Tests` | Game | Unity-only (save round-trip via JsonUtility) |

## Commands (PowerShell, repo root)
```
dotnet run --project tools/dotnet-harness -- test          # all Core + Holdem tests (NUnitLite)
dotnet run --project tools/dotnet-harness -- test --where "class =~ Lotto"
dotnet run --project tools/dotnet-harness -- sim           # loop + holdem sims -> md/SIM_REPORT.md, md/SIM_HOLDEM.md
dotnet run --project tools/dotnet-harness -- sim loop      # or: sim holdem | sim mental | sim luck (-> md/SIM_LUCK.md, q balance) | sim drift (-> md/SIM_DRIFT.md, price-drift check) | sim ladder (-> md/SIM_LADDER_ECON.md, satellite/pub economics grid)
```
Unity (batchmode, editor closed; path `C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Unity.exe`):
```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults <xml> -logFile <log>   # compile + all tests incl. Game save round-trip
Unity.exe -batchmode -quit -projectPath . -executeMethod AllIn.Game.Editor.ProjectSetup.Run            # scenes + build settings (idempotent)
Unity.exe -batchmode -quit -projectPath . -executeMethod AllIn.Game.Editor.ProjectSetup.BuildWindows   # Builds/Win64/AllIn.exe
Builds/Win64/AllIn.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -allinShots <dir>        # dev capture: scripted 30 days, screenshots every screen/event, uses <dir> as save dir
Builds/Win64/AllIn.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -allinHoldemShots <dir>  # dev capture: Pro bot plays a Pub (HUD 4) + a few Main hands (HUD 0); forces luck q=0.3 to shoot the hold'em "무조건 승리"
Builds/Win64/AllIn.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -allinShotsV5 <dir>       # dev capture (v0.5): street day/night, walking, car, clothes shop, wardrobe, car lot, companions, coin luck moment, settlement row
```
Harness compiles `Core/`, `Holdem/`, `Tests/Core`, `Tests/Holdem` via `<Compile Include>` at C# 9 (Unity's version). When the editor is open (batchmode refuses to run), use unity-mcp: `AssetDatabase.Refresh` → check `EditorUtility.scriptCompilationFailed`, run tests via `TestRunnerApi`, build via `BuildPipeline.BuildPlayer`. Unity compile check: unity-mcp once per session (0 errors); if MCP is unavailable (editor closed), use the batchmode commands above; only if neither works write "compile unverified" in STATUS.

## v0.5 code map
- `Core/Style.cs` (Place, Appearance, Wardrobe catalog, Cars, Companion) · `Core/LoopStyle.cs` (MoveTo / BuyClothes / Equip / BuyCar / SellCar / upkeep / v1→v2 migration) · `Core/Paperdoll.cs` (layers, `ICharacterLayerSource`, `CodeLayerSource` = string grids + palettes, pure C#; `DollPose`) · `Core/CarArt.cs`.
- Luck: `Loop.LuckQCoin` / `LuckRng`, `SubTickWalk.LuckTriggered/LuckPending/UseLuck()`; `LadderConfig.LuckQ`, `Ladder.LuckRng`, `LadderHand.LuckTriggered/LuckFlipped`. Rule defaults (`Rules.LuckQCoin`, `HoldemRules.LuckQHoldem`) are 0. UI: `Game/UI/LuckUi.cs` (`LuckOffer`).
- Presentation: `Game/World/WorldView.cs` (street, hero, car, companions, walking), `Game/World/SpriteCache.cs` (Core pixels → Point-filtered sprites), `Game/Loop/ShopScreens.cs` (clothes shop, wardrobe, car lot), `Game/Dev/EditorDebugMenu.cs` (F9 in the editor: companions).
- Saves are `SaveData.version` 2; `Loop.FromSave` migrates v1 (default look, starter uniform, no car, luck stream from the seed).

## Conventions (detail)
- Actions implement `Core/Actions/IAction`. New income/gamble sources plug in the same way.
- UI: UGUI + TextMeshPro built **in code at runtime** (no hand-edited prefabs/scenes). EventSystem uses `InputSystemUIInputModule`. Korean font: Galmuri TTF in `Assets/_Project/Fonts/Resources/`, turned into a dynamic TMP font at boot. Room backgrounds = world sprites under the **URP built-in** `UnityEngine.Rendering.Universal.PixelPerfectCamera` (320×180) — do not add the legacy `com.unity.2d.pixel-perfect` package. Gauges/buttons/popups = Screen Space-Overlay canvas.
- Saves: every saved number is integral (price in whole won, quantity derived) because JsonUtility does not round-trip doubles exactly. `SaveData` lives in Core; JSON IO (`Application.persistentDataPath/allin-save-v1.json`, temp file then replace) lives in Game. Autosave only.
- Test names state intent: `Liquidation_FiresAtTheTickLossReachesMargin_NotBefore`.
