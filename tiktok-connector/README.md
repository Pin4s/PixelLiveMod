# PixelLiveMod TikTok connector

Requires Node.js 20+ and the PixelLiveMod 0.5.0 plugin in the isolated game copy.

1. Copy `../.env.example` to `../.env` and set `TIKTOK_USERNAME` to a public LIVE account, with or without `@`. Set `PIXELLIVEMOD_BRIDGE_PORT` to the same value as `[Bridge] Port` in the game's BepInEx config (default 19081). The connector loads the project root `.env` automatically; existing environment variables take precedence.
2. Copy `config.example.json` to `config.json` (kept local and ignored by Git). This file contains gift mappings and per-gift quantities. Restart the connector after configuration changes.
3. In this directory, run `npm ci`, `npm run build`, `npm test`, then `npm start`. Tests read the example config and do not require a live username.
4. Start the isolated game with `Start-Isolated-Game.cmd` in the parent project directory. The connector retries the local bridge if the game starts later.

The nine gift entries have independent `quantity` values (1..20 per gift). `coins` is an identity check, not an input for ball quantity. Do not edit the gift names casually: catalog matching is exact. Optional numeric-string `giftId` may be added to an entry after a verified room catalog lookup; it is never guessed. If the catalog name is translated, an explicit ID plus verified coin cost permits the match. If catalog lookup fails, event fallback requires exact name and cost, and any configured ID must also match.

The connector uses `tiktok-live-connector@2.4.0`, an unofficial library. Its public connection uses Euler Stream signing with community rate limits; no account cookies or credentials are configured here. Catalog lookup may fail or a public LIVE may be offline, in which case the program logs the limitation and retries connection. Neither a LIVE connection nor gift receipt is implied by a successful local build.

TCP listens only on `127.0.0.1`. Each JSON-line command has a unique `requestId`, `type`, and `quantity` (1..20). The Unity response says whether the command was accepted *into the spawn queue*, not whether every ball was created. Actual creation/failure appears in `BepInEx/LogOutput.log`. A command with a lost acknowledgment is never resent automatically; its outcome is explicitly uncertain.
