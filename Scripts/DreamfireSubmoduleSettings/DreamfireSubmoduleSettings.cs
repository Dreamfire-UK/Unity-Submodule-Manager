using UnityEngine;

namespace DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings
{
    [CreateAssetMenu(
        fileName = "DreamfireSubmoduleSettings",
        menuName = "Dreamfire Submodules/Submodule Settings")]
    public sealed class DreamfireSubmoduleSettings : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string displayName;

        [Header("Library Location")]
        [SerializeField]
        private string librariesDirectory = "Assets";

        [Header("GitLab")]
        [SerializeField, Min(0)]
        private int gitLabGroupId;

        [SerializeField]
        private string gitLabGroupPath = string.Empty;

        [Header("Git")]
        [SerializeField]
        private string defaultBranch = "main";

        [Header("Manifests")]
        [SerializeField]
        private string libraryManifestFileName =
            "Library.Version.json";

        [SerializeField]
        private string packageManifestFileName =
            "Packages.Version.json";

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName)
                ? name
                : displayName.Trim();

        public string LibrariesDirectory =>
            string.IsNullOrWhiteSpace(librariesDirectory)
                ? "Assets"
                : NormalisePath(librariesDirectory);

        public int GitLabGroupId =>
            gitLabGroupId;

        public string GitLabGroupPath =>
            gitLabGroupPath?
                .Trim()
                .Trim('/') ??
            string.Empty;

        public string DefaultBranch =>
            string.IsNullOrWhiteSpace(defaultBranch)
                ? "main"
                : defaultBranch.Trim();

        public string LibraryManifestFileName =>
            string.IsNullOrWhiteSpace(libraryManifestFileName)
                ? "Library.Version.json"
                : libraryManifestFileName.Trim();

        public string PackageManifestFileName =>
            string.IsNullOrWhiteSpace(packageManifestFileName)
                ? "Packages.Version.json"
                : packageManifestFileName.Trim();

        private void OnValidate()
        {
            displayName =
                displayName?.Trim();

            librariesDirectory =
                NormalisePath(librariesDirectory);

            gitLabGroupPath =
                gitLabGroupPath?
                    .Trim()
                    .Trim('/');

            defaultBranch =
                defaultBranch?.Trim();

            libraryManifestFileName =
                libraryManifestFileName?.Trim();

            packageManifestFileName =
                packageManifestFileName?.Trim();

            if (string.IsNullOrWhiteSpace(librariesDirectory))
            {
                librariesDirectory = "Assets";
            }

            if (string.IsNullOrWhiteSpace(defaultBranch))
            {
                defaultBranch = "main";
            }

            if (string.IsNullOrWhiteSpace(
                    libraryManifestFileName))
            {
                libraryManifestFileName =
                    "Library.Version.json";
            }

            if (string.IsNullOrWhiteSpace(
                    packageManifestFileName))
            {
                packageManifestFileName =
                    "Packages.Version.json";
            }
        }

        private static string NormalisePath(
            string path)
        {
            return path?
                .Trim()
                .TrimEnd('/', '\\')
                .Replace('\\', '/');
        }
    }
}
