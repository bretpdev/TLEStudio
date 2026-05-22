# VS Code Project Setup Checklist

Use this checklist to make new .NET projects behave like NEPlumbingInc in VS Code.

## 1) Create/Open Project Structure

- Open the workspace root folder in VS Code (the folder that contains your project folder).
- Confirm your project file exists, for example: `YourProject/YourProject.csproj`.

## 2) Create a Solution File

From workspace root:

```bash
dotnet new sln -n YourProject
dotnet sln YourProject.sln add YourProject/YourProject.csproj
```

## 3) Add .vscode/tasks.json

Create `.vscode/tasks.json` with:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "type": "process",
            "command": "dotnet",
            "args": [
                "build",
                "${workspaceFolder}"
            ],
            "problemMatcher": "$msCompile",
            "presentation": {
                "reveal": "silent",
                "panel": "shared",
                "showReuseMessage": false,
                "clear": true,
                "focus": false
            }
        },
        {
            "label": "clean",
            "type": "process",
            "command": "dotnet",
            "args": [
                "clean",
                "${workspaceFolder}"
            ],
            "problemMatcher": [],
            "group": "build",
            "presentation": {
                "reveal": "silent",
                "panel": "shared",
                "showReuseMessage": false,
                "clear": true,
                "focus": false
            }
        }
    ]
}
```

## 4) Add .vscode/launch.json

Create `.vscode/launch.json` with:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch App",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/YourProject/bin/Debug/net9.0/YourProject.dll",
      "args": [],
      "cwd": "${workspaceFolder}/YourProject",
      "stopAtEntry": false,
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      },
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:7263"
      }
    }
  ]
}
```

Notes:
- Update `YourProject` in `program` and `cwd`.
- Update `net9.0` if your target framework differs.
- Update `ASPNETCORE_URLS` to match your launch settings if needed.

## 5) Keybindings Behavior

- Keep workspace `.vscode/keybindings.json` absent if you want behavior to come from global VS Code keybindings (same as NEPlumbingInc).
- If F6 behavior is different between projects, it is usually due to user-level keybindings.

## 6) Verify

From workspace root:

```bash
dotnet build
```

Then in VS Code:

- Press `F5` to run with pre-build.
- Press `F6` and confirm it behaves according to your global keybinding setup.
- Run `Tasks: Run Task` and confirm both `build` and `clean` exist.

## 7) Quick Copy Workflow

For a new project, copy these files from a known-good repo:

- `.vscode/tasks.json`
- `.vscode/launch.json`

Then only edit:

- project path/name
- target framework in DLL path
- ASP.NET URL/env values
