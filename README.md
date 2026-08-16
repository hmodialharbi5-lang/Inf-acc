# Infinite Accessories — TShock server plugin

Target: **TShock 6.1.0 / Terraria 1.4.5.6**.

This is a **server-side** plugin. The Terraria mobile UI is unchanged; extra accessories are stored by the server and their normal Terraria `Player.UpdateAccessory` routine is called for each extra accessory every game update.

## What this version does

- Extra accessory storage separate from the vanilla accessory UI.
- Duplicate accessories are allowed in extra storage.
- Uses Terraria's normal accessory update routine instead of a hand-written list of effects.
- Per-player enable/disable state.
- Persistent JSON data per player UUID.
- Configurable limit in code (`DefaultMaxExtra`, currently 20).

## Important limitation

This is intentionally **not** claiming that every special accessory effect is guaranteed to stack identically to a native second/third copy. Terraria has effects that are boolean, mutually exclusive, or otherwise handled specially. The plugin reuses the base accessory routine, which gives much broader coverage than manually implementing a few stats, but special cases may still need dedicated handling.

## Commands

- `/infacc help`
- `/infacc on`
- `/infacc off`
- `/infacc add <item id>`
- `/infacc remove <slot>`
- `/infacc list`
- `/infacc clear`
- `/infacc max`

Permission: `infaccessories.use`

Example:

`/infacc add 554`

The command currently uses **item IDs** to avoid depending on TShock's localized name parser. You can use any valid accessory ID from the Terraria version running on the server.

## Build

1. Copy `TerrariaServer.dll`, `TShockAPI.dll`, and `TerrariaApi.Server.dll` from your TShock server into the `References` folder.
2. Run `dotnet build -c Release` in this project folder.
3. Copy `bin/Release/net9.0/InfiniteAccessories.dll` to your server's `ServerPlugins` folder.
4. Restart the server.
5. Grant the permission `infaccessories.use` to the group that should use it.

## Why there is no prebuilt DLL in this archive

A TShock plugin must be compiled against the exact Terraria/TShock API assembly version used by the server. The archive is therefore set up to build against the server's own DLLs instead of bundling potentially incompatible game binaries.
