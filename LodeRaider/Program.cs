using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LodeRaider;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LodeRaider
{
    class Program
    {
        static void Main(string[] args)
        {
            string logFile = "asset_extraction.log";
            string dataDir = "E:\\Reverse\\LODERUNNER\\LODERUNN-WIN-2302\\LODERUNN-ASSET\\DATA";

            var assetExtractor = new AssetExtractor(logFile, dataDir);
            assetExtractor.ExtractAssets();
        }
    }
}