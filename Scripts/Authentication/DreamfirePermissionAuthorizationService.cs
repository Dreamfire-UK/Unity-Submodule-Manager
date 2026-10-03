using System;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public sealed class DreamfirePermissionAuthorizationService
        : IDreamfirePermissionAuthorizationService
    {
        private static readonly TimeSpan
            DefaultMaximumVerificationAge =
                TimeSpan.FromMinutes(15);

        private readonly IDreamfireAuthenticationSession
            authenticationSession;

        private readonly TimeSpan maximumVerificationAge;

        public DreamfirePermissionAuthorizationService(
            IDreamfireAuthenticationSession authenticationSession,
            TimeSpan? maximumVerificationAge = null)
        {
            this.authenticationSession =
                authenticationSession ??
                throw new ArgumentNullException(
                    nameof(authenticationSession));

            this.maximumVerificationAge =
                maximumVerificationAge ??
                DefaultMaximumVerificationAge;

            if (this.maximumVerificationAge <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumVerificationAge),
                    "The verification age must be greater than zero.");
            }
        }

        public ServiceResult.ServiceResult Authorize(
            DreamfirePermission requiredPermission,
            string operationName)
        {
            string normalisedOperationName =
                string.IsNullOrWhiteSpace(operationName)
                    ? "perform this operation"
                    : operationName.Trim();

            if (requiredPermission == DreamfirePermission.None)
            {
                return ServiceResult.ServiceResult.Failed(
                    $"No permission rule is configured to " +
                    $"{normalisedOperationName}.");
            }

            if (!authenticationSession.IsAuthenticated ||
                authenticationSession.CurrentUser == null)
            {
                return ServiceResult.ServiceResult.Failed(
                    $"Sign in with GitLab to " +
                    $"{normalisedOperationName}.");
            }

            DreamfireGitLabAuthenticatedUser user =
                authenticationSession.CurrentUser;

            if (user.IsMembershipExpired)
            {
                return ServiceResult.ServiceResult.Failed(
                    "Your GitLab project membership has expired.");
            }

            if (!authenticationSession.IsAuthorized)
            {
                return ServiceResult.ServiceResult.Failed(
                    "Your GitLab project membership does not grant " +
                    "access to Dreamfire libraries.");
            }

            TimeSpan verificationAge =
                DateTimeOffset.UtcNow -
                user.VerifiedAtUtc;

            if (verificationAge < TimeSpan.Zero ||
                verificationAge > maximumVerificationAge)
            {
                return ServiceResult.ServiceResult.Failed(
                    "Your GitLab permissions must be revalidated " +
                    $"before you can {normalisedOperationName}.");
            }

            if (!authenticationSession.HasPermission(
                    requiredPermission))
            {
                return ServiceResult.ServiceResult.Failed(
                    $"Your GitLab role " +
                    $"'{user.EffectiveAccessLevel}' does not permit " +
                    $"you to {normalisedOperationName}. Required " +
                    $"permission: {requiredPermission}.");
            }

            return ServiceResult.ServiceResult.Succeeded();
        }
    }
}