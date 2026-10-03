#if UNITY_EDITOR

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Authentication
{
    public sealed class DreamfireGitLabApiClient : IDreamfireGitLabApiClient, IDisposable
    {
        private const int MaximumResponseCharacters = 1_000_000;
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
        private readonly Uri apiBaseUri;
        private readonly long authorityProjectId;
        private readonly HttpClient httpClient;
        private readonly TimeSpan requestTimeout;
        private readonly bool ownsHttpClient;
        private bool isDisposed;

        public DreamfireGitLabApiClient(Uri gitLabBaseUri, long authorityProjectId, HttpClient httpClient = null, TimeSpan? requestTimeout = null)
        {
            ValidateBaseUri(gitLabBaseUri);
            if (authorityProjectId < 1) throw new ArgumentOutOfRangeException(nameof(authorityProjectId), "The authority project ID must be positive.");
            
            TimeSpan resolvedTimeout = requestTimeout ?? DefaultRequestTimeout;
            if (resolvedTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException( nameof(requestTimeout), "The request timeout must be positive.");

            apiBaseUri = CreateApiBaseUri( gitLabBaseUri);
            this.authorityProjectId = authorityProjectId;
            this.requestTimeout = resolvedTimeout;

            if (httpClient == null)
            {
                this.httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                ownsHttpClient = true;
            }
            else
            {
                this.httpClient = httpClient;
                ownsHttpClient = false;
            }
        }

        public async Task<ServiceResult<DreamfireGitLabAuthenticatedUser>> VerifyCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ServiceResult<string> tokenResult = ValidateToken(accessToken);
            if (tokenResult.IsFailure) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed(tokenResult.Error);

            string validatedToken = tokenResult.Value;

            ServiceResult<GitLabUserResponse> userResult =await GetCurrentUserAsync(validatedToken, cancellationToken).ConfigureAwait(false);
            if (userResult.IsFailure) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed(userResult.Error);

            GitLabUserResponse user = userResult.Value;
            if (user.id < 1 || string.IsNullOrWhiteSpace(user.username)) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed("GitLab returned an invalid user profile.");

            if (!string.IsNullOrWhiteSpace(user.state) && !string.Equals(user.state, "active", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed($"The GitLab account '{user.username}' " + $"is not active.");
            }

            DreamfireGitLabAccessLevel accessLevel = DreamfireGitLabAccessLevel.NoAccess;
            DateTimeOffset? membershipExpiresAtUtc = null;

            if (!user.is_admin)
            {
                ServiceResult<MembershipLookup> membershipResult = await GetMembershipAsync(validatedToken, user.id, cancellationToken).ConfigureAwait(false);
                if (membershipResult.IsFailure) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed(membershipResult.Error);

                MembershipLookup lookup = membershipResult.Value;
                if (lookup.WasFound)
                {
                    GitLabMemberResponse membership = lookup.Membership;
                    if (membership.id != user.id) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed("GitLab returned membership for an " + "unexpected user.");
                    if (string.IsNullOrWhiteSpace(membership.state) || string.Equals(membership.state, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        accessLevel = ConvertAccessLevel(membership.access_level);
                    }
                    ServiceResult<DateTimeOffset?> expiryResult = ParseExpiryDate(membership.expires_at);
                    if (expiryResult.IsFailure) return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed(expiryResult.Error);
                    membershipExpiresAtUtc = expiryResult.Value;
                }
            }

            try
            {
                DreamfireGitLabAuthenticatedUser authenticatedUser = new(user.id, authorityProjectId, user.username, user.name, user.avatar_url, user.web_url,
                            accessLevel, user.is_admin, DateTimeOffset.UtcNow, membershipExpiresAtUtc);
                return ServiceResult<DreamfireGitLabAuthenticatedUser>.Succeeded(authenticatedUser);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return ServiceResult<DreamfireGitLabAuthenticatedUser>.Failed("The verified GitLab identity is invalid: " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            if (ownsHttpClient) httpClient.Dispose();
            isDisposed = true;
        }

        private async Task<ServiceResult<GitLabUserResponse>> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
        {
            ServiceResult<GitLabHttpResponse> responseResult = await SendGetAsync("user", accessToken, cancellationToken) .ConfigureAwait(false);
            if (responseResult.IsFailure) return ServiceResult<GitLabUserResponse>.Failed(responseResult.Error);

            GitLabHttpResponse response = responseResult.Value;
            if (!response.IsSuccess) return ServiceResult<GitLabUserResponse>.Failed(CreateApiFailureMessage( "user verification", response));

            return DeserializeResponse<GitLabUserResponse>(response.Content, "GitLab user profile");
        }

        private async Task<ServiceResult<MembershipLookup>> GetMembershipAsync(string accessToken, long userId, CancellationToken cancellationToken)
        {
            string endpoint = $"projects/{authorityProjectId}/" + $"members/all/{userId}";
            ServiceResult<GitLabHttpResponse> responseResult = await SendGetAsync(endpoint, accessToken, cancellationToken).ConfigureAwait(false);
            if (responseResult.IsFailure) return ServiceResult<MembershipLookup>.Failed(responseResult.Error);
            GitLabHttpResponse response = responseResult.Value;
            if (response.StatusCode == HttpStatusCode.NotFound) return ServiceResult<MembershipLookup>.Succeeded(MembershipLookup.NotFound());
            if (!response.IsSuccess) return ServiceResult< MembershipLookup>.Failed(CreateApiFailureMessage("project membership verification", response));
            ServiceResult<GitLabMemberResponse> membershipResult = DeserializeResponse<GitLabMemberResponse>(response.Content, "GitLab project membership");
            if (membershipResult.IsFailure) return ServiceResult<MembershipLookup>.Failed(membershipResult.Error);
            return ServiceResult<MembershipLookup>.Succeeded(MembershipLookup.Found(membershipResult.Value));
        }

        private async Task<ServiceResult<GitLabHttpResponse>> SendGetAsync(string relativeEndpoint, string accessToken, CancellationToken cancellationToken)
        {
            Uri endpoint = new(apiBaseUri, relativeEndpoint);
            using (HttpRequestMessage request = new(HttpMethod.Get, endpoint))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", accessToken);
                request.Headers.TryAddWithoutValidation("User-Agent", "DreamfireSubmodules/1.0");
                using (CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCancellation.CancelAfter(requestTimeout);

                    try
                    {
                        using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCancellation.Token).ConfigureAwait(false))
                        {
                            string content = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (content.Length > MaximumResponseCharacters)
                            {
                                return ServiceResult< GitLabHttpResponse>.Failed("The GitLab response was " + "larger than the allowed limit.");
                            }
                            return ServiceResult<GitLabHttpResponse>.Succeeded(new GitLabHttpResponse(response.StatusCode, response.ReasonPhrase, content));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                        return ServiceResult<GitLabHttpResponse>.Failed($"The GitLab request timed out after " + $"{requestTimeout.TotalSeconds:0} " + "seconds.");
                    }
                    catch (HttpRequestException exception)
                    {
                        return ServiceResult<GitLabHttpResponse>.Failed("The GitLab request failed: " + exception.Message);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return ServiceResult<GitLabHttpResponse>.Failed( "The GitLab request was invalid: " + exception.Message);
                    }
                }
            }
        }

        private static ServiceResult<T> DeserializeResponse<T>(string json, string responseDescription) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return ServiceResult<T>.Failed($"{responseDescription} was empty.");

            try
            {
                T response = JsonUtility.FromJson<T>(json);
                if (response == null) return ServiceResult<T>.Failed($"{responseDescription} could not be parsed.");
                return ServiceResult<T>.Succeeded(response);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                return ServiceResult<T>.Failed($"{responseDescription} could not be parsed: " + exception.Message);
            }
        }

        private static ServiceResult<string> ValidateToken(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return ServiceResult<string>.Failed("A GitLab access token is required.");
            string token = accessToken.Trim();
            foreach (char character in token)
            {
                if (char.IsControl(character)) return ServiceResult<string>.Failed("The GitLab access token contains an " + "unsupported control character.");
            }
            return ServiceResult<string>.Succeeded(token);
        }

        private static ServiceResult<DateTimeOffset?> ParseExpiryDate(string expiryDate)
        {
            if (string.IsNullOrWhiteSpace(expiryDate)) return ServiceResult<DateTimeOffset?>.Succeeded(null);
            bool parsed = DateTimeOffset.TryParseExact(expiryDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal, out DateTimeOffset result);
            if (!parsed) return ServiceResult<DateTimeOffset?>.Failed("GitLab returned an invalid membership " + $"expiry date: '{expiryDate}'.");
            return ServiceResult<DateTimeOffset?>.Succeeded(result);
        }

        private static DreamfireGitLabAccessLevel ConvertAccessLevel(int accessLevel)
        {
            switch (accessLevel)
            {
                case 0: return DreamfireGitLabAccessLevel.NoAccess;
                case 5: return DreamfireGitLabAccessLevel.Minimal;
                case 10: return DreamfireGitLabAccessLevel.Guest;
                case 15: return DreamfireGitLabAccessLevel.Planner;
                case 20: return DreamfireGitLabAccessLevel.Reporter;
                case 25: return DreamfireGitLabAccessLevel.SecurityManager;
                case 30: return DreamfireGitLabAccessLevel.Developer;
                case 40: return DreamfireGitLabAccessLevel.Maintainer;
                case 50: return DreamfireGitLabAccessLevel.Owner;
                case 60: return DreamfireGitLabAccessLevel.Administrator;
                default: return DreamfireGitLabAccessLevel.Unknown;
            }
        }

        private static string CreateApiFailureMessage(string operation, GitLabHttpResponse response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: return "GitLab rejected the access token. It may " + "be invalid, revoked or expired.";
                case HttpStatusCode.Forbidden: return "The GitLab token does not have permission " + $"to perform {operation}.";
                case HttpStatusCode.TooManyRequests: return "GitLab temporarily rejected the request " + "because its rate limit was reached.";
                default: return $"GitLab {operation} failed with HTTP " + $"{(int)response.StatusCode} " + $"({response.ReasonPhrase}).";
            }
        }

        private static Uri CreateApiBaseUri(Uri gitLabBaseUri)
        {
            string baseAddress = gitLabBaseUri.AbsoluteUri.TrimEnd('/');
            if (!baseAddress.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase))  baseAddress += "/api/v4";
            return new Uri(baseAddress + "/", UriKind.Absolute);
        }

        private static void ValidateBaseUri(Uri gitLabBaseUri)
        {
            if (gitLabBaseUri == null) throw new ArgumentNullException(nameof(gitLabBaseUri));
            if (!gitLabBaseUri.IsAbsoluteUri) throw new ArgumentException("The GitLab address must be absolute.", nameof(gitLabBaseUri));
            bool isSecure = string.Equals(gitLabBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isLocalDevelopment = gitLabBaseUri.IsLoopback && string.Equals(gitLabBaseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            if (!isSecure && !isLocalDevelopment)
            {
                throw new ArgumentException("The GitLab address must use HTTPS. HTTP is " + "only allowed for a loopback development server.", nameof(gitLabBaseUri));
            }

            if (!string.IsNullOrEmpty(gitLabBaseUri.UserInfo))
            {
                throw new ArgumentException("Credentials must not be embedded in the " + "GitLab address.", nameof(gitLabBaseUri));
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(DreamfireGitLabApiClient));
            }
        }

        [Serializable]
        private sealed class GitLabUserResponse
        {
            public long id;
            public string username;
            public string name;
            public string state;
            public string avatar_url;
            public string web_url;
            public bool is_admin;
        }

        [Serializable]
        private sealed class GitLabMemberResponse
        {
            public long id;
            public string state;
            public int access_level;
            public string expires_at;
        }

        private sealed class MembershipLookup
        {
            public bool WasFound { get; }
            public GitLabMemberResponse Membership { get; }

            private MembershipLookup(bool wasFound, GitLabMemberResponse membership)
            {
                WasFound = wasFound;
                Membership = membership;
            }

            public static MembershipLookup Found(GitLabMemberResponse membership)
            {
                return new MembershipLookup(true, membership);
            }

            public static MembershipLookup NotFound()
            {
                return new MembershipLookup(false, null);
            }
        }

        private sealed class GitLabHttpResponse
        {
            public HttpStatusCode StatusCode { get; }
            public string ReasonPhrase { get; }
            public string Content { get; }

            public bool IsSuccess
            {
                get
                {
                    int statusCode = (int)StatusCode;
                    return statusCode >= 200 && statusCode <= 299;
                }
            }

            public GitLabHttpResponse(HttpStatusCode statusCode, string reasonPhrase, string content)
            {
                StatusCode = statusCode;
                ReasonPhrase = reasonPhrase ?? string.Empty;
                Content = content ?? string.Empty;
            }
        }
    }
}

#endif