using System;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public interface IDreamfireAuthenticationSession
    {
        event Action Changed;

        DreamfireGitLabAuthenticatedUser CurrentUser
        {
            get;
        }

        bool IsAuthenticated { get; }
        bool IsAuthorized { get; }
        DreamfirePermission Permissions { get; }
        bool HasPermission(DreamfirePermission requiredPermission);
    }

    public interface IDreamfireAuthenticationSessionStore: IDreamfireAuthenticationSession
    {
        void SetAuthenticatedUser(DreamfireGitLabAuthenticatedUser user);
        void Clear();
    }
}