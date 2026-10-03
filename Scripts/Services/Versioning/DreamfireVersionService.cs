#if UNITY_EDITOR

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services;
using DreamfireSubmodules.Scripts.Services.Versioning;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Services.Versioning
{
    public sealed class DreamfireVersionService
        : IDreamfireVersionService
    {
        public async Task<ServiceResult<DreamfireLibraryManifest>>
            ReadLibraryManifestAsync(
                string manifestPath,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    "The library manifest path was not provided.");
            }

            if (!File.Exists(manifestPath))
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    $"The library manifest was not found: " +
                    manifestPath);
            }

            try
            {
                string json =
                    await File.ReadAllTextAsync(
                        manifestPath,
                        cancellationToken);

                return ParseLibraryManifest(json);
            }
            catch (OperationCanceledException)
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    "Reading the library manifest was cancelled.");
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    $"Failed to read the library manifest: " +
                    exception.Message);
            }
        }

        public ServiceResult<DreamfireLibraryManifest>
            ParseLibraryManifest(
                string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    "The library manifest is empty.");
            }

            try
            {
                DreamfireLibraryManifest manifest =
                    JsonUtility.FromJson<
                        DreamfireLibraryManifest>(json);

                if (manifest == null)
                {
                    return ServiceResult<
                        DreamfireLibraryManifest>.Failed(
                        "The library manifest could not be parsed.");
                }

                if (string.IsNullOrWhiteSpace(manifest.name))
                {
                    return ServiceResult<
                        DreamfireLibraryManifest>.Failed(
                        "The library manifest does not contain a name.");
                }

                if (string.IsNullOrWhiteSpace(manifest.version))
                {
                    return ServiceResult<
                        DreamfireLibraryManifest>.Failed(
                        $"The manifest for '{manifest.name}' " +
                        "does not contain a version.");
                }

                if (!Version.TryParse(
                        manifest.version,
                        out _))
                {
                    return ServiceResult<
                        DreamfireLibraryManifest>.Failed(
                        $"'{manifest.version}' is not a valid version.");
                }

                return ServiceResult<
                    DreamfireLibraryManifest>.Succeeded(
                    manifest);
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    $"Failed to parse the library manifest: " +
                    exception.Message);
            }
        }

        public ServiceResult<Version> ParseVersion(
            string version)
        {
            if (!Version.TryParse(
                    version,
                    out Version parsedVersion))
            {
                return ServiceResult<Version>.Failed(
                    $"'{version}' is not a valid version.");
            }

            return ServiceResult<Version>.Succeeded(
                parsedVersion);
        }

        public DreamfireVersionComparison Compare(
            string installedVersion,
            string latestVersion)
        {
            if (!Version.TryParse(
                    installedVersion,
                    out Version installed) ||
                !Version.TryParse(
                    latestVersion,
                    out Version latest))
            {
                return DreamfireVersionComparison.Unknown;
            }

            int result =
                installed.CompareTo(latest);

            return result switch
            {
                < 0 => DreamfireVersionComparison.Older,
                > 0 => DreamfireVersionComparison.Newer,
                _ => DreamfireVersionComparison.Equal
            };
        }

        public bool IsUpdateAvailable(
            string installedVersion,
            string latestVersion)
        {
            return Compare(
                       installedVersion,
                       latestVersion) ==
                   DreamfireVersionComparison.Older;
        }
    }
}

#endif