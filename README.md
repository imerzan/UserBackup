# UserBackup
Backup user profiles in macOS and Windows

## Build Instructions
```csharp
dotnet publish -c Release -r win-x64 --self-contained=true /p:PublishSingleFile=false /p:AssemblyName=userbackup
dotnet publish -c Release -r osx-x64 --self-contained=true /p:PublishSingleFile=false /p:AssemblyName=userbackup

// The output 'publish' folder is self contained and can be run from anywhere.
```

## Optional Cmd Arguments
```bash
dest=DestFolder   ## Specify destination directory to backup to
threads=intThreadCount   ## Specify Thread Count for multi-threading (Default:16)
```
**Example**: `userbackup.exe dest=X:\Backups threads=12`
