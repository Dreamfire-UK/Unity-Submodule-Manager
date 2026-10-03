#if UNITY_EDITOR

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services;
using DreamfireSubmodules.Scripts.Services.Git;
using DreamfireSubmodules.Scripts.Services.GitLab;
using DreamfireSubmodules.Scripts.Services.Packages;
using DreamfireSubmodules.Scripts.Services.Updates;
using DreamfireSubmodules.Scripts.Services.Versioning;
using UnityEditor;

namespace DreamfireSubmodules.Editor.Services.Updates
{
    public sealed class DreamfireLibraryUpdateService : IDreamfireLibraryUpdateService
    {
        private readonly IDreamfireGitService _gitService;
        private readonly IDreamfireGitLabService _gitLabService;
        private readonly IDreamfireVersionService _versionService;
        private readonly IDreamfirePackageService _packageService;
        private readonly IDreamfirePermissionAuthorizationService
            _permissionAuthorizationService;

        public DreamfireLibraryUpdateService(
            IDreamfireGitService gitService,
            IDreamfireGitLabService gitLabService,
            IDreamfireVersionService versionService,
            IDreamfirePackageService packageService,
            IDreamfirePermissionAuthorizationService
                permissionAuthorizationService)
        {
            _gitService =
                gitService ??
                throw new ArgumentNullException(
                    nameof(gitService));

            _gitLabService =
                gitLabService ??
                throw new ArgumentNullException(
                    nameof(gitLabService));

            _versionService =
                versionService ??
                throw new ArgumentNullException(
                    nameof(versionService));

            _packageService =
                packageService ??
                throw new ArgumentNullException(
                    nameof(packageService));

            _permissionAuthorizationService =
                permissionAuthorizationService ??
                throw new ArgumentNullException(
                    nameof(permissionAuthorizationService));
        }

        public async Task<ServiceResult> CheckForUpdateAsync(
            DreamfireLibraryState library,
            DreamfireSubmoduleSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (library == null)
            {
                return ServiceResult.Failed(
                    "A Dreamfire library is required.");
            }

            if (settings == null)
            {
                return ServiceResult.Failed(
                    "Dreamfire submodule settings are required.");
            }

            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult
                authorizationResult =
                    _permissionAuthorizationService.Authorize(
                        DreamfirePermission.ViewLibraries,
                        "check Dreamfire library updates");

            if (authorizationResult.IsFailure)
            {
                return ServiceResult.Failed(
                    authorizationResult.Error);
            }

            DreamfireGitLabRepository repository = new()
            {
                name = library.Name,
                path = library.Name,
                path_with_namespace = $"{settings.GitLabGroupPath}/{library.Name}",
                default_branch = string.IsNullOrWhiteSpace(library.CurrentBranch)
                    ? settings.DefaultBranch
                    : library.CurrentBranch
            };

            ServiceResult<DreamfireLibraryManifest> remote =
                await _gitLabService.DownloadLibraryManifestAsync(
                    settings, repository, settings.LibraryManifestFileName, cancellationToken);

            if (!remote.IsSuccess)
                return ServiceResult.Failed(remote.Error);

            library.LatestVersion = remote.Value.version?.Trim() ?? string.Empty;
            library.HasUpdateAvailable = _versionService.IsUpdateAvailable(
                library.InstalledVersion, library.LatestVersion);

            return ServiceResult.Succeeded();
        }

        public async Task<ServiceResult> UpdateAsync(
            DreamfireLibraryState library,
            DreamfireSubmoduleSettings settings,
            bool installMissingPackages,
            CancellationToken cancellationToken = default)
        {
            if (library == null)
            {
                return ServiceResult.Failed(
                    "A Dreamfire library is required.");
            }

            if (settings == null)
            {
                return ServiceResult.Failed(
                    "Dreamfire submodule settings are required.");
            }

            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult
                authorizationResult =
                    _permissionAuthorizationService.Authorize(
                        DreamfirePermission.UpdateLibraries,
                        "update a Dreamfire library");

            if (authorizationResult.IsFailure)
            {
                return ServiceResult.Failed(
                    authorizationResult.Error);
            }

            if (library.HasLocalChanges)
                return ServiceResult.Failed("Commit or discard local changes before updating.");

            DreamfireGitResult pull = await _gitService.PullAsync(
                library.AbsolutePath, library.CurrentBranch, cancellationToken);

            if (!pull.Success)
                return ServiceResult.Failed(
                    string.IsNullOrWhiteSpace(pull.Error) ? pull.Output : pull.Error);

            AssetDatabase.Refresh();

            authorizationResult =
                _permissionAuthorizationService.Authorize(
                    DreamfirePermission.UpdateLibraries,
                    "finish updating a Dreamfire library");

            if (authorizationResult.IsFailure)
            {
                return ServiceResult.Failed(
                    authorizationResult.Error);
            }

            if (!string.IsNullOrWhiteSpace(library.PackageManifestPath))
            {
                ServiceResult<DreamfirePackageManifest> packageManifest =
                    await _packageService.ReadManifestAsync(
                        library.PackageManifestPath, cancellationToken);

                if (packageManifest.IsSuccess)
                {
                    ServiceResult<System.Collections.Generic.IReadOnlyList<DreamfirePackageState>> check =
                        await _packageService.CheckRequirementsAsync(
                            packageManifest.Value, cancellationToken);

                    if (check.IsSuccess)
                    {
                        library.Packages.Clear();
                        library.Packages.AddRange(check.Value);
                        library.HasMissingPackages = check.Value.Any(
                            package => package.Status == DreamfirePackageStatus.Missing ||
                                       package.Status == DreamfirePackageStatus.Outdated);

                        if (installMissingPackages && library.HasMissingPackages)
                        {
                            ServiceResult install =
                                await _packageService.InstallMissingAsync(check.Value, cancellationToken);

                            if (!install.IsSuccess)
                                return install;
                        }
                    }
                }
            }

            return ServiceResult.Succeeded();
        }
    }
}

#endif