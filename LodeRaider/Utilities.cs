using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LodeRaider
{
    public static class Utilities
    {
        public static string ToUnicode(BinaryReader binaryReader, int length)
        {
            string hexString = BitConverter.ToString(binaryReader.ReadBytes(length), 0, length);
            if (!string.IsNullOrEmpty(hexString))
            {
                StringBuilder sb = new StringBuilder();
                foreach (string substr in hexString.Split('-'))
                {
                    int value = Convert.ToInt32(substr, 16);
                    if (value != 0)
                    {
                        sb.Append(char.ConvertFromUtf32(value));
                    }
                }
                return sb.ToString();
            }
            return hexString;
        }

        public static void Seek(BinaryReader binaryReader, Asset asset)
        {
            if (binaryReader?.BaseStream != null)
            {
                binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
            }
        }

        public static string NormalizeFilename(Asset asset)
        {
            // First get the name without extension
            string nameWithoutExt = Path.GetFileNameWithoutExtension(asset.name);
            
            // Then normalize it
            var name = Regex.Replace(nameWithoutExt, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
            
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
            {
                name = asset.id.ToString();
            }
            return name;
        }
    }
} 