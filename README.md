# Woodword

Woodword is a Dalamud roleplay utility by Lynnaes Krasikaz that renders words between Common (English) and Vieran (Icelandic) through the Woodword relay.

## Features

- Common to Vieran and Vieran to Common translation
- Resizable, forest-themed interface with wrapping multiline text
- Manual copy action for generated Vieran text
- One-click clearing for either translation input
- Local-only, size-limited translation history with an in-plugin reader
- Per-installation random client identity and relay rate limiting
- No chat interception, automatic translation, or automatic posting

Open the window in game with `/woodword`.

## Build

Prerequisites:

- XIVLauncher and Dalamud installed and run at least once
- .NET 10 SDK
- `DALAMUD_HOME` set if Dalamud is installed somewhere other than its default location

Development build:

```powershell
dotnet build Woodword/Woodword.csproj -c Release
```

Development builds contain no relay token. Enter one through **Woodword Settings** for local testing, or set the `WOODWORD_RELAY_TOKEN` environment variable before building.

See [RELEASING.md](RELEASING.md) for the GitHub release process.

## Install through Dalamud

Add this URL under **Dalamud Settings → Experimental → Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/kaerlath/Woodword/main/repo.json
```

Then search for **Woodword** in the plugin installer.

## Privacy and behavior

- Translation occurs only after the user presses a translation button.
- Only the entered text, direction, relay token, and random installation ID are sent to the relay.
- Character names, account information, and game chat are not accessed.
- Woodword never posts translated text automatically.
- Successful translation inputs and outputs are stored only in the local plugin configuration folder when history is used; the oldest entries are pruned at the configured size limit.

## Important token note

The release workflow keeps the relay token out of the public source history, but a token embedded in a distributed DLL can be extracted. Cloudflare rate limiting and a hard Google API quota remain the meaningful abuse controls.

## License

Woodword is available under the MIT License.
