#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services.Discovery;
using DreamfireSubmodules.Scripts.Services.Versioning;

namespace DreamfireSubmodules.Editor.Services.Discovery
{
    public sealed class DreamfireLibraryDiscoveryService
        : IDreamfireLibraryDiscoveryService
    {
        private readonly IDreamfireVersionService _versionService;

        public DreamfireLibraryDiscoveryService(
            IDreamfireVersionService versionService)
        {
            _versionService =
                versionService ??
                throw new ArgumentNullException(
                    nameof(versionService));
        }

        public async Task<
                ServiceResult<IReadOnlyList<DreamfireLibraryState>>>
            DiscoverLibrariesAsync(
                string unityProjectRoot,
                DreamfireSubmoduleSettings settings,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(unityProjectRoot))
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireLibraryState>>.Failed(
                    "The Unity project root was not provided.");
            }

            if (settings == null)
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireLibraryState>>.Failed(
                    "Dreamfire submodule settings were not provided.");
            }

            try
            {
                string searchRoot =
                    GetSearchRoot(
                        unityProjectRoot,
                        settings.LibrariesDirectory);

                if (!Directory.Exists(searchRoot))
                {
                    return ServiceResult<
                        IReadOnlyList<DreamfireLibraryState>>.Failed(
                        $"The library search directory does not exist: " +
                        searchRoot);
                }

                string[] manifestPaths =
                    await FindManifestPathsAsync(
                        searchRoot,
                        settings.LibraryManifestFileName,
                        cancellationToken);

                HashSet<string> submodulePaths =
                    ReadSubmodulePaths(
                        unityProjectRoot);

                List<DreamfireLibraryState> libraries =
                    new(manifestPaths.Length);

                foreach (string manifestPath in manifestPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ServiceResult<DreamfireLibraryState> result =
                        await DiscoverLibraryInternalAsync(
                            unityProjectRoot,
                            manifestPath,
                            settings,
                            submodulePaths,
                            cancellationToken);

                    if (result.IsSuccess &&
                        result.Value != null)
                    {
                        libraries.Add(result.Value);
                    }
                }

                IReadOnlyList<DreamfireLibraryState> ordered =
                    libraries
                        .GroupBy(
                            library => library.AbsolutePath,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(
                            library => library.DisplayName,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                return ServiceResult<
                    IReadOnlyList<DreamfireLibraryState>>.Succeeded(
                    ordered);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireLibraryState>>.Failed(
                    $"Failed to discover local libraries: " +
                    exception.Message);
            }
        }

        public async Task<ServiceResult<DreamfireLibraryState>>
            DiscoverLibraryAsync(
                string libraryDirectory,
                DreamfireSubmoduleSettings settings,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(libraryDirectory))
            {
                return ServiceResult<DreamfireLibraryState>.Failed(
                    "The library directory was not provided.");
            }

            if (settings == null)
            {
                return ServiceResult<DreamfireLibraryState>.Failed(
                    "Dreamfire submodule settings were not provided.");
            }

            try
            {
                string absoluteLibraryDirectory =
                    Path.GetFullPath(libraryDirectory);

                string manifestPath =
                    Path.Combine(
                        absoluteLibraryDirectory,
                        settings.LibraryManifestFileName);

                if (!File.Exists(manifestPath))
                {
                    return ServiceResult<DreamfireLibraryState>.Failed(
                        $"No {settings.LibraryManifestFileName} file " +
                        $"was found in {absoluteLibraryDirectory}.");
                }

                string unityProjectRoot =
                    FindUnityProjectRoot(
                        absoluteLibraryDirectory);

                if (string.IsNullOrWhiteSpace(unityProjectRoot))
                {
                    return ServiceResult<DreamfireLibraryState>.Failed(
                        "Could not locate the Unity project root.");
                }

                HashSet<string> submodulePaths =
                    ReadSubmodulePaths(
                        unityProjectRoot);

                return await DiscoverLibraryInternalAsync(
                    unityProjectRoot,
                    manifestPath,
                    settings,
                    submodulePaths,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ServiceResult<DreamfireLibraryState>.Failed(
                    $"Failed to discover the library: " +
                    exception.Message);
            }
        }

        private async Task<ServiceResult<DreamfireLibraryState>>
            DiscoverLibraryInternalAsync(
                string unityProjectRoot,
                string manifestPath,
                DreamfireSubmoduleSettings settings,
                HashSet<string> submodulePaths,
                CancellationToken cancellationToken)
        {
            ServiceResult<DreamfireLibraryManifest> manifestResult =
                await _versionService.ReadLibraryManifestAsync(
                    manifestPath,
                    cancellationToken);

            if (!manifestResult.IsSuccess ||
                manifestResult.Value == null)
            {
                return ServiceResult<DreamfireLibraryState>.Failed(
                    manifestResult.Error);
            }

            string libraryDirectory =
                Path.GetDirectoryName(manifestPath);

            if (string.IsNullOrWhiteSpace(libraryDirectory))
            {
                return ServiceResult<DreamfireLibraryState>.Failed(
                    $"Could not determine the directory for: " +
                    manifestPath);
            }

            string absolutePath =
                Path.GetFullPath(libraryDirectory);

            string relativePath =
                NormalizePath(
                    Path.GetRelativePath(
                        unityProjectRoot,
                        absolutePath));

            string localGitPath =
                Path.Combine(
                    absolutePath,
                    ".git");

            bool isConfiguredSubmodule =
                submodulePaths.Contains(relativePath);

            bool isInitializedSubmodule =
                File.Exists(localGitPath) ||
                Directory.Exists(localGitPath);

            DreamfireLibraryState state = new()
            {
                Manifest = manifestResult.Value,

                RelativePath =
                    relativePath,

                AbsolutePath =
                    absolutePath,

                IsInstalled =
                    true,

                IsSubmodule =
                    isConfiguredSubmodule ||
                    isInitializedSubmodule,

                IsSubmoduleInitialized =
                    isInitializedSubmodule,

                CurrentBranch =
                    "Unknown",

                IsExpanded =
                    true,

                StatusMessage =
                    "Installed locally"
            };

            string packageManifestPath =
                Path.Combine(
                    absolutePath,
                    settings.PackageManifestFileName);

            if (File.Exists(packageManifestPath))
            {
                state.PackageManifestPath =
                    packageManifestPath;
            }

            return ServiceResult<DreamfireLibraryState>.Succeeded(
                state);
        }

        private static Task<string[]> FindManifestPathsAsync(
            string searchRoot,
            string manifestFileName,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return Directory
                        .EnumerateFiles(
                            searchRoot,
                            manifestFileName,
                            SearchOption.AllDirectories)
                        .Where(path =>
                            !IsIgnoredPath(path))
                        .ToArray();
                },
                cancellationToken);
        }

        private static bool IsIgnoredPath(
            string path)
        {
            string normalized =
                NormalizePath(path);

            return normalized.Contains(
                       "/Library/",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(
                       "/Temp/",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(
                       "/Logs/",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(
                       "/obj/",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSearchRoot(
            string unityProjectRoot,
            string configuredDirectory)
        {
            if (Path.IsPathRooted(configuredDirectory))
            {
                return Path.GetFullPath(
                    configuredDirectory);
            }

            return Path.GetFullPath(
                Path.Combine(
                    unityProjectRoot,
                    configuredDirectory));
        }

        private static HashSet<string> ReadSubmodulePaths(
            string repositoryRoot)
        {
            HashSet<string> paths =
                new(StringComparer.OrdinalIgnoreCase);

            string gitModulesPath =
                Path.Combine(
                    repositoryRoot,
                    ".gitmodules");

            if (!File.Exists(gitModulesPath))
            {
                return paths;
            }

            foreach (string line in
                     File.ReadLines(gitModulesPath))
            {
                string trimmed =
                    line.Trim();

                if (!trimmed.StartsWith(
                        "path",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int equalsIndex =
                    trimmed.IndexOf('=');

                if (equalsIndex < 0)
                {
                    continue;
                }

                string path =
                    trimmed[(equalsIndex + 1)..]
                        .Trim();

                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(
                        NormalizePath(path));
                }
            }

            return paths;
        }

        private static string FindUnityProjectRoot(
            string startingDirectory)
        {
            DirectoryInfo current =
                new(startingDirectory);

            while (current != null)
            {
                bool hasAssets =
                    Directory.Exists(
                        Path.Combine(
                            current.FullName,
                            "Assets"));

                bool hasProjectSettings =
                    Directory.Exists(
                        Path.Combine(
                            current.FullName,
                            "ProjectSettings"));

                if (hasAssets &&
                    hasProjectSettings)
                {
                    return current.FullName;
                }

                current =
                    current.Parent;
            }

            return string.Empty;
        }

        private static string NormalizePath(
            string path)
        {
            return path
                .Replace('\\', '/')
                .Trim('/');
        }
    }
}

#endif