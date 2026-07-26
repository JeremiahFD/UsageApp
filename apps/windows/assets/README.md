# Windows assets

The runtime tray icon is generated in memory from the current remaining
percentage, so it always reflects the latest snapshot and does not require a
static image file. A branded `.ico` can be added here later and referenced from
the Electron Builder configuration without changing that dynamic tray behavior.
