using System.Collections.Generic;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.Services.GitLab;
using DreamfireSubmodules.Scripts.Services.Packages;

namespace DreamfireSubmodules.Scripts.DreamfireLibrary
{
    public sealed class DreamfireLibraryState
    {
        public DreamfireLibraryManifest Manifest { get; set; }

        public DreamfirePackageManifest PackageManifest { get; set; }

        public DreamfireGitLabRepository Repository { get; set; }

        public string RelativePath { get; set; } =
            string.Empty;

        public string AbsolutePath { get; set; } =
            string.Empty;

        public string PackageManifestPath { get; set; } =
            string.Empty;

        public string CurrentBranch { get; set; } =
            string.Empty;

        public string LatestVersion { get; set; } =
            string.Empty;

        public string StatusMessage { get; set; } =
            string.Empty;

        public string ErrorMessage { get; set; } =
            string.Empty;

        public string BusyMessage { get; set; } =
            string.Empty;

        public string CommitMessage { get; set; } =
            string.Empty;

        public bool IsInstalled { get; set; }

        public bool IsSubmodule { get; set; }

        public bool IsSubmoduleInitialized { get; set; }

        public bool HasLocalChanges { get; set; }

        public bool HasUnpushedCommits { get; set; }

        public bool HasUpdateAvailable { get; set; }

        public bool HasMissingPackages { get; set; }

        public bool IsExpanded { get; set; } = true;

        public bool IsBusy { get; set; }

        public int AheadCount { get; set; }

        public int BehindCount { get; set; }

        public List<DreamfirePackageState> Packages { get; } =
            new();

        public string Name =>
            Manifest?.name?.Trim() ??
            Repository?.Name ??
            string.Empty;

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(
                Manifest?.displayName)
                ? Manifest.displayName.Trim()
                : Name;

        public string InstalledVersion =>
            Manifest?.version?.Trim() ??
            string.Empty;

        public bool HasError =>
            !string.IsNullOrWhiteSpace(
                ErrorMessage);
    }
}