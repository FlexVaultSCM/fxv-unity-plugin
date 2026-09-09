# FlexVault Unity Plugin (`com.flexvault.vcs`)

Unity Editor Version Control plugin for [FlexVault](https://fxv.dev). Integrates the `fxv` CLI directly into the Unity Editor.

---

## Requirements

* **Unity**: Unity 2021.3 LTS, 2022.3 LTS, or Unity 6+.
* **FlexVault CLI**: `fxv` binary version `0.5.0` to `< 0.10.0` installed and accessible (or configured via Project Settings).
* **Newtonsoft JSON**: Automatically resolved via Package Manager (`com.unity.nuget.newtonsoft-json`).

---

## Installation

### Option 1: Package Manager (Git URL or Release Tarball URL)
1. In the Unity Editor, open **Window > Package Manager**.
2. Click the `+` button in the top-left and select **Add package from git URL...**.
3. Enter the repository URL or a direct release tarball link from [GitHub Releases](https://github.com/FlexVaultSCM/fxv-unity-plugin/releases/):
   * **Latest Git**: `https://github.com/FlexVaultSCM/fxv-unity-plugin.git`
   * **Pinned Tag**: `https://github.com/FlexVaultSCM/fxv-unity-plugin.git#v0.2.0`
   * **Release Tarball**: `https://github.com/FlexVaultSCM/fxv-unity-plugin/releases/latest/download/com.flexvault.vcs.tgz`

### Option 2: Download Release Tarball
1. Download `com.flexvault.vcs.tgz` from the latest release on [GitHub Releases](https://github.com/FlexVaultSCM/fxv-unity-plugin/releases/).
2. In the Unity Editor, open **Window > Package Manager**.
3. Click `+` and choose **Add package from tarball...**.
4. Select the downloaded `.tgz` archive.

### Option 3: Local Disk (Development / Embedded)
1. In the Unity Editor, open **Window > Package Manager**.
2. Click the `+` button in the top-left and choose **Add package from disk...**.
3. Select `package.json` in the `fxv-unity-plugin` folder (or copy `fxv-unity-plugin` into your project's `Packages/com.flexvault.vcs/` folder).

---

## Configuration

Navigate to **Edit > Project Settings > Version Control > FlexVault**:
* **CLI Executable Path**: Set a custom path to `fxv.exe` (Windows) or `fxv` (macOS/Linux), or rely on auto-discovery from standard locations and `PATH`.
* **Test Connection**: Validates that Unity can execute `fxv status` and communicate with the local repository.

---

## Features

* **Dockable FlexVault Window** (`Window > Version Control > FlexVault`):
  * **Changes View**: Displays all modified, added, and deleted files with status badges.
  * **Draft Checkpointing**: Create local snapshots (`fxv snapshot`) without publishing to remote.
  * **Conflict Resolution**: Inline conflict resolution banners and per-file `[Mine]` / `[Theirs]` buttons (`fxv resolve`).
  * **Diff Support**: Diff individual or selected files against their published base revision, using external graphical diff viewers configured via `FXV_DIFF_TOOL`, `DIFF`, VS Code, or Rider.
  * **Publish**: Prompts for a commit description and publishes the entire workspace draft (`fxv snapshot` followed by `fxv publish`).
  * **Revert**: Reverts selected assets and their companion `.meta` files to the published base.
  * **Sync View**: Displays revision status (revisions behind remote HEAD) and provides one-click workspace synchronization (`fxv sync`).
  * **History & Revision Jumping**: Shows recent branch commits with timestamps, author attribution, revision comparison, and one-click workspace state switching (`fxv goto`).
* **Project Window Badges**:
  * Displays visual status indicators on items in the Project window (`+` Added, `~` Modified, `-` Deleted, `!` Conflicted).
* **Right-Click Context Menus** (`Assets > FlexVault`):
  * Quick access to **Diff Selected Against Base**, **Resolve Conflict** (Keep Mine / Take Theirs), **Ignore Selected** (Add to `.fxvignore` and `.gitignore`), **History**, **Revert Selected**, **Refresh Status**, and **Open FlexVault Window**.
* **Engine Lifecycle & Mutation Safety**:
  * Enforces asset and `.meta` companion atomicity (including recursive folder expansion).
  * Safety guards prevent workspace mutations while in Play Mode or when scenes have unsaved edits.
  * Wraps file modification operations with `EditorApplication.LockReloadAssemblies()` to prevent domain reloads mid-operation.
  * Marshals asset reloads to the main thread via `AssetDatabase.Refresh()`.
* **Project Settings & Authentication**:
  * Auto-discovers CLI executables and tests CLI connection.
  * In-editor user login and logout management (`fxv login` / `fxv logout`).
