using System;
using System.Collections.Generic;

namespace DreamfireSubmodules.Scripts.DreamfireManifest
{
    [Serializable]
    public sealed class DreamfirePackageManifest
    {
        public List<DreamfirePackageRequirement> packages = new();

        public List<DreamfireLibraryRequirement> libraries = new();
    }

    [Serializable]
    public sealed class DreamfirePackageRequirement
    {
        public string packageName;
        public string version;
    }

    [Serializable]
    public sealed class DreamfireLibraryRequirement
    {
        public string name;
        public string version;
    }
}