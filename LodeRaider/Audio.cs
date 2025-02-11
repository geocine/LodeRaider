using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LodeRaider
{
    internal class Audio
    {

        static int[] adaptationTable = new int[]
        {
            230, 230, 230, 230, 307, 409, 512, 614,
            768, 614, 512, 409, 307, 230, 230, 230
        };

        static int[] adaptationCoeff1 = new int[]
        {
            256, 512, 0, 192, 240, 460, 392
        };

        static int[] adaptationCoeff2 = new int[]
        {
            0, -256, 0, 64, 0, -208, -232
        };

        struct MsAdpcmState
        {
            public int delta;
            public int sample1;
            public int sample2;
            public int coeff1;
            public int coeff2;
        }

        static int AdpcmMsExpandNibble(ref MsAdpcmState channel, int nibble)
        {
            int nibbleSign = nibble - (((nibble & 0x08) != 0) ? 0x10 : 0);
            int predictor = ((channel.sample1 * channel.coeff1) + (channel.sample2 * channel.coeff2)) / 256 + (nibbleSign * channel.delta);

            if (predictor < -32768)
                predictor = -32768;
            else if (predictor > 32767)
                predictor = 32767;

            channel.sample2 = channel.sample1;
            channel.sample1 = predictor;

            channel.delta = (adaptationTable[nibble] * channel.delta) / 256;
            if (channel.delta < 16)
                channel.delta = 16;

            return predictor;
        }

        // Convert buffer containing MS-ADPCM wav data to a 16-bit signed PCM buffer
        internal static byte[] ConvertMsAdpcmToPcm(byte[] buffer, int offset, int count, int channels, int blockAlignment)
        {
            Console.WriteLine($"Converting MSADPCM: offset={offset}, count={count}, channels={channels}, blockAlignment={blockAlignment}");
            
            MsAdpcmState channel0 = new MsAdpcmState();
            MsAdpcmState channel1 = new MsAdpcmState();

            try 
            {
                // Calculate samples per block more safely using checked arithmetic
                int bytesPerChannel = blockAlignment / channels;
                int samplesPerBlock = checked(((bytesPerChannel - 7) * 2) + 2);
                
                // Calculate total samples more safely
                int fullBlocks = count / blockAlignment;
                int remainingBytes = count % blockAlignment;
                int samplesInLastBlock = 0;
                
                if (remainingBytes > 0)
                {
                    int lastBlockBytesPerChannel = remainingBytes / channels;
                    if (lastBlockBytesPerChannel > 7) // Ensure we have enough bytes for a valid block
                    {
                        samplesInLastBlock = ((lastBlockBytesPerChannel - 7) * 2) + 2;
                    }
                }
                
                // Calculate total samples with overflow checking
                long totalSamplesLong = checked((long)fullBlocks * samplesPerBlock + samplesInLastBlock);
                if (totalSamplesLong > int.MaxValue)
                {
                    throw new OverflowException("Sample count too large");
                }
                int totalSamples = (int)totalSamplesLong;
                
                // Allocate output buffer with overflow checking
                long bufferSize = checked((long)totalSamples * sizeof(short) * channels);
                if (bufferSize > int.MaxValue)
                {
                    throw new OverflowException("Buffer size too large");
                }
                var samples = new byte[(int)bufferSize];
                int sampleOffset = 0;

                bool stereo = channels == 2;

                while (count > 0)
                {
                    try 
                    {
                        int blockSize = Math.Min(blockAlignment, count);  // Use Math.Min to prevent overflow
                        count -= blockSize;  // Reduce count by actual block size used

                        // Calculate samples in this block safely
                        long samplesInBlock = ((long)(blockSize / channels) - 7) * 2 + 2;
                        if (samplesInBlock < 2 || samplesInBlock > int.MaxValue)
                            break;

                        int totalSamplesInBlock = (int)samplesInBlock;

                        int offsetStart = offset;
                        int blockPredictor = buffer[offset];
                        ++offset;
                        if (blockPredictor > 6)
                            blockPredictor = 6;
                        channel0.coeff1 = adaptationCoeff1[blockPredictor];
                        channel0.coeff2 = adaptationCoeff2[blockPredictor];
                        if (stereo)
                        {
                            blockPredictor = buffer[offset];
                            ++offset;
                            if (blockPredictor > 6)
                                blockPredictor = 6;
                            channel1.coeff1 = adaptationCoeff1[blockPredictor];
                            channel1.coeff2 = adaptationCoeff2[blockPredictor];
                        }

                        channel0.delta = buffer[offset];
                        channel0.delta |= buffer[offset + 1] << 8;
                        if ((channel0.delta & 0x8000) != 0)
                            channel0.delta -= 0x10000;
                        offset += 2;
                        if (stereo)
                        {
                            channel1.delta = buffer[offset];
                            channel1.delta |= buffer[offset + 1] << 8;
                            if ((channel1.delta & 0x8000) != 0)
                                channel1.delta -= 0x10000;
                            offset += 2;
                        }

                        channel0.sample1 = buffer[offset];
                        channel0.sample1 |= buffer[offset + 1] << 8;
                        if ((channel0.sample1 & 0x8000) != 0)
                            channel0.sample1 -= 0x10000;
                        offset += 2;
                        if (stereo)
                        {
                            channel1.sample1 = buffer[offset];
                            channel1.sample1 |= buffer[offset + 1] << 8;
                            if ((channel1.sample1 & 0x8000) != 0)
                                channel1.sample1 -= 0x10000;
                            offset += 2;
                        }

                        channel0.sample2 = buffer[offset];
                        channel0.sample2 |= buffer[offset + 1] << 8;
                        if ((channel0.sample2 & 0x8000) != 0)
                            channel0.sample2 -= 0x10000;
                        offset += 2;
                        if (stereo)
                        {
                            channel1.sample2 = buffer[offset];
                            channel1.sample2 |= buffer[offset + 1] << 8;
                            if ((channel1.sample2 & 0x8000) != 0)
                                channel1.sample2 -= 0x10000;
                            offset += 2;
                        }

                        if (stereo)
                        {
                            samples[sampleOffset] = (byte)channel0.sample2;
                            samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                            samples[sampleOffset + 2] = (byte)channel1.sample2;
                            samples[sampleOffset + 3] = (byte)(channel1.sample2 >> 8);
                            samples[sampleOffset + 4] = (byte)channel0.sample1;
                            samples[sampleOffset + 5] = (byte)(channel0.sample1 >> 8);
                            samples[sampleOffset + 6] = (byte)channel1.sample1;
                            samples[sampleOffset + 7] = (byte)(channel1.sample1 >> 8);
                            sampleOffset += 8;
                        }
                        else
                        {
                            samples[sampleOffset] = (byte)channel0.sample2;
                            samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                            samples[sampleOffset + 2] = (byte)channel0.sample1;
                            samples[sampleOffset + 3] = (byte)(channel0.sample1 >> 8);
                            sampleOffset += 4;
                        }

                        blockSize -= (offset - offsetStart);
                        if (stereo)
                        {
                            for (int i = 0; i < blockSize; ++i)
                            {
                                int nibbles = buffer[offset];

                                int sample = AdpcmMsExpandNibble(ref channel0, nibbles >> 4);
                                samples[sampleOffset] = (byte)sample;
                                samples[sampleOffset + 1] = (byte)(sample >> 8);

                                sample = AdpcmMsExpandNibble(ref channel1, nibbles & 0x0f);
                                samples[sampleOffset + 2] = (byte)sample;
                                samples[sampleOffset + 3] = (byte)(sample >> 8);

                                sampleOffset += 4;
                                ++offset;
                            }
                        }
                        else
                        {
                            for (int i = 0; i < blockSize; ++i)
                            {
                                int nibbles = buffer[offset];

                                int sample = AdpcmMsExpandNibble(ref channel0, nibbles >> 4);
                                samples[sampleOffset] = (byte)sample;
                                samples[sampleOffset + 1] = (byte)(sample >> 8);

                                sample = AdpcmMsExpandNibble(ref channel0, nibbles & 0x0f);
                                samples[sampleOffset + 2] = (byte)sample;
                                samples[sampleOffset + 3] = (byte)(sample >> 8);

                                sampleOffset += 4;
                                ++offset;
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        throw;
                    }
                }

                return samples;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ConvertMsAdpcmToPcm: {ex.Message}");
                Console.WriteLine($"Parameters: buffer.Length={buffer.Length}, offset={offset}, count={count}, channels={channels}, blockAlignment={blockAlignment}");
                throw;
            }
        }

        // Convert 8-bit PCM to 16-bit PCM
        internal static byte[] ConvertPcm8bitTo16bit(byte[] data)
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

        internal static void WriteAudioDataToWav(byte[] audioData, string fileName, int numChannels = 1, int sampleRate = 22050)
        {
            // Set the sample rate, number of channels, and bits per sample
            int numBitsPerSample = 16; // Always use 16-bit for WAV files
            int numBytesPerSample = numChannels * numBitsPerSample / 8; // 2 bytes per sample
            int numBytesPerSecond = sampleRate * numBytesPerSample; // 44100 bytes per second

            using (FileStream fileStream = new FileStream($"{fileName}.wav", FileMode.Create))
            using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
            {
                // Write the wave file headers
                binaryWriter.Write("RIFF".ToCharArray());
                binaryWriter.Write((int)(36 + audioData.Length)); // file size, 36 bytes in the header
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
                binaryWriter.Write(audioData.Length); // data size
                                                      // Write the audio data
                binaryWriter.Write(audioData);
            }
        }

    }
}
