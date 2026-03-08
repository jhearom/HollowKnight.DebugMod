# For any questions or bugs reports, join the [Modding Discord](https://discord.gg/F6Y5TeFQ8j)
[![Discord](https://img.shields.io/discord/879125729936298015.svg?logo=discord&logoColor=white&logoWidth=20&labelColor=7289DA&label=Discord&color=17cf48)](https://discord.gg/F6Y5TeFQ8j)

### To see changes in the recent version, read the [ChangeLog](https://github.com/TheMulhima/HollowKnight.DebugMod/blob/master/ChangeLog.md)   
----------------------------------------------------------------------------------------
                                       FEATURES
----------------------------------------------------------------------------------------
* A completely new toggleable UI in game that provides the following functions:
* Cheats such as invincibility and noclip
* The ability to unlock all charms or repair broken ones
* Change which skills the player has
* Change which items the player has
* Give the player more of consumable resources such as geo and essence
* Respawn bosses
* Hold multiple dream gate positions
* Change the player's respawn point to anywhere in the current scene
* Recall to the set respawn point
* Kill all enemies
* Add HP bars to enemies
* Draw collision boxes for enemies
* Clone or delete any enemy
* Set an enemy's health to 9999
* Change the player's nail damage
* Damage the player
* Change the camera zoom level
* Disable the in game HUD
* Make the player invisible
* Disable the lighting around the player
* Disable the vignette drawn around the player
* Change the time scale of the game
                         PLAYMAKER RUNTIME TRACE (PM TRACE)
----------------------------------------------------------------------------------------
DebugMod includes an opt-in, profile-based multi-probe PM Trace runtime for analysis.

Default behavior:
* OFF by default
* Always starts OFF on launch (no auto-enable from prior session state)
* No gameplay behavior changes when OFF

Runtime controls (F1 Top Menu -> `PM Trace`):
* Enable/Disable Trace (single toggle button based on current state)
* List Profiles
* Next Profile
* Run Script
* Clear Buffer
* Flush Trace
* Reload Config
* Show Status
* Dump Status

Config file:
* `%APPDATA%\\..\\LocalLow\\Team Cherry\\Hollow Knight\\DebugModData\\pmtrace_config.json`
* If missing, it is created automatically with defaults.
* Edit filters/output in this file, then use `Reload Config` in-game.
* Optional script-runner flag:
  * `commandScript.autoRunOnReload` (default `false`)
* A starter template is also generated at:
  `%APPDATA%\\..\\LocalLow\\Team Cherry\\Hollow Knight\\DebugModData\\pmtrace_config.template.windows.json`
* Machine-readable contract artifacts for tooling/analysis:
  * `docs/pmtrace-capabilities.json`
  * `docs/pmtrace-config.schema.json`
  * `docs/pmtrace-record.schema.json`
  * `docs/pmtrace-command-contract.json`
  * `docs/pmtrace-analysis-prompt-template.md`

Default output:
* `%APPDATA%\\..\\LocalLow\\Team Cherry\\Hollow Knight\\DebugModData\\pmtrace\\pmtrace_<timestamp>_<session>.jsonl`

Command script file (manual-first execution path):
* `%APPDATA%\\..\\LocalLow\\Team Cherry\\Hollow Knight\\DebugModData\\pmtrace_commands.txt`
* Execute via PM Trace menu button: `Run Script`
* Optional auto-run on `Reload Config` when `commandScript.autoRunOnReload=true`
* Script format:
  * one PM Trace command per line
  * blank lines and `#` comments allowed
  * command examples:
    * `pmtrace profile activate shriek_hitgate`
    * `pmtrace clear`
    * `pmtrace enable`
    * `pmtrace disable`
    * `pmtrace flush`

Automation command surface:
* `DebugExport.ExecutePmTraceCommand(string commandLine)`
* `DebugExport.GetPmTraceCommandHelp()`
* Supported commands:
  * `pmtrace help`
  * `pmtrace enable|disable|clear|flush|reload|status|dump_status`
  * `pmtrace profiles list`
  * `pmtrace profile activate <name>`
* Command responses are JSON payloads (`ok`, `code`, `message`, optional `data`) for parse-friendly automation.

Notes:
* JSONL rows use a normalized event envelope (`event_type`, timing/session fields, source/target identity, optional payload).
* PM Trace built-in profiles: `default_fsm`, `combat_minimal`, `shriek_hitgate`.
* `fixed_frame_count` is a sampled proxy based on `fixedTime / fixedDeltaTime`.
* `Dump Status` writes a small JSON status snapshot into the PM trace output directory.
* PM Trace now auto-flushes buffered rows when trace is disabled and on application quit (if new rows were captured since the last flush).
* Script runner safety constraints:
  * fixed file path inside `DebugModData` only,
  * strict command allowlist parser reuse (no shell/process execution),
  * command/file size limits,
  * command chaining token rejection (`;`, `&&`, `||`, `|`, etc.),
  * fail-closed on first invalid line.
----------------------------------------------------------------------------------------
                             INSTALLATION (STEAM, WINDOWS)
----------------------------------------------------------------------------------------
1) Download the modding API from here: https://drive.google.com/open?id=0B_b9PFqx_PR9X1ZrWGFxUGdydTg
2) Right click Hollow Knight in Steam -> Properties -> Local Files -> Browse Local Files
3) Create a backup of the game files located here
4) Copy the contents of the modding API zip into this folder (Overwrite files when asked)
5) Copy the contents of this zip into the folder (Overwrite files when asked)
6) This mod should not affect saves negatively, but it is a good idea to back them up anyway.
   Saves are located at %AppData%\..\LocalLow\Team Cherry\Hollow Knight\
----------------------------------------------------------------------------------------
                     UPGRADING SAVE STATES TO SAVE STATES WITH PAGES
----------------------------------------------------------------------------------------
If you're upgrading DebugMod from version `1.4.7` or below, do the following:
1) Update the installed mod.
2) Start Hollow Knight and then exit.
3) Open a file browser and navigate to `%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight`
4) Open DebugMod.GlobalSettings.json in a text editor
5) Modify MaxSaveStates's value from 6 to 10 if you want more save states per page of
   save states. Don't go higher than 10 - not exactly sure what'll happen but it 
   probably won't work as you want.
7) Save and close the file.
8) Back in the file browser, navigate to `Savestates Current Patch` - these are your 
   old save states
9) Move them to `%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\DebugModData\Savestates Current Patch\0`
   - this should make them visible to the updated mod on the first page of the save 
   states. The directory should be empty if you're upgrading from verion `1.4.7` 
   or below. You can delete `%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\Savestates Current Patch`
   if you like.
10) Boot the game back up. You should be good to go!
11) Additionally, you probably want to bind the Next/Previous Page commands to make full
    use of the save state pages feature.
----------------------------------------------------------------------------------------
                                     CREDITS
----------------------------------------------------------------------------------------
Coding - Seanpr<br />
SaveStates/Old Current Patch - 56<br />
UI design and graphics - The Embraced One<br />
Assistance with canvas - KDT<br />
1.5 and A lot of Changes - Mulhima<br />
Multiple SaveStates/Minimal info panel- Cerpin<br />
Improve hitbox viewer - DemoJameson<br />
Multiple SaveState Pages - Magnetic Pizza (and jhearom for porting to cp)<br />
Additional Glitched functionality - pseudorandomhk<br />
Additional Bindable Functions, and Fix Stuff - Flib<br/>
Buttons to directly run bindable actions - flukebull<br/>
Add frame by frame advance code - SFGrenade<br/>
