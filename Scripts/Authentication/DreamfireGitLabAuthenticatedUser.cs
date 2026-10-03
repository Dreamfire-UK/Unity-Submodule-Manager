using System;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public sealed class DreamfireGitLabAuthenticatedUser
    {
        public long UserId { get; }
        public long AuthorityProjectId { get; }
        public string Username { get; }
        public string DisplayName { get; }
        public string AvatarUrl { get; }
        public string WebUrl { get; }
        public DreamfireGitLabAccessLevel AccessLevel { get; }
        public bool IsGitLabAdministrator { get; }
        public DateTimeOffset VerifiedAtUtc { get; }
        public DateTimeOffset? MembershipExpiresAtUtc { get; }
        public DreamfireGitLabAccessLevel EffectiveAccessLevel =>  IsGitLabAdministrator ? DreamfireGitLabAccessLevel.Administrator : AccessLevel;

        public bool IsMembershipExpired
        {
            get
            {
                if (IsGitLabAdministrator || !MembershipExpiresAtUtc.HasValue) return false;
                return MembershipExpiresAtUtc.Value <= DateTimeOffset.UtcNow;
            }
        }

        public DreamfirePermission Permissions
        {
            get
            {
                if (IsMembershipExpired) return DreamfirePermission.None;
                return DreamfireGitLabPermissionPolicy.GetPermissions(EffectiveAccessLevel);
            }
        }

        public bool IsAuthorized => HasPermission(DreamfirePermission.ViewLibraries);

        public DreamfireGitLabAuthenticatedUser(long userId, long authorityProjectId, string username, string displayName, string avatarUrl, string webUrl,
            DreamfireGitLabAccessLevel accessLevel, bool isGitLabAdministrator, DateTimeOffset verifiedAtUtc, DateTimeOffset? membershipExpiresAtUtc)
        {
            if (userId < 1) throw new ArgumentOutOfRangeException(nameof(userId), "The GitLab user ID must be positive.");
            if (authorityProjectId < 1) throw new ArgumentOutOfRangeException(nameof(authorityProjectId), "The authority project ID must be positive.");
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("A GitLab username is required.", nameof(username));

            UserId = userId;
            AuthorityProjectId = authorityProjectId;
            Username = username.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Username : displayName.Trim();
            AvatarUrl = avatarUrl?.Trim() ?? string.Empty;
            WebUrl = webUrl?.Trim() ?? string.Empty;
            AccessLevel = accessLevel;
            IsGitLabAdministrator = isGitLabAdministrator;
            VerifiedAtUtc = verifiedAtUtc.ToUniversalTime();
            MembershipExpiresAtUtc = membershipExpiresAtUtc?.ToUniversalTime();
        }

        public bool HasPermission(DreamfirePermission requiredPermission)
        {
            if (requiredPermission == DreamfirePermission.None) return true;
            DreamfirePermission permissions = Permissions;
            return (permissions & requiredPermission) == requiredPermission;
        }
    }
}