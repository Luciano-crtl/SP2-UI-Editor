# SP2 UI Editor: guide

Changes the interface of SimplePlanes 2 without code, while the game runs. Needs BepInEx 5 (x64) in the game.

## Only using other people's packages

1. Close the game and copy `plugin\SP2UIEditor.dll` to `BepInEx\plugins`.
2. Start the game once and close it.
3. Copy the `.sp2ui` files to `BepInEx\config\SP2UIEditor\packages`.
4. In the game, **Ctrl+Shift+U** opens the list to switch each package on or off.

The editor's program is not needed and no server is opened. If a button of a package wants to call a function of the game, the game asks first: only say yes to people you trust.

## Editing

1. Do step 1 above and start the game.
2. Open `editor\SP2UIEditor.exe`. The game asks whether it may connect: click **Okay**.
   To never be asked, set `Enabled = true` in `BepInEx\config\com.luciano.sp2uieditor.cfg`.
3. Choose English at the top right. **Pick in the game**, click an element and change its properties on the right. **Ctrl+Z** in the window undoes.
4. **Packages** > type a name > **New package**, then **Save**.

| I want to... | Do |
|---|---|
| change a text | select the text part, under "Texts and parts" |
| put something new | **Add: Inside** or **Add: After** |
| make a window | **Panels** > name > **New panel**, then **Add: Inside** |
| open the window with a button | add a button, and under **What it does** choose "Open or close a panel" |
| tie a control to a mod | add a toggle or slider, and under **What it does** choose "Mod setting" |
| change the color of every button | **Shared styles** |
| send it to a friend | send the file in the packages folder |
| use it in your own mod | **Packages** > **Export XML and C#** |

If a screen was already open when the plugin loaded, close and open it to see which file it came from.

## Claude

    claude mcp add --scope user sp2ui -- C:\path\mcp\SP2UI.Mcp.exe

It sees the interface and can select elements. It only changes anything with **Claude may edit** checked in the window, and can never approve a function of the game.

## Problems

| What happens | What to do |
|---|---|
| the window stays at "Waiting for the game" | see whether the game shows the question; look for `[UIEDIT]` in `BepInEx\LogOutput.log` |
| an edit is gone after a game update | **Packages** shows "the element is gone"; make the edit again |
| something looks wrong | switch the package off (Ctrl+Shift+U) or take the file out of the folder |
| revoke permissions for functions | with the game closed, delete `BepInEx\config\SP2UIEditor\trust.json` |
