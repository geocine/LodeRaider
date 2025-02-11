using System;
using System.IO;
using System.Text;

namespace LodeRaider
{
    public class AudioExtractor
    {
        private readonly string outputDir;

        public AudioExtractor(string outputDirectory = "audio")
        {
            outputDir = outputDirectory;
            Directory.CreateDirectory(outputDir);
        }

        public void ExtractSound(Asset asset)
        {
            using (var binaryStream = File.Open(asset.prsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var binaryReader = new BinaryReader(binaryStream);
                Utilities.Seek(binaryReader, asset);

                Console.WriteLine($"Processing sound: {asset.name}");
                Console.WriteLine($"Asset length: {asset.length}");

                if (binaryReader.ReadUInt16() != 4)
                {
                    Console.WriteLine($"Invalid sound file: {asset.name}");
                    return;
                }

                // Read the sound data and convert it to 16-bit PCM
                uint dataLength = binaryReader.ReadUInt32() + 1U;
                Console.WriteLine($"Sound data length: {dataLength}");

                byte[] soundData = binaryReader.ReadBytes((int)dataLength);
                Console.WriteLine($"Read {soundData.Length} bytes");

                try 
                {
                    byte[] waveData = Audio.ConvertPcm8bitTo16bit(soundData);
                    Console.WriteLine($"Converted to {waveData.Length} bytes PCM");
                    
                    var fileName = Utilities.NormalizeFilename(asset);
                    string outputPath = Path.Combine(outputDir, $"{fileName}.wav");
                    Audio.WriteAudioDataToWav(waveData, outputPath, 1, 22050);
                    Console.WriteLine($"Saved audio: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {asset.name}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        public void ExtractPCM(Asset asset)
        {
            using (var binaryStream = File.Open(asset.prsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var binaryReader = new BinaryReader(binaryStream);
                Utilities.Seek(binaryReader, asset);

                var header = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
                if (header != "RIFF")
                {
                    Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
                    return;
                }

                binaryReader.ReadInt32(); // Skip size
                var format = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
                if (format != "WAVE")
                {
                    Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
                    return;
                }

                WaveHeader waveHeader = default;
                while (binaryReader.BaseStream.Position < (asset.offset + asset.length))
                {
                    string section = Encoding.ASCII.GetString(binaryReader.ReadBytes(4));
                    int chunkSize = binaryReader.ReadInt32();

                    if (section == "fmt ")
                    {
                        waveHeader.FormatType = binaryReader.ReadUInt16();
                        waveHeader.NumChannels = binaryReader.ReadUInt16();
                        waveHeader.SampleRate = binaryReader.ReadUInt32();
                        waveHeader.ByteRate = binaryReader.ReadUInt32();
                        waveHeader.BlockAlign = binaryReader.ReadUInt16();
                        waveHeader.BitsPerSample = binaryReader.ReadUInt16();
                        waveHeader.CbSize = binaryReader.ReadUInt16();
                        waveHeader.DataSize = binaryReader.ReadUInt16();

                        // Read MSADPCM specific data
                        var numSamples = binaryReader.ReadUInt16();
                        binaryReader.ReadBytes(numSamples * 4); // Skip coefficient data

                        if (waveHeader.FormatType != 2 || waveHeader.BitsPerSample != 4)
                        {
                            Console.WriteLine($"Not a valid MSADPCM file: {asset.name}");
                            return;
                        }
                    }
                    else if (section == "data")
                    {
                        byte[] soundData = binaryReader.ReadBytes(chunkSize);
                        byte[] pcmData = Audio.ConvertMsAdpcmToPcm(
                            soundData, 
                            0, 
                            chunkSize, 
                            (short)waveHeader.NumChannels, 
                            (short)waveHeader.BlockAlign
                        );

                        var fileName = Utilities.NormalizeFilename(asset);
                        string outputPath = Path.Combine(outputDir, $"{fileName}.wav");
                        Audio.WriteAudioDataToWav(
                            pcmData, 
                            outputPath,
                            waveHeader.NumChannels, 
                            (int)waveHeader.SampleRate
                        );
                        Console.WriteLine($"Saved audio: {outputPath}");
                        return;
                    }
                    else
                    {
                        // Skip unknown chunks
                        binaryReader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                    }
                }
            }
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
} 