#if UNITY_EDITOR

using System;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class
        DreamfireGitCommandAuthorizationService
        : IDreamfireGitCommandAuthorizationService
    {
        private static readonly TimeSpan
            DefaultMaximumVerificationAge =
                TimeSpan.FromMinutes(15);

        private readonly IDreamfireAuthenticationSession
            authenticationSession;

        private readonly TimeSpan maximumVerificationAge;

        public DreamfireGitCommandAuthorizationService(
            IDreamfireAuthenticationSession
                authenticationSession,
            TimeSpan? maximumVerificationAge = null)
        {
            this.authenticationSession =
                authenticationSession ??
                throw new ArgumentNullException(
                    nameof(authenticationSession));

            this.maximumVerificationAge =
                maximumVerificationAge ??
                DefaultMaximumVerificationAge;

            if (this.maximumVerificationAge <=
                TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumVerificationAge),
                    "The verification age must be greater than zero.");
            }
        }

        public ServiceResult Authorize(
            DreamfireGitExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(
                    nameof(request));
            }

            if (!authenticationSession.IsAuthenticated ||
                authenticationSession.CurrentUser == null)
            {
                return ServiceResult.Failed(
                    "Sign in with GitLab before running Git commands.");
            }

            DreamfireGitLabAuthenticatedUser user =
                authenticationSession.CurrentUser;

            if (user.IsMembershipExpired)
            {
                return ServiceResult.Failed(
                    "Your GitLab project membership has expired.");
            }

            if (!authenticationSession.IsAuthorized)
            {
                return ServiceResult.Failed(
                    "Your GitLab project membership does not " +
                    "grant access to Dreamfire libraries.");
            }

            TimeSpan verificationAge =
                DateTimeOffset.UtcNow -
                user.VerifiedAtUtc;

            if (verificationAge < TimeSpan.Zero ||
                verificationAge >
                maximumVerificationAge)
            {
                return ServiceResult.Failed(
                    "Your GitLab permissions must be revalidated " +
                    "before running this command.");
            }

            if (!DreamfireGitCommandRiskPolicy
                    .TryValidateDeclaredRisk(
                        request.GitSubcommand,
                        request.RiskLevel,
                        out string riskValidationError))
            {
                return ServiceResult.Failed(
                    riskValidationError);
            }

            if (!DreamfireGitLabPermissionPolicy
                    .TryGetRequiredGitPermission(
                        request.RiskLevel,
                        out DreamfirePermission
                            requiredPermission))
            {
                return ServiceResult.Failed(
                    $"The Git risk level '{request.RiskLevel}' " +
                    "does not have an authorization rule.");
            }

            if (!authenticationSession.HasPermission(
                    requiredPermission))
            {
                return ServiceResult.Failed(
                    $"Your GitLab role " +
                    $"'{user.EffectiveAccessLevel}' does not " +
                    $"permit the '{request.DisplayName}' command. " +
                    $"Required permission: {requiredPermission}.");
            }

            return ServiceResult.Succeeded();
        }
    }
}

#endif
