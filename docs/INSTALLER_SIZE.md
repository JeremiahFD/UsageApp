# Windows installer size

The native Beta 1 installer is 303,104 bytes (296 KiB), and the portable x64
ZIP is about 127 KiB. It uses Windows' installed .NET Framework and does not
bundle Electron, Chromium, Node.js, Android, or font files.

The earlier Electron beta was about 100 MB because it bundled a browser and
desktop runtime. That download has been replaced on the public Beta 1 release,
while the older Electron source remains in this repository for reference.

Future size work could replace the IExpress wrapper with a purpose-built small
installer, trim unused resources, or move to a newer native Windows stack. Code
signing would improve trust prompts but would not materially reduce size.
