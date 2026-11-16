# MagicCircle

Created with `./tools/dev.ps1 new-mod`. Build / deploy / run:

```powershell
./tools/dev.ps1 build MagicCircle
./tools/dev.ps1 deploy MagicCircle
./tools/dev.ps1 launch
./tools/dev.ps1 log --source MagicCircle
```

Anything placed in `assets/` is copied next to the DLL by `deploy` and `package`.
Acceptance markers: log lines starting with `[SFM-ACCEPT]` are what `wait-log` and the agent look for.
