using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;


string dataDir = Path.Combine(Environment.CurrentDirectory, "DATA");
string[] excludeFiles = new string[] { };

foreach (string text in Directory.EnumerateFiles(dataDir, "*.PRD"))
{
    if (!excludeFiles.Contains(text.Substring(text.Length - 6)))
    {
        using (BinaryReader binaryReader = new BinaryReader(File.Open(text, FileMode.Open)))
        {
            binaryReader.ReadBytes(2); // 01PRD skip
            string prsFilePath = ToUnicode(binaryReader, 256); // 02PRD path to prs file
            string prsFile = Path.GetFileName(prsFilePath).ToUpperInvariant();
            string prsPath = Path.Combine(dataDir, prsFile);
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
                    Console.WriteLine("{0,-20} | {1,-10} | {2,-10} | {3,-10} | {4,-10}", asset, asset.assetType, asset.offset, asset.length, asset.id);
                    // if (asset.assetType == "SND")
                    // {
                    //    ExtractSound(asset);
                    //    Console.WriteLine("Extracted sound: " + asset.name);
                    // }
                }
            }
        }
    }
}


// seek inside the PRS file
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
    // seek to the offset
    baseStream.Seek((long)asset.offset, SeekOrigin.Begin);
}

// SND
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
        int sampleRate = 22050; // Sound files are always 22050 Hz
        int numChannels = 1; // Always use mono for WAV files
        int numBitsPerSample = 16; // Always use 16-bit for WAV files
        int numBytesPerSample = numChannels * numBitsPerSample / 8; // 2 bytes per sample
        int numBytesPerSecond = sampleRate * numBytesPerSample; // 44100 bytes per second

        // Create a new file stream and binary writer
        var fileName = NormalizeFilename(asset);
        using (FileStream fileStream = new FileStream($"{fileName}.wav", FileMode.Create))
        using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
        {
            // Write the wave file headers
            binaryWriter.Write("RIFF".ToCharArray());
            binaryWriter.Write((int)(36 + waveData.Length)); // file size , 36 bytes in the header
            binaryWriter.Write("WAVE".ToCharArray());
            binaryWriter.Write("fmt ".ToCharArray());
            binaryWriter.Write(16); // 16 bytes in the fmt chunk
            binaryWriter.Write((short)1); // PCM audio format
            binaryWriter.Write((short)numChannels);
            binaryWriter.Write(sampleRate); // sample rate
            binaryWriter.Write(numBytesPerSecond); // bytes per second
            binaryWriter.Write((short)numBytesPerSample);  // block align
            binaryWriter.Write((short)numBitsPerSample); // 16 bits per sample
            binaryWriter.Write("data".ToCharArray());
            binaryWriter.Write(waveData.Length); // data size

            // Write the audio data
            binaryWriter.Write(waveData);
        }
    }
}

// Convert 8-bit PCM to 16-bit PCM
byte[] To16BitPCM(byte[] data)
{
    byte[] array = new byte[data.Length * 2]; // twice the length because 16-bit
    for (int i = 0; i < data.Length; i++)
    {
        // This line takes the current 8-bit sample from the input array and subtracts 128 from it. 
        // This is because 8-bit audio samples are typically signed, meaning that their range goes from -128 
        // to +127. By subtracting 128, we center the range around 0

        // The result is then shifted left by 8 bits, effectively multiplying it by 256. 
        // This is because each 16-bit sample requires two bytes, and there are 8 bits in a byte. 
        // So shifting left by 8 bits effectively moves the 8-bit sample to the high byte of the 16-bit sample.
        
        // This correctly map the range of the 8-bit sample to the larger range of the 16-bit sample  while maintaining the same center point.
        short num = (short)(data[i] - 128 << 8); // 8-bit to 16-bit
        // This line stores the low byte of the converted 16-bit sample in the output "array" at the current index multiplied by 2. 
        // This is because each 16-bit sample requires two bytes, and we are storing the low byte first.
        array[i * 2] = (byte)num; // low byte
        // This line stores the high byte of the converted 16-bit sample in the output "array" at the current index multiplied by 2 plus 1. 
        // This is because each 16-bit sample requires two bytes, and we are storing the high byte second.
        // The high byte is extracted from the short "num" by shifting it to the right by 8 bits. 
        // This effectively discards the low byte and keeps only the high byte.
        array[i * 2 + 1] = (byte)(num >> 8); // high byte
    }
    return array;
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