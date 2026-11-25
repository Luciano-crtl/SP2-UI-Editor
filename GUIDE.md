# SP2 UI Editor: guide for people who never programmed

This program changes the interface of SimplePlanes 2 (buttons, texts, colors, windows) while the game runs. You click what you want to change and see the result at once.

**Can it break the game?** The game's files are never touched. Everything can be undone, and nothing is kept until you save. See "Something went wrong" at the end.

**The (!) button** is beside almost everything in the program. Rest the mouse on it, or click it, to read what that thing is and how to fill it in. The **Help** button, at the top, has a summary of this guide.

## 1. Install (only once)

You need BepInEx 5 (x64) in the game. It is what makes mods possible. If you already use an SP2 mod with BepInEx, you have it.

1. Close the game.
2. Copy `plugin\SP2UIEditor.dll` to the folder `BepInEx\plugins`, inside the game's folder. (In Steam: right-click the game > Manage > Browse local files.)
3. Start the game.

**Only want to use an interface someone else made?** Go to part 6.

## 2. Connect

1. With the game running, open `editor\SP2UIEditor.exe`.
2. The game asks whether the program may connect. Click **Okay**.
3. Choose **English** at the top right of the program.

To not be asked again: open `BepInEx\config\com.sp2uieditor.cfg` in Notepad, change it to `Enabled = true`, save it and restart the game.

## 3. Your first change

1. In the program, click **Pick in the game**.
2. Go to the game and click a button. It gets marked.
3. Go back to the program. What can be changed is on the right.
4. At **Color**, click the little colored square and choose another color.
5. Look at the game: the color changed.
6. Click **Undo**. It is as it was.

## 4. The program's window

| Part | What it is for |
|---|---|
| Left: the list | everything on the game's screen; what is further in is inside the item above |
| Middle: the picture | what the game shows; click it to choose |
| Right: the properties | what you can change on the chosen item |
| Top: the buttons | undo, windows of yours, packages, save |

Words used in the program:

| Word | Means |
|---|---|
| Element | anything on the screen: a button, a text, a picture, a box |
| Property | something about the element: color, width, text |
| Box, list, row | elements that hold others and arrange them |
| Anchor | the corner or side of the screen the element is tied to |
| Style class | a ready look; everything that uses the same one looks alike |
| Constant | a value with a name, such as "Main color", used in many places |
| Template | a ready piece you can add |
| Panel | a window of yours inside the game |
| Package | the file that keeps your changes |
| Method | an internal command of the game that a button of yours can call |

How to fill in values:

| Kind | Example |
|---|---|
| Number | `120` (sizes are in points of the screen; use a point for decimals: `0.5`) |
| Color | click the little square, or type `#FF0000` (red) |
| Position | `10 -20`: first sideways, then up (negative goes down) |
| Margins | `5 10 5 10`: top, right, bottom, left |

A wrong value? The program says so and does not apply it.

## 5. Recipes

**Change what a button says**
1. Pick the button.
2. If the kind shown on the right is "Text", go to step 4.
3. Under **Texts and parts**, click the text.
4. Change **Text** and press Enter.

**Hide something.** Pick it and click **Hide**. To bring it back, **Show** or **Undo**.

**Change the color of every button of a kind.** Pick one, click **Shared styles** and change the color. It counts for all of them.

**Change the order.** Pick it and use **Up** and **Down**.

**Put something new.** Pick a place and click **Add: Inside** (at the end of that place) or **Add: After** (right after the item). Choose what to put; each choice says what it is.

**Make a window of yours**
1. **Panels** > type a name > **New panel**.
2. The window opens in the game. Use **Add: Inside** for each item.

**Make a button that opens your window**
1. In flight, under **Go to a place...**, choose **Flight menu**.
2. **Add: Inside** > **Button**.
3. Under **What it does**, choose **Open or close a panel**, choose the window and click **Apply**.

**Control an option of a mod**
1. In your window, add a **Toggle** (yes or no) or a **Slider** (number).
2. Under **What it does**, choose **Mod setting**, find the option and click **Apply**.

**Control a part of your craft**
1. Add a button, toggle or slider.
2. Under **What it does**, choose **Craft variable**, give it a name (for example `MyLight`) and click **Apply**.
3. On the part, use that name in the controller's expression (Funky Trees).

**A button that calls a command of the game.** Under **What it does**, **Method of the game**. Each command is explained; those marked "automatic description" were made only from the name. At the first click the game asks for your permission.

## 6. Keep and share

**Keep.** Under **Packages**, type a name and click **New package** (only the first time). Then **Save**. The changes come back by themselves when the game starts.

**Send to a friend.** The **Packages** window shows, at the end, where the folder is. Send the `.sp2ui` file named as your package.

**Use someone else's.** Install (part 1; the editor program is not needed), start the game once, close it, and put the `.sp2ui` file in `BepInEx\config\SP2UIEditor\packages`.

**Switch on and off.** In the game, **Ctrl+Shift+U** opens the list of packages. Each says which screens it changes: if it only changes the Designer, you will see no difference in flight.

**Careful:** if a package wants to call a command of the game, the game asks first. Only say yes to people you trust.

## 7. Something went wrong

| What happened | What to do |
|---|---|
| I do not like the last change | **Undo** (Ctrl+Z) as many times as needed |
| I want to drop a change already saved | **Packages**, and the **✕** beside it |
| the screen looks odd or something is gone | **Ctrl+Shift+U** and switch the package off |
| even that does not work | close the game and take the `.sp2ui` file out of the packages folder |
| I want everything as before | close the game and delete `SP2UIEditor.dll` from `BepInEx\plugins` |
| the window stays at "Waiting for the game" | see whether the game shows the connection question |
| I cannot find a property | open **Advanced** and type part of its name |
| I changed it and nothing happened | what is around it may decide its size (a list arranges its items): change the outer box, through the **path** at the top right |
| a change is gone after a game update | **Packages** shows "the element is gone"; make it again |
| it does not say which file the screen came from | close and open that screen in the game |
| take back permissions given to commands | with the game closed, delete `BepInEx\config\SP2UIEditor\trust.json` |

## 8. Optional: ask Claude

If you use Claude Code, register the connector once:

    claude mcp add --scope user sp2ui -- C:\path\mcp\SP2UI.Mcp.exe

Then you can ask, for example, "make the menu buttons wider". It only changes anything with **Claude may edit** checked in the window, and can never give permission to a command of the game.

## 9. For people who make mods

**Packages** > **Export XML and C#** makes the code of the interface to use in your own mod.
