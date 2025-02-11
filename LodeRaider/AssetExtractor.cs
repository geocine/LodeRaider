using System;
using System.IO;

namespace LodeRaider
{
    public class AssetExtractor
    {
        private readonly string logFile;
        private readonly string dataDir;
        private readonly SpriteExtractor spriteExtractor;
        private readonly AudioExtractor audioExtractor;

        public AssetExtractor(string logFile, string dataDir)
        {
            this.logFile = logFile;
            this.dataDir = dataDir;
            this.spriteExtractor = new SpriteExtractor();
            this.audioExtractor = new AudioExtractor();
        }

        public void ExtractAssets()
        {
            // Clear previous log
            File.WriteAllText(logFile, "");

            // Create a writer that writes to both console and file
            using (var fileWriter = new StreamWriter(logFile, true))
            using (var multiWriter = new MultiWriter(fileWriter, Console.Out))
            {
                Console.SetOut(multiWriter);
                ProcessPrdFiles();
            }
        }

        private void ProcessPrdFiles()
        {
            foreach (string prdFile in Directory.EnumerateFiles(dataDir, "*.PRD"))
            {
                ProcessPrdFile(prdFile);
            }
        }

        private void ProcessPrdFile(string prdFile)
        {
            using (BinaryReader binaryReader = new BinaryReader(File.Open(prdFile, FileMode.Open)))
            {
                binaryReader.ReadBytes(2); // 01PRD skip
                string prsFilePath = Utilities.ToUnicode(binaryReader, 256);
                string prsFile = Path.GetFileName(prsFilePath).ToUpperInvariant();
                string prsPath = Path.Combine(dataDir, prsFile);

                Console.WriteLine($"Looking for PRS file: {prsPath}");
                Console.WriteLine($"PRS file exists: {File.Exists(prsPath)}");

                if (string.IsNullOrEmpty(prsFile) || (!string.IsNullOrEmpty(prsFile) && !File.Exists(prsPath)))
                {
                    Console.WriteLine("Invalid resource: " + prdFile);
                    return;
                }

                ProcessAssets(binaryReader, prsPath);
            }
        }

        private void ProcessAssets(BinaryReader binaryReader, string prsPath)
        {
            binaryReader.ReadBytes(12); // 03PRD skip
            short assetCount = binaryReader.ReadInt16(); // 04PRD number of assets

            for (int i = 0; i < assetCount; i++)
            {
                ProcessSingleAsset(binaryReader, prsPath);
            }
        }

        private void ProcessSingleAsset(BinaryReader binaryReader, string prsPath)
        {
            binaryReader.ReadBytes(10); // 01AST skip
            Asset asset = new Asset
            {
                offset = binaryReader.ReadInt32(),
                assetType = Utilities.ToUnicode(binaryReader, 4).ToUpperInvariant(),
                id = binaryReader.ReadInt16(),
                name = Utilities.ToUnicode(binaryReader, 18),
                length = binaryReader.ReadInt32(),
                prsPath = prsPath
            };

            if (asset.length == 0 || asset.offset == 0) return;

            LogAssetInfo(asset);
            ExtractAsset(asset);
        }

        private void LogAssetInfo(Asset asset)
        {
            Console.WriteLine("{0,-20} | {1,-10} | {2,-10} | {3,-10} | {4,-10}",
                asset, asset.assetType, asset.offset, asset.length, asset.id);
        }

        private void ExtractAsset(Asset asset)
        {
            switch (asset.assetType)
            {
                case "SND":
                    audioExtractor.ExtractSound(asset);
                    Console.WriteLine("Extracted sound: " + asset.name);
                    break;

                case "PCM":
                    audioExtractor.ExtractPCM(asset);
                    Console.WriteLine("Extracted pcm: " + asset.name);
                    break;

                case "CLU":
                    spriteExtractor.LoadPaletteFromPrs(asset.prsPath, asset.offset, asset.length);
                    Console.WriteLine($"Loaded palette from {asset.name}");
                    break;

                case "PAK":
                    Console.WriteLine($"Processing PAK file: {asset.name}");
                    var spriteData = spriteExtractor.LoadPak(asset);
                    if (spriteData.ImageData != null)
                    {
                        Console.WriteLine($"Extracted sprite: {asset.name} ({spriteData.Width}x{spriteData.Height}, {spriteData.Frames?.Length ?? 0} frames)");
                    }
                    break;
            }
        }
    }
} 