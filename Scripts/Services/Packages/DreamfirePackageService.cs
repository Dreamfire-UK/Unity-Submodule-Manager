#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services;
using DreamfireSubmodules.Scripts.Services.Packages;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Services.Packages
{
    public sealed class DreamfirePackageService : IDreamfirePackageService
    {
        public async Task<ServiceResult<DreamfirePackageManifest>> ReadManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return ServiceResult<DreamfirePackageManifest>.Failed(
                    "The package manifest was not found.");

            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                DreamfirePackageManifest manifest =
                    JsonUtility.FromJson<DreamfirePackageManifest>(json);

                return manifest == null
                    ? ServiceResult<DreamfirePackageManifest>.Failed("The package manifest is invalid.")
                    : ServiceResult<DreamfirePackageManifest>.Succeeded(manifest);
            }
            catch (Exception exception)
            {
                return ServiceResult<DreamfirePackageManifest>.Failed(exception.Message);
            }
        }

        public async Task<ServiceResult<IReadOnlyList<DreamfirePackageState>>> CheckRequirementsAsync(
            DreamfirePackageManifest manifest,
            CancellationToken cancellationToken = default)
        {
            if (manifest == null)
                return ServiceResult<IReadOnlyList<DreamfirePackageState>>.Failed(
                    "The package manifest was not provided.");

            ListRequest request = Client.List(true, false);
            while (!request.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.Status == StatusCode.Failure)
                return ServiceResult<IReadOnlyList<DreamfirePackageState>>.Failed(
                    request.Error?.message ?? "Unity Package Manager listing failed.");

            Dictionary<string, string> installed = request.Result
                .ToDictionary(package => package.name, package => package.version,
                    StringComparer.OrdinalIgnoreCase);

            List<DreamfirePackageState> states = new();

            foreach (DreamfirePackageRequirement requirement in manifest.packages ?? new List<DreamfirePackageRequirement>())
            {
                if (requirement == null || string.IsNullOrWhiteSpace(requirement.packageName))
                {
                    states.Add(new DreamfirePackageState(
                        requirement, string.Empty, DreamfirePackageStatus.Invalid));
                    continue;
                }

                if (!installed.TryGetValue(requirement.packageName.Trim(), out string installedVersion))
                {
                    states.Add(new DreamfirePackageState(
                        requirement, string.Empty, DreamfirePackageStatus.Missing));
                    continue;
                }

                DreamfirePackageStatus status =
                    IsVersionAtLeast(installedVersion, requirement.version)
                        ? DreamfirePackageStatus.Installed
                        : DreamfirePackageStatus.Outdated;

                states.Add(new DreamfirePackageState(
                    requirement, installedVersion, status));
            }

            return ServiceResult<IReadOnlyList<DreamfirePackageState>>.Succeeded(states);
        }

        public async Task<ServiceResult> InstallMissingAsync(
            IEnumerable<DreamfirePackageState> packages,
            CancellationToken cancellationToken = default)
        {
            DreamfirePackageState[] targets = packages?
                .Where(package =>
                    package.Status == DreamfirePackageStatus.Missing ||
                    package.Status == DreamfirePackageStatus.Outdated)
                .ToArray() ?? Array.Empty<DreamfirePackageState>();

            foreach (DreamfirePackageState package in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string version = package.Requirement.version?.Trim() ?? string.Empty;
                string identifier = string.IsNullOrWhiteSpace(version)
                    ? package.Requirement.packageName
                    : $"{package.Requirement.packageName}@{version}";

                AddRequest request = Client.Add(identifier);

                while (!request.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (request.Status == StatusCode.Failure)
                {
                    return ServiceResult.Failed(
                        request.Error?.message ?? $"Failed to install {identifier}.");
                }
            }

            return ServiceResult.Succeeded();
        }

        private static bool IsVersionAtLeast(string installed, string required)
        {
            if (string.IsNullOrWhiteSpace(required))
                return true;

            return Version.TryParse(installed, out Version installedVersion) &&
                   Version.TryParse(required, out Version requiredVersion)
                ? installedVersion >= requiredVersion
                : string.Equals(installed, required, StringComparison.OrdinalIgnoreCase);
        }
    }
}

#endif