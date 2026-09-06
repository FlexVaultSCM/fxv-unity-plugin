# FlexVault Unity Plugin Implementation Plan

This document outlines the architecture and implementation roadmap for the Unity FlexVault Version Control Plugin (`fxv-unity-plugin`).

For the initial release, the design prioritizes **simplicity, reliability, and correctness** over premature performance optimizations.

---

## 1. FlexVault SCM Characteristics

* **Optimistic Workflow**: Assets remain locally writable on disk without mandatory exclusive lock checkout steps.
* **No File-Level Staging Area**: Unlike Git (`git add`, `git rm`) or Perforce (`p4 add`, `p4 delete`), FlexVault has no separate staging commands. The working tree is tracked automatically against the repository tree.
* **Workspace-Wide Drafts & Publishing**:
  * `fxv snapshot -d "..."` captures the entire workspace working state into a local draft revision.
  * `fxv publish -d "..."` publishes the current workspace draft to the CAS and remote store.
  * Commits are whole-workspace. File selection lists in the UI are scoped to file-targeted operations such as Revert and Diff.
* **Targeted Revert**: `fxv revert <files...>` restores specific files (and companion `.meta` files) to their published base.
* **Targeted History & Diff**: `fxv cat <path> -r <revision>` extracts historical revisions to temporary files for side-by-side visual or text diffing.

---

## 2. Architecture & Design

```
┌─────────────────────────────────────────────────────────────┐
│                    Unity Editor UI Layer                    │
│  - FlexVault Window (Dockable Changes & Sync UI)            │
│  - Project Hierarchy Overlay Badges (ProjectWindowItemOnGUI)│
│  - Context Menus (Assets > FlexVault > Revert, Diff...)     │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│             Unity Editor Interception & Lifecycle           │
│  - AssetPostprocessor (OnPostprocessAllAssets triggers sync)│
│  - EditorApplication.update / delayCall (Main thread queue) │
│  - EditorApplication.LockReloadAssemblies (Domain safety)   │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                 FlexVault Plugin Core (C#)                  │
│  - FlexVaultClient / FxvRunner (Async CLI process execution)│
│  - FlexVaultStateCache (Thread-safe path/GUID to status map)│
│  - Companion Meta Helper (Asset + .meta pairing rules)      │
└──────────────────────────────┬──────────────────────────────┘
                               │ (Process spawn with --format json)
┌──────────────────────────────▼──────────────────────────────┐
│                    fxv CLI Executable                       │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Key Components (First Pass)

### 1. Process Execution & JSON Serialization
* **`FxvRunner.cs`**:
  * Spawns `fxv` via `System.Diagnostics.Process` with redirected standard I/O and UTF-8 encoding.
  * Appends default flags: `--unattended --no-color --format json`.
  * Runs on background tasks via `Task.Run` with `CancellationToken` support.
  * Parses structured JSON responses and error envelopes (`status: "error"`, code, message).
* **`FxvJsonDto.cs`**:
  * C# DTO models for `fxv status`, `fxv sync`, `fxv publish`, and `fxv history`.
  * Serialized using `Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json`) for broad Unity LTS compatibility.

### 2. State Cache & Meta Companion Handling
* **`FlexVaultStateCache.cs`**:
  * Thread-safe cache storing normalized paths and file states (`Unchanged`, `Added`, `Modified`, `Deleted`, `Conflicted`, `Ignored`).
  * Fast GUID-to-state lookup map for overlay drawing without per-frame path allocations.
* **`FlexVaultMetaHelper.cs`**:
  * Enforces asset and `.meta` pairing.
  * When reverting or inspecting an asset, ensures `asset.meta` is included.
  * Correctly handles folder `.meta` files when directory structures change.

### 3. Unity Lifecycle & Domain Reload Safety
* **Status Triggering**:
  * `AssetPostprocessor.OnPostprocessAllAssets` triggers a debounced status refresh (300ms window).
  * Window focus (`EditorWindow.OnFocus`) triggers a background status refresh.
* **Assembly Reload & AssetDatabase Integration**:
  * During multi-step operations (`sync`, `revert`, `publish`), wrap operations with `EditorApplication.LockReloadAssemblies()`.
  * Ensure all `AssetDatabase.Refresh()` and `AssetDatabase.ImportAsset()` calls are marshaled to the Unity main thread via `EditorApplication.delayCall`.

### 4. User Interface
* **`FlexVaultWindow.cs`**:
  * Dockable Editor Window accessed via `Window > Version Control > FlexVault`.
  * **Changes View**: Displays all modified/added/deleted files.
    * Multi-selection allows context actions: Revert Selected, Diff Selected.
    * Commit message input with "Publish Changes" button (executing `fxv snapshot` followed by `fxv publish`).
    * Displays active branch, current draft revision, and published base.
  * **Sync View**: Displays sync status and "Sync Workspace" button.
* **`FlexVaultOverlayDrawer.cs`**:
  * Subscribes to `EditorApplication.projectWindowItemOnGUI`.
  * Queries `FlexVaultStateCache` using asset GUIDs to render compact status icons.

---

## 4. Implementation Phases

### Phase 1: Package Structure & CLI Bridge
1. Create UPM package layout (`package.json`, `Editor/FlexVault.VCS.Editor.asmdef`).
2. Add `FlexVaultSettings.cs` for configuring CLI binary path and auto-detecting `fxv` on PATH.
3. Implement `FxvRunner.cs` and `FxvJsonDto.cs`.

### Phase 2: State Cache & Meta Helper
1. Implement `FlexVaultMetaHelper.cs` for asset/.meta path resolution.
2. Implement `FlexVaultStateCache.cs` with thread-safe updates and event dispatching.
3. Implement `FlexVaultPostprocessor.cs` for debounced status checks.

### Phase 3: Core SCM Operations & UI Window
1. Implement Status, Snapshot/Publish, Revert, and Sync operations.
2. Build `FlexVaultWindow.cs` providing Changes view and Sync controls.
3. Implement `FlexVaultOverlayDrawer.cs` for Project window status icons.
4. Add right-click context menu options (`Assets > FlexVault > Revert`, `Assets > FlexVault > History`).
