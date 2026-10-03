#if UNITY_EDITOR

using System;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.Authentication
{
    public sealed class DreamfireAuthenticationCoordinator
        : IDreamfireAuthenticationCoordinator
    {
        private readonly IDreamfireGitLabApiClient apiClient;

        private readonly IDreamfireGitLabAccessTokenProvider
            tokenProvider;

        private readonly IDreamfireAuthenticationSessionStore
            sessionStore;

        private CancellationTokenSource activeCancellation;

        private bool suppressOperationFailure;
        private bool isDisposed;

        public event Action StateChanged;

        public bool IsBusy { get; private set; }

        public string LastError { get; private set; } =
            string.Empty;

        public bool CanRevalidate =>
    !isDisposed &&
    tokenProvider.HasToken;

        public DreamfireAuthenticationCoordinator(
            IDreamfireGitLabApiClient apiClient,
            IDreamfireGitLabAccessTokenProvider tokenProvider,
            IDreamfireAuthenticationSessionStore sessionStore)
        {
            this.apiClient =
                apiClient ??
                throw new ArgumentNullException(
                    nameof(apiClient));

            this.tokenProvider =
                tokenProvider ??
                throw new ArgumentNullException(
                    nameof(tokenProvider));

            this.sessionStore =
                sessionStore ??
                throw new ArgumentNullException(
                    nameof(sessionStore));

            this.sessionStore.Changed +=
                HandleSessionChanged;
        }

        public async Task<ServiceResult<
            DreamfireGitLabAuthenticatedUser>> SignInAsync(
                string accessToken,
                CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!TryBeginOperation(
                    cancellationToken,
                    out CancellationTokenSource operation))
            {
                return ServiceResult<
                    DreamfireGitLabAuthenticatedUser>.Failed(
                        "Another authentication operation is running.");
            }

            try
            {
                tokenProvider.Clear();
                sessionStore.Clear();

                ServiceResult storeResult =
                    tokenProvider.SetToken(
                        accessToken);

                if (storeResult.IsFailure)
                {
                    return SetFailure(
                        storeResult.Error);
                }

                ServiceResult<string> tokenResult =
                    tokenProvider.GetToken();

                if (tokenResult.IsFailure)
                {
                    tokenProvider.Clear();

                    return SetFailure(
                        tokenResult.Error);
                }

                ServiceResult<
                    DreamfireGitLabAuthenticatedUser>
                    verificationResult =
                        await apiClient.VerifyCurrentUserAsync(
                            tokenResult.Value,
                            operation.Token);

                operation.Token
                    .ThrowIfCancellationRequested();

                if (verificationResult.IsFailure)
                {
                    tokenProvider.Clear();
                    sessionStore.Clear();

                    return SetFailure(
                        verificationResult.Error);
                }

                sessionStore.SetAuthenticatedUser(
                    verificationResult.Value);

                LastError = string.Empty;
                NotifyStateChanged();

                return verificationResult;
            }
            catch (OperationCanceledException)
            {
                tokenProvider.Clear();
                sessionStore.Clear();

                return CreateCancellationResult(
                    "GitLab sign-in was cancelled.");
            }
            catch (Exception)
            {
                tokenProvider.Clear();
                sessionStore.Clear();

                if (suppressOperationFailure)
                {
                    return ServiceResult<
                        DreamfireGitLabAuthenticatedUser>.Failed(
                            "The authentication operation was cancelled.");
                }

                return SetFailure(
                    "GitLab sign-in failed unexpectedly.");
            }
            finally
            {
                FinishOperation(operation);
            }
        }

        public async Task<ServiceResult<
            DreamfireGitLabAuthenticatedUser>> RevalidateAsync(
                CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!TryBeginOperation(
                    cancellationToken,
                    out CancellationTokenSource operation))
            {
                return ServiceResult<
                    DreamfireGitLabAuthenticatedUser>.Failed(
                        "Another authentication operation is running.");
            }

            try
            {
                ServiceResult<string> tokenResult =
                    tokenProvider.GetToken();

                if (tokenResult.IsFailure)
                {
                    sessionStore.Clear();

                    return SetFailure(
                        tokenResult.Error);
                }

                ServiceResult<
                    DreamfireGitLabAuthenticatedUser>
                    verificationResult =
                        await apiClient.VerifyCurrentUserAsync(
                            tokenResult.Value,
                            operation.Token);

                operation.Token
                    .ThrowIfCancellationRequested();

                if (verificationResult.IsFailure)
                {
                    sessionStore.Clear();

                    return SetFailure(
                        verificationResult.Error);
                }

                sessionStore.SetAuthenticatedUser(
                    verificationResult.Value);

                LastError = string.Empty;
                NotifyStateChanged();

                return verificationResult;
            }
            catch (OperationCanceledException)
            {
                sessionStore.Clear();

                return CreateCancellationResult(
                    "GitLab session validation was cancelled.");
            }
            catch (Exception)
            {
                sessionStore.Clear();

                if (suppressOperationFailure)
                {
                    return ServiceResult<
                        DreamfireGitLabAuthenticatedUser>.Failed(
                            "The authentication operation was cancelled.");
                }

                return SetFailure(
                    "GitLab session validation failed unexpectedly.");
            }
            finally
            {
                FinishOperation(operation);
            }
        }

        public void SignOut()
        {
            if (isDisposed)
            {
                return;
            }

            suppressOperationFailure = true;

            CancelActiveOperation();

            tokenProvider.Clear();
            sessionStore.Clear();

            LastError = string.Empty;
            NotifyStateChanged();
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            suppressOperationFailure = true;

            sessionStore.Changed -=
                HandleSessionChanged;

            CancelActiveOperation();

            tokenProvider.Clear();
            sessionStore.Clear();

            StateChanged = null;
        }

        private bool TryBeginOperation(
            CancellationToken cancellationToken,
            out CancellationTokenSource operation)
        {
            if (IsBusy)
            {
                operation = null;
                return false;
            }

            suppressOperationFailure = false;
            LastError = string.Empty;
            IsBusy = true;

            operation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            activeCancellation = operation;

            NotifyStateChanged();
            return true;
        }

        private void FinishOperation(
            CancellationTokenSource operation)
        {
            if (ReferenceEquals(
                    activeCancellation,
                    operation))
            {
                activeCancellation = null;
            }

            operation.Dispose();

            IsBusy = false;
            suppressOperationFailure = false;

            NotifyStateChanged();
        }

        private void CancelActiveOperation()
        {
            CancellationTokenSource cancellation =
                activeCancellation;

            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The operation already finished.
            }
        }

        private ServiceResult<
            DreamfireGitLabAuthenticatedUser>
            CreateCancellationResult(
                string message)
        {
            if (suppressOperationFailure)
            {
                return ServiceResult<
                    DreamfireGitLabAuthenticatedUser>.Failed(
                        "The authentication operation was cancelled.");
            }

            return SetFailure(message);
        }

        private ServiceResult<
            DreamfireGitLabAuthenticatedUser>
            SetFailure(
                string error)
        {
            LastError =
                string.IsNullOrWhiteSpace(error)
                    ? "GitLab authentication failed."
                    : error.Trim();

            NotifyStateChanged();

            return ServiceResult<
                DreamfireGitLabAuthenticatedUser>.Failed(
                    LastError);
        }

        private void HandleSessionChanged()
        {
            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            if (!isDisposed)
            {
                StateChanged?.Invoke();
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(
                        DreamfireAuthenticationCoordinator));
            }
        }
    }
}

#endif