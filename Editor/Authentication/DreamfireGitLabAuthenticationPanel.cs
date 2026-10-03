#if UNITY_EDITOR

using System;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using UnityEditor;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Authentication
{
    public sealed class DreamfireGitLabAuthenticationPanel
        : IDisposable
    {
        private static readonly TimeSpan
            RevalidationInterval =
                TimeSpan.FromMinutes(10);

        private readonly
            IDreamfireAuthenticationCoordinator
            authenticationCoordinator;

        private readonly
            IDreamfireAuthenticationSession
            authenticationSession;

        private readonly Action repaint;

        private readonly CancellationTokenSource
            lifetimeCancellation =
                new();

        private string accessToken = string.Empty;
        private double nextRevalidationTime;
        private bool isDisposed;

        public DreamfireGitLabAuthenticationPanel(
            IDreamfireAuthenticationCoordinator
                authenticationCoordinator,
            IDreamfireAuthenticationSession
                authenticationSession,
            Action repaint)
        {
            this.authenticationCoordinator =
                authenticationCoordinator ??
                throw new ArgumentNullException(
                    nameof(authenticationCoordinator));

            this.authenticationSession =
                authenticationSession ??
                throw new ArgumentNullException(
                    nameof(authenticationSession));

            this.repaint =
                repaint ??
                throw new ArgumentNullException(
                    nameof(repaint));

            authenticationCoordinator.StateChanged +=
                HandleAuthenticationStateChanged;

            EditorApplication.update +=
                HandleEditorUpdate;

            ScheduleNextRevalidation();
        }

        public void Draw()
        {
            if (isDisposed)
            {
                return;
            }

            EditorGUILayout.LabelField(
                "GitLab Account",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                if (authenticationSession.IsAuthenticated &&
                    authenticationSession.CurrentUser != null)
                {
                    DrawAuthenticatedUser();
                }
                else
                {
                    DrawSignIn();
                }

                DrawOperationState();
                DrawError();
            }
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            authenticationCoordinator.StateChanged -=
                HandleAuthenticationStateChanged;

            EditorApplication.update -=
                HandleEditorUpdate;

            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();

            accessToken = string.Empty;
        }

        private void DrawSignIn()
        {
            EditorGUILayout.HelpBox(
                "Sign in using a GitLab personal access token. " +
                "The token is kept in memory only and is cleared " +
                "when you sign out or the authentication services " +
                "are disposed.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(
                       authenticationCoordinator.IsBusy))
            {
                accessToken =
                    EditorGUILayout.PasswordField(
                        "Access Token",
                        accessToken);
            }

            bool canSignIn =
                !authenticationCoordinator.IsBusy &&
                !string.IsNullOrWhiteSpace(accessToken);

            using (new EditorGUI.DisabledScope(!canSignIn))
            {
                if (GUILayout.Button("Sign In"))
                {
                    _ = SignInAsync();
                }
            }

            if (authenticationCoordinator.CanRevalidate)
            {
                EditorGUILayout.Space(2);

                EditorGUILayout.HelpBox(
                    "A token remains available in memory. " +
                    "You can retry verification without entering it again.",
                    MessageType.Warning);

                using (new EditorGUI.DisabledScope(
                           authenticationCoordinator.IsBusy))
                {
                    if (GUILayout.Button(
                            "Retry GitLab Verification"))
                    {
                        _ = RevalidateAsync();
                    }
                }
            }
        }

        private void DrawAuthenticatedUser()
        {
            DreamfireGitLabAuthenticatedUser user =
                authenticationSession.CurrentUser;

            EditorGUILayout.LabelField(
                "User",
                FormatUser(user));

            EditorGUILayout.LabelField(
                "GitLab Role",
                ObjectNames.NicifyVariableName(
                    user.EffectiveAccessLevel.ToString()));

            EditorGUILayout.LabelField(
                "Verified",
                user.VerifiedAtUtc
                    .ToLocalTime()
                    .ToString("g"));

            if (user.MembershipExpiresAtUtc.HasValue)
            {
                EditorGUILayout.LabelField(
                    "Membership Expires",
                    user.MembershipExpiresAtUtc.Value
                        .ToLocalTime()
                        .ToString("g"));
            }

            EditorGUILayout.LabelField(
                "Permissions",
                authenticationSession.Permissions.ToString());

            EditorGUILayout.Space(2);

            if (authenticationSession.IsAuthorized)
            {
                EditorGUILayout.HelpBox(
                    "Your GitLab membership has been verified.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Your GitLab account is valid, but your project " +
                    "role does not grant access to Dreamfire libraries.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           authenticationCoordinator.IsBusy))
                {
                    if (GUILayout.Button(
                            "Revalidate Permissions"))
                    {
                        _ = RevalidateAsync();
                    }
                }

                if (GUILayout.Button("Sign Out"))
                {
                    accessToken = string.Empty;

                    authenticationCoordinator.SignOut();
                }
            }
        }

        private void DrawOperationState()
        {
            if (!authenticationCoordinator.IsBusy)
            {
                return;
            }

            EditorGUILayout.Space(2);

            EditorGUILayout.HelpBox(
                "Contacting GitLab...",
                MessageType.Info);
        }

        private void DrawError()
        {
            if (string.IsNullOrWhiteSpace(
                    authenticationCoordinator.LastError))
            {
                return;
            }

            EditorGUILayout.Space(2);

            EditorGUILayout.HelpBox(
                authenticationCoordinator.LastError,
                MessageType.Error);
        }

        private async Task SignInAsync()
        {
            string submittedToken = accessToken;
            accessToken = string.Empty;

            repaint();

            try
            {
                await authenticationCoordinator.SignInAsync(
                    submittedToken,
                    lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Closing the panel cancels authentication.
            }
            catch (ObjectDisposedException)
            {
                // Authentication services were disposed.
            }
            finally
            {
                submittedToken = string.Empty;

                if (!isDisposed)
                {
                    ScheduleNextRevalidation();
                    repaint();
                }
            }
        }

        private async Task RevalidateAsync()
        {
            if (authenticationCoordinator.IsBusy ||
                !authenticationCoordinator.CanRevalidate)
            {
                return;
            }

            try
            {
                await authenticationCoordinator.RevalidateAsync(
                    lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Closing the panel cancels revalidation.
            }
            catch (ObjectDisposedException)
            {
                // Authentication services were disposed.
            }
            finally
            {
                if (!isDisposed)
                {
                    ScheduleNextRevalidation();
                    repaint();
                }
            }
        }

        private void HandleEditorUpdate()
        {
            if (isDisposed ||
                authenticationCoordinator.IsBusy ||
                !authenticationCoordinator.CanRevalidate)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup <
                nextRevalidationTime)
            {
                return;
            }

            // Schedule immediately to prevent another Editor update
            // from starting a duplicate request.
            ScheduleNextRevalidation();

            _ = RevalidateAsync();
        }

        private void HandleAuthenticationStateChanged()
        {
            if (!isDisposed)
            {
                repaint();
            }
        }

        private void ScheduleNextRevalidation()
        {
            nextRevalidationTime =
                EditorApplication.timeSinceStartup +
                RevalidationInterval.TotalSeconds;
        }

        private static string FormatUser(
            DreamfireGitLabAuthenticatedUser user)
        {
            if (string.IsNullOrWhiteSpace(
                    user.DisplayName))
            {
                return "@" + user.Username;
            }

            if (string.IsNullOrWhiteSpace(
                    user.Username))
            {
                return user.DisplayName;
            }

            return $"{user.DisplayName} (@{user.Username})";
        }
    }
}

#endif