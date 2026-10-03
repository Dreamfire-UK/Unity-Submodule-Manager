#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.Services.Git;
using DreamfireSubmodules.Scripts.Services.GitLab;
using UnityEditor;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Windows
{
    public sealed class DreamfireInstallLibraryWindow : EditorWindow
    {
        private readonly List<(DreamfireSubmoduleSettings settings, DreamfireGitLabRepository repository)> _repositories = new();

        private IDreamfireGitLabService _gitLabService;
        private IDreamfireGitService _gitService;
        private IDreamfirePermissionAuthorizationService
            _permissionAuthorizationService;
        private IReadOnlyList<DreamfireSubmoduleSettings> _settings;
        private string _unityProjectRoot;
        private string _parentRepositoryPath;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private bool _loading;
        private bool _hasLoaded;
        private string _error = string.Empty;
        private CancellationTokenSource _cts;

        public static void Open(
            IReadOnlyList<DreamfireSubmoduleSettings> settings,
            string unityProjectRoot,
            string parentRepositoryPath,
            IDreamfireGitLabService gitLabService,
            IDreamfireGitService gitService,
            IDreamfirePermissionAuthorizationService
                permissionAuthorizationService)
        {
            global::DreamfireSubmodules.Editor.DreamfireLibraryManagerWindow.Open();
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void OnGUI()
        {
            bool hasRuntimeState =
                HasRuntimeState(
                    out string runtimeStateFailure);

            var installFailure  = string.Empty;
            bool canInstall = hasRuntimeState && CanInstall(
                    out installFailure);

            if (!hasRuntimeState)
            {
                installFailure = runtimeStateFailure;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string nextSearch = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
                {
                    _search = nextSearch;
                }

                using (new EditorGUI.DisabledScope(
                           _loading ||
                           !hasRuntimeState))
                {
                    if (GUILayout.Button(
                            "Reload",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(70f)))
                    {
                        _ = LoadAsync();
                    }
                }
            }

            if (!hasRuntimeState)
            {
                EditorGUILayout.HelpBox(
                    runtimeStateFailure,
                    MessageType.Warning);

                if (GUILayout.Button(
                        "Return to Dreamfire Libraries",
                        GUILayout.Height(28f)))
                {
                    DreamfireLibraryManagerWindow.Open();
                    Close();
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Error);

            if (!canInstall &&
                !string.IsNullOrWhiteSpace(installFailure))
            {
                EditorGUILayout.HelpBox(
                    installFailure,
                    MessageType.Warning);
            }

            if (_loading)
                EditorGUILayout.HelpBox("Loading repositories from GitLab...", MessageType.Info);

            if (!_loading &&
                _hasLoaded &&
                string.IsNullOrWhiteSpace(_error) &&
                _repositories.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No accessible repositories were returned by GitLab. " +
                    "Confirm that the signed-in account can read the private " +
                    "library group (including its subgroups) and that the " +
                    "access token has read_api or api scope.",
                    MessageType.Info);
            }

            int visibleRepositoryCount =
                _repositories.Count(MatchesSearch);

            if (!_loading &&
                _repositories.Count > 0 &&
                visibleRepositoryCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No repositories match the current search.",
                    MessageType.Info);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var item in _repositories.Where(MatchesSearch))
            {
                bool installed = Directory.Exists(
                    Path.Combine(
                        _unityProjectRoot,
                        item.settings.LibrariesDirectory,
                        item.repository.Name));

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(item.repository.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.repository.Description, EditorStyles.wordWrappedMiniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(
                                   installed ||
                                   _loading ||
                                   !canInstall))
                        {
                            if (GUILayout.Button(installed ? "Installed" : "Install", GUILayout.Width(90f)))
                                _ = InstallAsync(item.settings, item.repository);
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private bool MatchesSearch((DreamfireSubmoduleSettings settings, DreamfireGitLabRepository repository) item)
        {
            return string.IsNullOrWhiteSpace(_search) ||
                   (item.repository.Name?.Contains(
                       _search,
                       StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (item.repository.Description?.Contains(
                       _search,
                       StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private async Task LoadAsync()
        {
            if (_loading)
            {
                return;
            }

            if (!HasRuntimeState(
                    out string runtimeStateFailure))
            {
                _error = runtimeStateFailure;
                _hasLoaded = true;
                Repaint();
                return;
            }

            _loading = true;
            _hasLoaded = false;
            _error = string.Empty;
            _repositories.Clear();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                foreach (DreamfireSubmoduleSettings settings in _settings)
                {
                    var result = await _gitLabService.GetRepositoriesAsync(settings, _cts.Token);
                    if (!result.IsSuccess)
                    {
                        _error = result.Error;
                        continue;
                    }

                    foreach (DreamfireGitLabRepository repository in result.Value)
                        _repositories.Add((settings, repository));
                }
            }
            catch (OperationCanceledException)
            {
                // Closing or reloading the window cancels the request.
            }
            catch (Exception exception)
            {
                _error = exception.Message;
            }
            finally
            {
                _loading = false;
                _hasLoaded = true;
                Repaint();
            }
        }

        private bool HasRuntimeState(
            out string failureReason)
        {
            if (_settings == null ||
                _gitLabService == null ||
                _gitService == null ||
                _permissionAuthorizationService == null ||
                string.IsNullOrWhiteSpace(_unityProjectRoot) ||
                string.IsNullOrWhiteSpace(_parentRepositoryPath))
            {
                failureReason =
                    "The installer session was reset by a Unity script " +
                    "reload. Return to Dreamfire Libraries, sign in again " +
                    "if requested, and reopen Install Library.";

                return false;
            }

            if (_settings.Count == 0)
            {
                failureReason =
                    "No Dreamfire submodule settings assets were found. " +
                    "Create or assign a settings asset before installing " +
                    "private libraries.";

                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private async Task InstallAsync(
            DreamfireSubmoduleSettings settings,
            DreamfireGitLabRepository repository)
        {
            if (!CanInstall(
                    out string installFailure))
            {
                _error = installFailure;
                Repaint();
                return;
            }

            string targetPath =
                Path.GetFullPath(
                    Path.Combine(
                        _unityProjectRoot,
                        settings.LibrariesDirectory,
                        repository.Name));

            string relativePath =
                Path.GetRelativePath(
                        Path.GetFullPath(
                            _parentRepositoryPath),
                        targetPath)
                    .Replace('\\', '/');

            DreamfireGitResult result = await _gitService.AddSubmoduleAsync(
                _parentRepositoryPath,
                repository.HttpUrl,
                relativePath,
                repository.DefaultBranch,
                _cts?.Token ?? CancellationToken.None);

            if (!result.Success)
            {
                _error = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
                Repaint();
                return;
            }

            AssetDatabase.Refresh();
            Repaint();
        }

        private bool CanInstall(
            out string failureReason)
        {
            if (_permissionAuthorizationService == null)
            {
                failureReason =
                    "The permission service is unavailable.";

                return false;
            }

            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult
                authorizationResult =
                    _permissionAuthorizationService.Authorize(
                        DreamfirePermission.InstallLibraries,
                        "install a Dreamfire library");

            failureReason =
                authorizationResult.IsFailure
                    ? authorizationResult.Error
                    : string.Empty;

            return !authorizationResult.IsFailure;
        }
    }
}

#endif
