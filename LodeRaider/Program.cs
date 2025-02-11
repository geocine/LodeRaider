using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LodeRaider;
using System.IO;

// Before starting the main processing
string logFile = "asset_extraction.log";
// Clear previous log
File.WriteAllText(logFile, "");

// Create a writer that writes to both console and file
using (var fileWriter = new StreamWriter(logFile, true))
using (var multiWriter = new MultiWriter(fileWriter, Console.Out))
{
    Console.SetOut(multiWriter);

    // Directory containing game assets
    //string dataDir = Path.Combine(Environment.CurrentDirectory, "DATA");
    string dataDir = "E:\\Reverse\\LODERUNNER\\LODERUNN-WIN-2302\\LODERUNN-ASSET\\DATA";

    // Process each PRD (resource definition) file
    foreach (string text in Directory.EnumerateFiles(dataDir, "*.PRD"))
    {
        using (BinaryReader binaryReader = new BinaryReader(File.Open(text, FileMode.Open)))
        {
            binaryReader.ReadBytes(2); // 01PRD skip
            string prsFilePath = ToUnicode(binaryReader, 256); // 02PRD path to prs file
            string prsFile = Path.GetFileName(prsFilePath).ToUpperInvariant();
            string prsPath = Path.Combine(dataDir, prsFile);

            // Add debug output
            Console.WriteLine($"Looking for PRS file: {prsPath}");
            Console.WriteLine($"PRS file exists: {File.Exists(prsPath)}");

            if (string.IsNullOrEmpty(prsFile) || (!string.IsNullOrEmpty(prsFile) && !File.Exists(prsPath)))
            {
                Console.WriteLine("Invalid resource: " + text);
                continue;
            }
            binaryReader.ReadBytes(12); // 03PRD skip
            short num = binaryReader.ReadInt16(); // 04PRD number of assets
            for (int i = 0; i < (int)num; i++)
            {
                // asset info
                binaryReader.ReadBytes(10); // 01AST skip
                Asset asset = new Asset
                {
                    offset = binaryReader.ReadInt32(), // 02AST offset
                    assetType = ToUnicode(binaryReader, 4).ToUpperInvariant(), // 03AST type
                    id = binaryReader.ReadInt16(), // 04AST id
                    name = ToUnicode(binaryReader, 18), // 05AST name
                    length = binaryReader.ReadInt32(), // 06AST length
                    prsPath = prsPath
                };
                if (asset.length != 0 && asset.offset != 0)
                {
                    // print name | type | offset | length | id
                    Console.WriteLine("{0,-20} | {1,-10} | {2,-10} | {3,-10} | {4,-10}", 
                        asset, asset.assetType, asset.offset, asset.length, asset.id);
                    
                    if (asset.assetType == "SND") //&& asset.name == "sierra")
                    {
                        ExtractSound(asset);
                        Console.WriteLine("Extracted sound: " + asset.name);
                    }
                    if (asset.assetType == "PCM") //&& asset.name == "CREDITS")
                    {
                        ExtractPCM(asset);
                        Console.WriteLine("Extracted pcm: " + asset.name);
                    }
                    if (asset.assetType == "PAK") //&& asset.name == "Rope Trap 3")
                    {
                        var pakData = LoadPak(asset);
                        Console.WriteLine($"{asset.name,-20} | {asset.assetType,-10} | {asset.offset,-10} | {asset.length,-10} | {asset.id,-10}");
                    }
                }
            }
        }
    }
}

// Convert bytes to Unicode string, keeping original implementation
string ToUnicode(BinaryReader binaryReader, int length)
{
    string hexString = BitConverter.ToString(binaryReader.ReadBytes(length), 0, length);
    if (!string.IsNullOrEmpty(hexString))
    {
        StringBuilder sb = new StringBuilder();
        foreach (string substr in hexString.Split('-'))
        {
            // Convert the hex string to an integer
            int value = Convert.ToInt32(substr, 16);
            if (value != 0)
            {
                // Convert the integer to its Unicode equivalent character and append it to the StringBuilder
                sb.Append(char.ConvertFromUtf32(value));
            }
        }
        return sb.ToString();
    }
    return hexString;
}

// Helper to seek within PRS file
void Seek(BinaryReader binaryReader, Asset asset)
{
    if (binaryReader == null) return;
    Stream baseStream = binaryReader.BaseStream;
    if (baseStream == null) return;
    baseStream.Seek(asset.offset, SeekOrigin.Begin);
}

// Original working ExtractPCM implementation
void ExtractPCM(Asset asset)
{
    // Open the binary stream
    using (var binaryStream = File.Open(asset.prsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        // Create a binary reader to read the sound data
        var binaryReader = new BinaryReader(binaryStream);

        // Seek to the data as specified by the asset
        Seek(binaryReader, asset);
        var header = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
        if (header != "RIFF")
        {
            Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
            return;
        }
        binaryReader.ReadInt32();
        var format = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
        if (format != "WAVE")
        {
            Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
            return;
        }

        WaveHeader waveHeader = default(WaveHeader);
        while (!(binaryReader == null || binaryReader.BaseStream.Position == (asset.offset + asset.length)))
        {
            string section = Encoding.ASCII.GetString(binaryReader.ReadBytes(4)); // fmt desc header
            int num = binaryReader.ReadInt32(); // size of wave section chunk
            if (!(section == "fmt "))
            {
                if (section == "data")
                {
                    byte[] soundData = binaryReader.ReadBytes(num);
                    byte[] pcmData = Audio.ConvertMsAdpcmToPcm(soundData, 0, num, (short)waveHeader.NumChannels, (short)waveHeader.BlockAlign);

                    int sampleRate = (int)waveHeader.SampleRate;
                    int numChannels = waveHeader.NumChannels;

                    var fileName = NormalizeFilename(asset);
                    Audio.WriteAudioDataToWav(pcmData, fileName, numChannels, sampleRate);
                    return;
                }
                binaryReader.BaseStream.Seek((long)num, SeekOrigin.Current);
            }
            else
            {
                waveHeader.FormatType = binaryReader.ReadUInt16();
                waveHeader.NumChannels = binaryReader.ReadUInt16();
                waveHeader.SampleRate = binaryReader.ReadUInt32();
                waveHeader.ByteRate = binaryReader.ReadUInt32();
                waveHeader.BlockAlign = binaryReader.ReadUInt16();
                waveHeader.BitsPerSample = binaryReader.ReadUInt16();
                waveHeader.CbSize = binaryReader.ReadUInt16();
                waveHeader.DataSize = binaryReader.ReadUInt16();
                // Read Num Channels
                var numSamples = binaryReader.ReadUInt16();
                // Read all 7 samples, 7 * 4bytes (32 bits) , 2 bytes per channel
                binaryReader.ReadBytes(numSamples * 4);
                if (waveHeader.FormatType != 2 || waveHeader.BitsPerSample != 4)
                {
                    Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
                    return;
                }
            }
        }
        Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
        return;
    }
}

// Original working ExtractSound implementation
void ExtractSound(Asset asset)
{
    using (var binaryStream = File.Open(asset.prsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var binaryReader = new BinaryReader(binaryStream);
        Seek(binaryReader, asset);

        // Add debug output
        Console.WriteLine($"Processing sound: {asset.name}");
        Console.WriteLine($"Asset length: {asset.length}");

        if (binaryReader.ReadUInt16() != 4)
        {
            Console.WriteLine($"Invalid sound file: {asset.name}");
            return;
        }

        // Read the sound data and convert it to 16-bit PCM
        uint num = binaryReader.ReadUInt32() + 1U;
        Console.WriteLine($"Sound data length: {num}");

        byte[] soundData = binaryReader.ReadBytes((int)num);
        Console.WriteLine($"Read {soundData.Length} bytes");

        try 
        {
            byte[] waveData = Audio.ConvertPcm8bitTo16bit(soundData);
            Console.WriteLine($"Converted to {waveData.Length} bytes PCM");
            
            // Create a new file stream and binary writer
            var fileName = NormalizeFilename(asset);
            Audio.WriteAudioDataToWav(waveData, fileName, 1, 22050);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {asset.name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}

// Placeholder for PAK loading implementation
object LoadPak(Asset asset)
{
    using (var binaryReader = new BinaryReader(File.Open(asset.prsPath, FileMode.Open)))
    {
        binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
        
        // TODO: Implement PAK loading
        // Based on NOTES.md:
        // 1. Get IMG with same name as PAK
        // 2. Seek PAK
        // 3. LoadPak return struct
        // 4. Seek PAK (resets)
        // 5. Return texture 2d
        // 6. Seek IMG
        // 7. Return rectangle array
        // 8. Get MUV with same name as PAK
        // 9. Seek MUV (Movie)
        // 10. Return Struct27
        
        return null;
    }
}

string NormalizeFilename(Asset asset)
{
    var name = Regex.Replace(asset.name, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
    if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
    {
        name = asset.id.ToString();
    }
    return name;
}

struct Asset
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

struct WaveHeader
{
    public ushort FormatType;      // wave type format
    public ushort NumChannels;     // number of channels (mono/stereo)
    public uint SampleRate;        // sample rate in Hz
    public uint ByteRate;          // bytes per second
    public ushort BlockAlign;      // block align
    public ushort BitsPerSample;   // bits per sample
    public ushort CbSize;          // Extra format bytes
    public ushort DataSize;        // Size of data chunk
}