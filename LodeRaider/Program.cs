using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

string dataDir = @"\DATA";

string[] excludeFiles = new string[] { "02.PRD", "09.PRD", "10.PRD", "11.PRD", "12.PRD" };
foreach (string text in Directory.EnumerateFiles(dataDir, "*.PRD"))
{
    if (!excludeFiles.Contains(text.Substring(text.Length - 6)))
    {
        using (BinaryReader binaryReader = new BinaryReader(File.Open(text, FileMode.Open)))
        {
            binaryReader.ReadBytes(2);
            string prsFile = Path.GetFileNameWithoutExtension(text).ToUpperInvariant() + ".PRS";
            string placeHolder = ToUnicode(binaryReader, 256);
            string directoryHeader = "RDpcTU9SRUNPREVcTE9ERVJVTk5cREFUQVw=";
            directoryHeader = Encoding.UTF8.GetString(Convert.FromBase64String(directoryHeader));
            if (!string.Equals(placeHolder, directoryHeader + prsFile, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Invalid directory: " + text);
                continue;
            }
            string prsPath = Path.Combine(dataDir, prsFile);
            binaryReader.ReadBytes(12);
            short num = binaryReader.ReadInt16();
            for (int i = 0; i < (int)num; i++)
            {
                // Exclude UIX/PZL
                binaryReader.ReadBytes(10);
                Asset asset = new Asset
                {
                    offset = binaryReader.ReadInt32(),
                    assetType = ToUnicode(binaryReader, 4).ToUpperInvariant(),
                    id = binaryReader.ReadInt16(),
                    name = ToUnicode(binaryReader, 18),
                    length = binaryReader.ReadInt32(),
                    prsPath = prsPath
                };
                if (asset.length != 0 && asset.offset != 0)
                {
                    // print name | type | offset | length | id
                    Console.WriteLine("{0,-20} | {1,-10} | {2,-10} | {3,-10} | {4,-10}", asset, asset.assetType, asset.offset, asset.length, asset.id);
                    // if (asset.assetType == "SND")
                    // {
                    //     ExtractSound(asset);
                    //     Console.WriteLine("Extracted sound: " + asset.name);
                    // }
                }
            }
        }
    }
}

void Seek(BinaryReader binaryReader, Asset asset)
{
    if (binaryReader == null)
    {
        return;
    }
    Stream baseStream = binaryReader.BaseStream;
    if (baseStream == null)
    {
        return;
    }
    baseStream.Seek((long)asset.offset, SeekOrigin.Begin);
}

void ExtractSound(Asset asset)
{
    // Open the binary stream
    using (var binaryStream = File.Open(asset.prsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        // Create a binary reader to read the sound data
        var binaryReader = new BinaryReader(binaryStream);

        // Seek to the data as specified by the asset
        Seek(binaryReader, asset);
        if (binaryReader.ReadUInt16() != 4)
        {
            Console.WriteLine($"Invalid sound file: {asset.name}");
            return;
        }

        // Read the sound data and convert it to 16-bit PCM
        uint num = binaryReader.ReadUInt32() + 1U;
        byte[] soundData = binaryReader.ReadBytes((int)num);
        byte[] waveData = To16BitPCM(soundData);

        // Set the sample rate, number of channels, and bits per sample
        int sampleRate = 22050; // Modify this to match your game's sample rate
        int numChannels = 1; // Always use mono for WAV files
        int numBitsPerSample = 16; // Always use 16-bit for WAV files
        int numBytesPerSample = numChannels * numBitsPerSample / 8;
        int numBytesPerSecond = sampleRate * numBytesPerSample;

        // Create a new file stream and binary writer
        var fileName = NormalizeFilename(asset);
        using (FileStream fileStream = new FileStream($"{fileName}.wav", FileMode.Create))
        using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
        {
            // Write the wave file headers
            binaryWriter.Write("RIFF".ToCharArray());
            binaryWriter.Write((int)(36 + waveData.Length));
            binaryWriter.Write("WAVE".ToCharArray());
            binaryWriter.Write("fmt ".ToCharArray());
            binaryWriter.Write(16);
            binaryWriter.Write((short)1); // PCM audio format
            binaryWriter.Write((short)numChannels);
            binaryWriter.Write(sampleRate);
            binaryWriter.Write(numBytesPerSecond);
            binaryWriter.Write((short)numBytesPerSample);
            binaryWriter.Write((short)numBitsPerSample);
            binaryWriter.Write("data".ToCharArray());
            binaryWriter.Write(waveData.Length);

            // Write the audio data
            binaryWriter.Write(waveData);
        }
    }
}


byte[] To16BitPCM(byte[] data)
{
    byte[] array = new byte[data.Length * 2];
    for (int i = 0; i < data.Length; i++)
    {
        short num = (short)(data[i] - 128 << 8);
        array[i * 2] = (byte)num;
        array[i * 2 + 1] = (byte)(num >> 8);
    }
    return array;
}

//.PRS could stand for "packed resource file" or "compressed resource file," indicating that it contains compressed game data or resources.
//.PRD could stand for "product data file," indicating that it contains data related to the game product, such as level or character data.

string NormalizeFilename(Asset asset)
{
    var name = Regex.Replace(asset.name, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
    if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
    {
        name = asset.id.ToString();
    }
    return name;
}

string ToUnicode(BinaryReader binaryReader, int length)
{
    string text = BitConverter.ToString(binaryReader.ReadBytes(length), 0, length);
    if (!string.IsNullOrEmpty(text))
    {
        StringBuilder sb = new StringBuilder();
        foreach (string substr in text.Split('-'))
        {
            // Convert each substring to an integer in base 16 (hexadecimal
            int value = Convert.ToInt32(substr, 16);
            if (value != 0)
            {
                // Convert the integer to its Unicode equivalent character and append it to the StringBuilder
                sb.Append(char.ConvertFromUtf32(value));
            }
        }
        return sb.ToString();
    }
    return text;
}


struct Asset
{
    public override string ToString()
    {
        return string.IsNullOrEmpty(name.Trim()) ? "Resource" : name;
    }
    public string assetType;
    public short id;
    public string name;
    public int length;
    public int offset;
    public string prsPath;
}