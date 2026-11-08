# Buds 3 Control

A portable Windows 11 taskbar app for **OnePlus Buds 3**. Switch between **Noise Cancellation**, **Transparency**, and **Off** over Bluetooth.

## Run

1. Pair the earbuds in **Settings → Bluetooth & devices**. Leave them awake and put at least one in your ear to change modes.
2. Double-click `dist\Buds3Control.exe`. Windows may place the blue headphones icon under the taskbar’s **Show hidden icons** arrow.
3. Right-click the icon and choose a mode. A check mark shows the mode the earbuds report. **Exit** closes the app.

The app reconnects when the earbuds wake up. If it stays disconnected, open the menu and choose **Connection details**. If another earbud control app is connected, close it and choose **Refresh connection**. The app looks for the Bluetooth name `OnePlus Buds 3` and checks the model ID before enabling writes.

## Build from source

Run from this folder in PowerShell. The build uses the .NET Framework compiler included with Windows 11 and produces `dist\Buds3Control.exe`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Diagnostics

Exit the tray app first. The same executable accepts `--status` or `--set off|anc|transparency`. Use `Start-Process -Wait` with redirected standard output and standard error to capture results from the Windows executable.

## Protocol references

- The [QuickBuds protocol notes](https://github.com/spizganed/QuickBuds/blob/main/docs/PROTOCOL.md) document the RFCOMM service and noise control commands.
- The [QuickBuds model list](https://github.com/spizganed/QuickBuds/blob/main/app/src/main/assets/models.json) identifies the Buds 3 as `063C14` and provides its mode mapping.
- [Microsoft's Bluetooth Winsock documentation](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-connect) describes connecting by service UUID.
