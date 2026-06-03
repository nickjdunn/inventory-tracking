# Nordic NUR .NET DLL (Merlin **CE** uses NurApiDotNetWCE.dll in \Windows)

The gun in audit report `merlin-handheld-01` already has:

- `\Windows\NurApiDotNetWCE.dll` — **.NET loadable** (use this)
- Do **not** upload desktop `NurApiDotNet.dll` unless you have no WCE build

Optional mirror on server (for other guns):

```powershell
# After copying NurApiDotNetWCE.dll off the gun via USB/ActiveSync:
.\scripts\upload-nur-dll.ps1 -DllPath "C:\path\NurApiDotNetWCE.dll" -Target wce
```

Parse an audit export:

```powershell
.\scripts\apply-nur-from-audit.ps1 -AuditJsonPath "C:\Users\nickj\Downloads\merlin-audit-....json"
```
