using System;

namespace DreamfireSubmodules.Scripts.DreamfireManifest
{
    [Serializable]
    public sealed class DreamfireLibraryManifest
    {
        public string name;
        public string displayName;
        public string version;
        public string description;
        public string author;
        public int type;
    }
}