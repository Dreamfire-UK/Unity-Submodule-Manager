#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Authentication
{
    [FilePath(
        "ProjectSettings/DreamfireGitLabProjectSettings.asset",
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class DreamfireGitLabProjectSettings
        : ScriptableSingleton<DreamfireGitLabProjectSettings>
    {
        public const string SettingsPath =
            "Project/Dreamfire Submodules/GitLab";

        private const string DefaultGitLabBaseUrl =
            "https://gitlab.dreamfirestudio.net";

        [SerializeField]
        private string gitLabBaseUrl =
            DefaultGitLabBaseUrl;

        [SerializeField]
        private long authorityProjectId;

        public bool TryGetConfiguration(
            out Uri gitLabBaseUri,
            out long configuredAuthorityProjectId,
            out string validationError)
        {
            configuredAuthorityProjectId =
                authorityProjectId;

            if (!Uri.TryCreate(
                    gitLabBaseUrl?.Trim(),
                    UriKind.Absolute,
                    out gitLabBaseUri))
            {
                validationError =
                    "Enter a valid absolute GitLab URL in " +
                    "Project Settings.";

                return false;
            }

            bool isSecure =
                string.Equals(
                    gitLabBaseUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase);

            bool isLocalDevelopment =
                gitLabBaseUri.IsLoopback &&
                string.Equals(
                    gitLabBaseUri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase);

            if (!isSecure && !isLocalDevelopment)
            {
                validationError =
                    "The GitLab URL must use HTTPS. HTTP is only " +
                    "allowed for a loopback development server.";

                return false;
            }

            if (!string.IsNullOrEmpty(
                    gitLabBaseUri.UserInfo))
            {
                validationError =
                    "Credentials must not be embedded in the " +
                    "GitLab URL.";

                return false;
            }

            if (configuredAuthorityProjectId < 1)
            {
                validationError =
                    "Enter the numeric GitLab authority project ID " +
                    "in Project Settings.";

                return false;
            }

            validationError = string.Empty;
            return true;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(
                SettingsPath,
                SettingsScope.Project,
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    "Dreamfire",
                    "GitLab",
                    "Submodules",
                    "Authentication",
                    "Project ID"
                })
            {
                label = "Dreamfire GitLab",
                guiHandler = _ => DrawSettings()
            };
        }

        private static void DrawSettings()
        {
            DreamfireGitLabProjectSettings settings =
                instance;

            EditorGUILayout.LabelField(
                "GitLab Authentication",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "The authority project determines which GitLab " +
                "membership and role grant Dreamfire permissions.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();

            string updatedBaseUrl =
                EditorGUILayout.TextField(
                    "GitLab URL",
                    settings.gitLabBaseUrl);

            long updatedAuthorityProjectId =
                EditorGUILayout.LongField(
                    "Authority Project ID",
                    settings.authorityProjectId);

            if (EditorGUI.EndChangeCheck())
            {
                settings.gitLabBaseUrl =
                    updatedBaseUrl;

                settings.authorityProjectId =
                    updatedAuthorityProjectId;

                settings.Save(true);
            }

            if (!settings.TryGetConfiguration(
                    out _,
                    out _,
                    out string validationError))
            {
                EditorGUILayout.Space(4f);

                EditorGUILayout.HelpBox(
                    validationError,
                    MessageType.Warning);
            }
        }
    }
}

#endif
