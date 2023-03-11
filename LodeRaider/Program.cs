using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LodeRaider;


string dataDir = Path.Combine(Environment.CurrentDirectory, "DATA");

foreach (string text in Directory.EnumerateFiles(dataDir, "*.PRD"))
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
                // if (asset.assetType == "SND" && asset.name == "sierra")
                // {
                //     ExtractSound(asset);
                //     Console.WriteLine("Extracted sound: " + asset.name);
                // }
                //if (asset.assetType == "PCM" && asset.name == "CREDITS")
                //{
                //    ExtractPCM(asset);
                //    Console.WriteLine("Extracted pcm: " + asset.name);
                //}
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
            Console.WriteLine($"Not a valid wave file: {asset.name}");
            return;
        }
        binaryReader.ReadInt32();
        var format = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
        if (format != "WAVE")
        {
            Console.WriteLine($"Not a valid wave file: {asset.name}");
            return;
        }

        WaveData waveData = default(WaveData);
        while (!(binaryReader == null || binaryReader.BaseStream.Position == (asset.offset + asset.length)))
        {
            string section = Encoding.ASCII.GetString(binaryReader.ReadBytes(4)); // fmt desc header
            int num = binaryReader.ReadInt32(); // size of wave section chunk
            if (!(section == "fmt "))
            {

                if (section == "data")
                {

                    int sampleRate = (int)waveData.Header.SampleRate;
                    int numChannels = waveData.Header.NumChannels; // Always use mono for WAV files
                    int numBitsPerSample = 16; // Always use 16-bit for WAV files
                    int numBytesPerSample = numChannels * numBitsPerSample / 8; // 2 bytes per sample
                    int numBytesPerSecond = sampleRate * numBytesPerSample;

                    Console.WriteLine("write data");
                    var fileName = NormalizeFilename(asset);
                    using (FileStream fileStream = new FileStream($"{fileName}.wav", FileMode.Create))
                    using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
                    {

                        // Write the wave file headers
                        binaryWriter.Write("RIFF".ToCharArray());
                        binaryWriter.Write((int)(36 + num)); // file size , 36 bytes in the header
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
                        binaryWriter.Write(num); // data size

                        byte[] soundData = binaryReader.ReadBytes(num);

                        byte[] pcmData = Audio.ConvertMsAdpcmToPcm(soundData, 0, num, (short)waveData.Header.NumChannels, (short)waveData.Header.BlockAlign);

                        // Write the audio data
                        binaryWriter.Write(pcmData);


                    }
                    return;
                }
                binaryReader.BaseStream.Seek((long)num, SeekOrigin.Current);
            }
            else
            {
                waveData.Header = new WaveHeader
                {
                    FormatType = binaryReader.ReadUInt16(),
                    NumChannels = binaryReader.ReadUInt16(),
                    SampleRate = binaryReader.ReadUInt32(),
                    ByteRate = binaryReader.ReadUInt32(),
                    BlockAlign = binaryReader.ReadUInt16(),
                    BitsPerSample = binaryReader.ReadUInt16(),
                    CbSize = binaryReader.ReadUInt16()
                };
                waveData.DataSize = binaryReader.ReadUInt16();
                waveData.NumSamples = binaryReader.ReadUInt16();
                waveData.Samples = new WaveSample[7]; // 7 channels max
                for (int i = 0; i < (int)waveData.NumSamples; i++)
                {
                    waveData.Samples[i].LeftChannel = binaryReader.ReadInt16();
                    waveData.Samples[i].RightChannel = binaryReader.ReadInt16();
                }
                if (waveData.Header.FormatType != 2 || waveData.Header.BitsPerSample != 4)
                {
                    // Incorrect file
                    return;
                }
            }
        }
        // Incorrect file
        return;
    }
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
        byte[] waveData = Audio.ConvertPcm8bitTo16bit(soundData);

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

struct WaveHeader
{
    public ushort FormatType;      // wave type format
    public ushort NumChannels;     // number of channels (mono/stereo)
    public uint SampleRate;        // sample rate in Hz
    public uint ByteRate;          // bytes per second
    public ushort BlockAlign;      // block align
    public ushort BitsPerSample;   // bits per sample
    public ushort CbSize;         // unknown field with value 32 (0x20) which is a space character
}

internal struct WaveData
{
    public WaveHeader Header;      // wave file header
    public ushort DataSize;        // size of remaining data in bytes
    public ushort NumSamples;      // number of audio samples
    public WaveSample[] Samples;   // array of audio samples
}

internal struct WaveSample
{
    public short LeftChannel;      // left channel audio sample
    public short RightChannel;     // right channel audio sample (if stereo)
}
