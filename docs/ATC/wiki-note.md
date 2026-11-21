# Note for the wiki (stage 12)

Text to put on the JoinFS wiki, where the ATC and EuroScope pages are. Not part of the program; delete this file once the
wiki is updated.

- **`joinfs-euroscope-bridge` is no longer needed.** From 26.7 JoinFS itself is the FSD server that EuroScope connects to.
  Remove the bridge from the "ATC / EuroScope" instructions and point to `docs/ATC/euroscope-setup.md` (Settings → ATC →
  "Enable ATC client connection", or `-fsd`; EuroScope: private server `127.0.0.1`, port `6809`).
- **The old ATC mode is gone:** the ATC checkbox with airport, level and frequency, `-atc`, `-airport` and the `euroscope.txt`
  file. `-atc` and `-airport` are accepted and ignored (one log line).
- **Hubs must run 26.7** for controllers to see each other through them; a direct link between nodes works with any build.
