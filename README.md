# Unity Submodule Manager

A Unity Editor tool for managing Git submodules inside a Unity project. It gives you a dashboard for setup, status checks, updates, branch switching, and publishing changes without leaving the Editor.

This repository contains the `DreamfireSubmodules` tool extracted from a Unity 6 project. It is intended to be placed at `Assets/DreamfireSubmodules` in a Git-backed Unity project.

## Features

- Discover submodules from the project's `.gitmodules` file and show their local and remote status.
- Initialize submodules, including nested ones, after cloning a project.
- Check for upstream updates and update clean submodules while skipping those with local changes or unpublished commits.
- Switch a submodule to a local or remote branch.
- Add an existing Git repository as a submodule at a chosen project path and upstream branch.
- Commit and push submodule changes, then record and push the updated submodule references in the parent repository.
- Open a submodule folder or terminal directly from the dashboard.

The repository also contains a configurable Git command catalogue and GitLab authentication components for permission-controlled workflows. These require project-specific GitLab configuration; the main submodule dashboard works with Git and `.gitmodules`.

## Requirements

- A Unity project under Git version control.
- Git installed and available to the Unity Editor process.
- Git credentials configured for any private repositories you use.

The source was inspected in a project using **Unity 6000.4.0f1**. Other Unity versions have not been verified.

## Install

1. Copy this repository's contents into `Assets/DreamfireSubmodules` in your Unity project, keeping the Unity `.meta` files alongside their assets.
2. Open the project in Unity and let it import and compile the scripts.
3. Choose **Dreamfire Submodules → Submodule Manager** from the Unity menu.

You can also include this repository as a Git submodule at `Assets/DreamfireSubmodules`:

```bash
git submodule add <repository-url> Assets/DreamfireSubmodules
git commit -m "Add Unity Submodule Manager"
```

Replace `<repository-url>` with this repository's clone URL.

## Quick start

1. After cloning a Unity project that uses submodules, open the manager and select **Setup Project** to initialize them.
2. Use **Check Now** to refresh status and inspect available changes.
3. Select **Update Safe** to update eligible, clean submodules.
4. Review the resulting changes, enter a change description, and select **Save & Push All** when ready to publish them.

For a single submodule, use its row to set it up, update it, switch branches, or save and push its changes. The **Add a submodule by repository URL** section lets you specify a URL, project folder, and upstream branch.

## GitLab configuration

The included GitLab authentication and command components use **Project Settings → Dreamfire Submodules → GitLab**. Configure the GitLab URL and the numeric authority project ID for your organization before using those workflows. The checked-in source has a Dreamfire-specific default GitLab URL, so replace it for another organization. GitLab access and project membership are needed for permission-controlled commands.

## Repository layout

```text
Editor/                         Unity Editor windows, Git operations, and authentication
GitCommands/BuiltIn/            Built-in command definitions
Scripts/                        Settings, manifests, models, and services
DreamfireSubmoduleSettings/     Sample project-specific settings assets
*.asmdef                        Runtime and Editor assembly definitions
```

The sample settings assets are specific to the source project. Review or replace them before using library discovery or GitLab repository listing in another project.

## License

No license has been selected for this repository. Add a license before inviting others to reuse or redistribute the code.
