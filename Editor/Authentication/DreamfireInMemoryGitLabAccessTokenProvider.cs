#if UNITY_EDITOR

using System;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.Authentication
{
    public sealed class DreamfireInMemoryGitLabAccessTokenProvider : IDreamfireGitLabAccessTokenProvider
    {
        private const int MaximumTokenLength = 4096;
        private readonly object synchronizationRoot = new();
        private char[] tokenCharacters;
        private bool isDisposed;

        public bool HasToken
        {
            get
            {
                lock (synchronizationRoot)
                {
                    return !isDisposed && tokenCharacters != null && tokenCharacters.Length > 0;
                }
            }
        }

        public ServiceResult SetToken(string accessToken)
        {
            if (accessToken == null) return ServiceResult.Failed("A GitLab access token is required.");
            int startIndex = 0;
            int endIndex = accessToken.Length - 1;
            while (startIndex <= endIndex && char.IsWhiteSpace(accessToken[startIndex])) startIndex++;
            while (endIndex >= startIndex && char.IsWhiteSpace(accessToken[endIndex])) endIndex--;
            int tokenLength = endIndex - startIndex + 1;
            if (tokenLength <= 0) return ServiceResult.Failed("A GitLab access token is required.");
            if (tokenLength > MaximumTokenLength) return ServiceResult.Failed("The GitLab access token is too long.");

            for (int index = startIndex; index <= endIndex; index++)
            {
                if (char.IsControl(accessToken[index])) return ServiceResult.Failed("The GitLab access token contains " + "an unsupported control character.");
            }

            lock (synchronizationRoot)
            {
                if (isDisposed)
                {
                    return ServiceResult.Failed("The access-token provider has been disposed.");
                }

                char[] replacement = new char[tokenLength];
                accessToken.CopyTo(startIndex, replacement, 0, tokenLength);
                ClearTokenBuffer();
                tokenCharacters = replacement;
            }
            return ServiceResult.Succeeded();
        }

        public ServiceResult<string> GetToken()
        {
            lock (synchronizationRoot)
            {
                if (isDisposed)
                {
                    return ServiceResult<string>.Failed("The access-token provider has been disposed.");
                }

                if (tokenCharacters == null || tokenCharacters.Length == 0)
                {
                    return ServiceResult<string>.Failed("No GitLab access token is available.");
                }

                return ServiceResult<string>.Succeeded(new string(tokenCharacters));
            }
        }

        public void Clear()
        {
            lock (synchronizationRoot)
            {
                if (isDisposed)
                {
                    return;
                }

                ClearTokenBuffer();
            }
        }

        public void Dispose()
        {
            lock (synchronizationRoot)
            {
                if (isDisposed)
                {
                    return;
                }

                ClearTokenBuffer();
                isDisposed = true;
            }
        }

        private void ClearTokenBuffer()
        {
            if (tokenCharacters == null)
            {
                return;
            }

            Array.Clear(tokenCharacters, 0, tokenCharacters.Length);
            tokenCharacters = null;
        }
    }
}

#endif