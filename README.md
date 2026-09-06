# FlexVault Unity Plugin (`com.flexvault.vcs`)

Unity Editor Version Control plugin for [FlexVault](https://fxv.dev). Integrates the `fxv` CLI directly into the Unity Editor.

---

## Requirements

* **Unity**: Unity 2021.3 LTS, 2022.3 LTS, or Unity 6+.
* **FlexVault CLI**: `fxv` binary installed and accessible (or configured via Project Settings).
* **Newtonsoft JSON**: Automatically resolved via Package Manager (`com.unity.nuget.newtonsoft-json`).

---

## Installation

### Option 1: Local Disk (Development / Embedded)
1. In the Unity Editor, open **Window > Package Manager**.
2. Click the `+` button in the top-left and choose **Add package from disk...**.
3. Select `package.json` in the `fxv-unity-plugin` folder (or copy `fxv-unity-plugin` into your project's `Packages/com.flexvault.vcs/` folder).

### Option 2: Git URL
1. In the Unity Editor, open **Window > Package Manager**.
2. Click `+` and choose **Add package from git URL...**.
3. Enter the repository URL.

---

## Configuration

Navigate to **Edit > Project Settings > Version Control > FlexVault**:
* **CLI Executable Path**: Set a custom path to `fxv.exe` (Windows) or `fxv` (macOS/Linux), or rely on auto-discovery from standard locations and `PATH`.
* **Test Connection**: Validates that Unity can execute `fxv status` and communicate with the local repository.

---

## Features

* **Dockable FlexVault Window** (`Window > Version Control > FlexVault`):
  * **Changes View**: Displays all modified, added, and deleted files with status badges.
  * **Diff Support**: Diff individual or selected files against their published base.
  * **Publish**: Prompts for a commit description and publishes the entire workspace draft (`fxv snapshot` followed by `fxv publish`).
  * **Revert**: Reverts selected assets and their companion `.meta` files to the published base.
  * **Sync View**: Displays revision status (revisions behind remote HEAD) and provides one-click workspace synchronization (`fxv sync`).
  * **History View**: Shows recent branch commits with timestamps, author attribution, and revision comparison.
* **Project Window Badges**:
  * Displays visual status indicators on items in the Project window (`+` Added, `~` Modified, `-` Deleted, `!` Conflicted).
* **Right-Click Context Menus** (`Assets > FlexVault`):
  * Quick access to **Diff Selected Against Base**, **History**, **Revert Selected**, **Refresh Status**, and **Open FlexVault Window**.
* **Engine Lifecycle Safety**:
  * Enforces asset and `.meta` companion atomicity (including recursive folder expansion).
  * Wraps file modification operations with `EditorApplication.LockReloadAssemblies()` to prevent domain reloads mid-operation.
  * Marshals asset reloads to the main thread via `AssetDatabase.Refresh()`.
