using System;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public sealed class DreamfireAuthenticationSession : IDreamfireAuthenticationSessionStore
    {
        private readonly object synchronizationRoot = new();
        private DreamfireGitLabAuthenticatedUser currentUser;

        public event Action Changed;

        public DreamfireGitLabAuthenticatedUser CurrentUser
        {
            get
            {
                lock (synchronizationRoot)
                {
                    return currentUser;
                }
            }
        }

        public bool IsAuthenticated => CurrentUser != null;
        public bool IsAuthorized => CurrentUser?.IsAuthorized ?? false;
        public DreamfirePermission Permissions => CurrentUser?.Permissions ?? DreamfirePermission.None;

        public void SetAuthenticatedUser(DreamfireGitLabAuthenticatedUser user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            bool wasChanged;
            lock (synchronizationRoot)
            {
                wasChanged = !ReferenceEquals(currentUser, user);
                currentUser = user;
            }

            if (wasChanged) Changed?.Invoke();
        }

        public void Clear()
        {
            bool wasChanged;
            lock (synchronizationRoot)
            {
                wasChanged = currentUser != null;
                currentUser = null;
            }
            if (wasChanged) Changed?.Invoke();
        }

        public bool HasPermission(DreamfirePermission requiredPermission)
        {
            DreamfireGitLabAuthenticatedUser user = CurrentUser;
            return user != null && user.HasPermission(requiredPermission);
        }
    }
}