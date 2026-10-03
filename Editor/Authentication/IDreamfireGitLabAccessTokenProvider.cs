#if UNITY_EDITOR

using System;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.Authentication
{
    public interface IDreamfireGitLabAccessTokenProvider : IDisposable
    {
        bool HasToken { get; }
        ServiceResult SetToken(string accessToken);
        ServiceResult<string> GetToken(); 
        void Clear();
    }
}

#endif