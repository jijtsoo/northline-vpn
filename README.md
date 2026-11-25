# Northline

A small Dynamic Island for OpenVPN, pinned to the top center of the screen.

## Use

1. Build Release and run **as Administrator**.
2. A short black pill sits at the top center.
3. Hold the pointer on it for one second. A thin bar opens.
4. Add a profile, connect, and leave. The bar folds back into the pill.
5. Logins are remembered with Windows DPAPI. Right-click the pill to quit.

## Build

```powershell
dotnet build -c Release
```

Exe: `OpenVpnClient\bin\Release\net8.0-windows\OpenVpnClient.exe`

Needs OpenVPN Community (`openvpn.exe`), not OpenVPN Connect.
Remove old profiles and add the `.ovpn` again so certificates are copied with it.
