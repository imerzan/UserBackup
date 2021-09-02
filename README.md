# UserBackup
Backup user profiles in macOS and Windows

## General Info
* Automatically scans all system volumes for valid User Directories to backup. Also supports backing up multiple user profiles in a single operation.
* File IO Operations run on 16 (by default) Worker Threads concurrently for optimal speed. Completely Thread Safe.
* Grabs Bookmarks from popular web browser applications.
* Skips Hidden/System folders in addition to AppData/Cloud Storage Folders (Goole Drive, OneDrive, etc.) to reduce bloat, and speed up recursion.
* Provides progress updates every ~5 seconds during backup operation.
* Logs backup details/errors/completion to text logfile.

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

## Demo
![Example1](https://user-images.githubusercontent.com/42287509/131873068-4a91a24f-f2e0-4b01-92a2-96d0070cef20.jpg)
![Example2](https://user-images.githubusercontent.com/42287509/131873077-e405113a-40e2-4ff5-8efc-573f1a291a87.jpg)
![Example3](https://user-images.githubusercontent.com/42287509/131873103-974fc5ac-efe2-427f-8c05-c6916f302ca3.jpg)
