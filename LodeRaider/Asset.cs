using System;

namespace LodeRaider
{
    public struct Asset
    {
        public string assetType;  // Type of asset (SND, PCM, PAK, etc)
        public short id;          // Asset ID
        public string name;       // Asset name
        public int length;        // Asset data length
        public int offset;        // Offset in PRS file
        public string prsPath;    // Path to PRS file containing asset data

        public override string ToString()
        {
            return string.IsNullOrEmpty(name.Trim()) ? "Resource" : name;
        }
    }
} 