using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Collections.Generic;

namespace LodeRaider
{
    public struct SpriteData
    {
        public int Width;
        public int Height;
        public byte[] ImageData;
        public Rectangle[] Frames;  // For animation frames
    }

    public class SpriteExtractor
    {
        private readonly string outputDir;
        private Color[] palette;
        private bool hasPalette = false;

        public SpriteExtractor(string outputDirectory = "sprites")
        {
            outputDir = outputDirectory;
            Directory.CreateDirectory(outputDir);
            // Initialize with default VGA palette
            InitializeDefaultPalette();
        }

        private void InitializeDefaultPalette()
        {
            palette = new Color[256];
            
            // XNA/FNA uses premultiplied alpha
            for (int i = 0; i < 256; i++)
            {
                // Convert 8-bit index to RGB components
                int r = ((i >> 5) & 0x07) * 32;  // 3 bits for red (0-7) scaled to 0-255
                int g = ((i >> 2) & 0x07) * 32;  // 3 bits for green (0-7) scaled to 0-255
                int b = (i & 0x03) * 64;         // 2 bits for blue (0-3) scaled to 0-255
                
                // Apply gamma correction (XNA uses sRGB)
                r = (int)(Math.Pow(r / 255.0, 2.2) * 255);
                g = (int)(Math.Pow(g / 255.0, 2.2) * 255);
                b = (int)(Math.Pow(b / 255.0, 2.2) * 255);
                
                palette[i] = Color.FromArgb(255, r, g, b);
            }
            
            // Set special colors
            palette[0] = Color.FromArgb(0, 0, 0, 0);      // Transparent black
            palette[1] = Color.FromArgb(255, 0, 0, 0);    // Solid black
            palette[255] = Color.FromArgb(255, 255, 255, 255); // White
        }

        public void LoadPaletteFromPrs(string prsPath, int offset, int length)
        {
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(prsPath)))
                {
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                    
                    // Read CLU header
                    uint magic = reader.ReadUInt32();
                    Console.WriteLine($"CLU Magic: 0x{magic:X8}");
                    
                    // Read palette size
                    short paletteSize = reader.ReadInt16();
                    Console.WriteLine($"Palette size: {paletteSize}");
                    
                    // Skip additional header bytes if needed
                    reader.BaseStream.Seek(2, SeekOrigin.Current); // Skip 2 bytes
                    
                    // Read palette data - 256 colors
                    palette = new Color[256];
                    
                    for (int i = 0; i < 256; i++)
                    {
                        // Each color is stored as 3 bytes (RGB)
                        byte r = (byte)(reader.ReadByte() * 4); // Scale from 6-bit to 8-bit
                        byte g = (byte)(reader.ReadByte() * 4);
                        byte b = (byte)(reader.ReadByte() * 4);
                        byte a = 255;
                        
                        palette[i] = Color.FromArgb(a, r, g, b);
                        
                        // Debug output for first few colors
                        if (i < 16)
                        {
                            Console.WriteLine($"Color {i}: R={r} G={g} B={b} A={a}");
                        }
                    }
                    
                    hasPalette = true;
                    Console.WriteLine("Loaded palette from CLU file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading palette: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                // Fall back to default palette
                InitializeDefaultPalette();
            }
        }

        public SpriteData LoadPak(Asset asset)
        {
            try 
            {
                using (var binaryReader = new BinaryReader(File.Open(asset.prsPath, FileMode.Open)))
                {
                    binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
                    
                    // Read and dump first 16 bytes to help debug the format
                    byte[] headerBytes = new byte[16];
                    int bytesRead = binaryReader.Read(headerBytes, 0, 16);
                    Console.WriteLine("First 16 bytes: " + BitConverter.ToString(headerBytes, 0, bytesRead));
                    
                    // Check if this is a PNG file (font texture)
                    if (headerBytes[0] == 0x89 && headerBytes[1] == 0x50 && headerBytes[2] == 0x4E && headerBytes[3] == 0x47)
                    {
                        Console.WriteLine("Detected PNG file (likely a font texture)");
                        // Reset position to start of PNG
                        binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
                        
                        // Load PNG directly as a Bitmap
                        using (var ms = new MemoryStream())
                        {
                            byte[] buffer = new byte[4096];
                            int read;
                            while ((read = binaryReader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ms.Write(buffer, 0, read);
                            }
                            ms.Position = 0;
                            
                            using (var bitmap = new Bitmap(ms))
                            {
                                var pngSpriteData = new SpriteData();
                                pngSpriteData.Width = bitmap.Width;
                                pngSpriteData.Height = bitmap.Height;
                                
                                // Convert bitmap to raw pixel data
                                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                                var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                
                                try
                                {
                                    int bytes = Math.Abs(bitmapData.Stride) * bitmap.Height;
                                    pngSpriteData.ImageData = new byte[bitmap.Width * bitmap.Height];
                                    
                                    // Convert ARGB to indexed color
                                    byte[] tempData = new byte[bytes];
                                    Marshal.Copy(bitmapData.Scan0, tempData, 0, bytes);
                                    
                                    for (int y = 0; y < bitmap.Height; y++)
                                    {
                                        for (int x = 0; x < bitmap.Width; x++)
                                        {
                                            int pixelIndex = y * bitmap.Width + x;
                                            int sourceIndex = y * Math.Abs(bitmapData.Stride) + x * 4;
                                            
                                            // Convert to grayscale and use as index
                                            byte r = tempData[sourceIndex + 2];
                                            byte g = tempData[sourceIndex + 1];
                                            byte b = tempData[sourceIndex + 0];
                                            byte a = tempData[sourceIndex + 3];
                                            
                                            // If pixel is transparent, use index 0
                                            if (a < 128)
                                            {
                                                pngSpriteData.ImageData[pixelIndex] = 0;
                                            }
                                            else
                                            {
                                                // Convert to grayscale index
                                                byte gray = (byte)((r * 0.299 + g * 0.587 + b * 0.114) / 16);
                                                pngSpriteData.ImageData[pixelIndex] = (byte)(gray + 1); // +1 to avoid index 0 (transparent)
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    bitmap.UnlockBits(bitmapData);
                                }
                                
                                return pngSpriteData;
                            }
                        }
                    }
                    
                    // Check if this is a wallpaper file (RLE pattern)
                    if (headerBytes[0] == 0x00 && headerBytes[1] == 0xA2 && headerBytes[2] == 0x61)
                    {
                        Console.WriteLine("Detected wallpaper file (RLE compressed)");
                        
                        // Reset position to start
                        binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
                        
                        // For wallpapers, we'll use a fixed size of 640x480 (game's base resolution)
                        var wallpaperSpriteData = new SpriteData();
                        wallpaperSpriteData.Width = 640;
                        wallpaperSpriteData.Height = 480;
                        
                        // Read the compressed data
                        byte[] compressedData = new byte[asset.length];
                        int read = binaryReader.Read(compressedData, 0, asset.length);
                        
                        // Decompress the data
                        wallpaperSpriteData.ImageData = DecompressWallpaperRLE(compressedData);
                        
                        return wallpaperSpriteData;
                    }
                    
                    // Reset position for normal PAK processing
                    binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
                    
                    var commandSprite = new SpriteData();
                    
                    // Read format ID
                    byte formatId = binaryReader.ReadByte();
                    Console.WriteLine($"Format ID: 0x{formatId:X2}");
                    Console.WriteLine($"Current file position after format ID: {binaryReader.BaseStream.Position}");

                    // Check for special format IDs
                    if (formatId == 0x8A || formatId == 0x89)
                    {
                        // Format 0x8A is RLE compressed
                        // First 16 bytes contain format info
                        binaryReader.BaseStream.Position = asset.offset;
                        byte[] header = new byte[16];
                        binaryReader.Read(header, 0, 16);
                        Console.WriteLine($"Header bytes: {BitConverter.ToString(header)}");
                        
                        // Skip format ID and try different byte orderings for dimensions
                        binaryReader.BaseStream.Position = asset.offset + 1;
                        
                        // Try different byte combinations for width/height
                        byte[] dimensionBytes = new byte[4];
                        binaryReader.Read(dimensionBytes, 0, 4);
                        Console.WriteLine($"Dimension bytes: {BitConverter.ToString(dimensionBytes)}");
                        
                        // Try different byte orderings
                        ushort[] possibleWidths = new ushort[] {
                            (ushort)((dimensionBytes[0] << 8) | dimensionBytes[1]),  // Big endian
                            (ushort)(dimensionBytes[0] | (dimensionBytes[1] << 8)),  // Little endian
                            (ushort)((dimensionBytes[1] << 8) | dimensionBytes[0]),  // Swapped big endian
                            (ushort)dimensionBytes[0]  // Single byte
                        };
                        
                        ushort[] possibleHeights = new ushort[] {
                            (ushort)((dimensionBytes[2] << 8) | dimensionBytes[3]),  // Big endian
                            (ushort)(dimensionBytes[2] | (dimensionBytes[3] << 8)),  // Little endian
                            (ushort)((dimensionBytes[3] << 8) | dimensionBytes[2]),  // Swapped big endian
                            (ushort)dimensionBytes[2]  // Single byte
                        };
                        
                        // Print all possibilities
                        for (int i = 0; i < possibleWidths.Length; i++)
                        {
                            Console.WriteLine($"Possible dimensions {i}: {possibleWidths[i]}x{possibleHeights[i]}");
                        }
                        
                        // Try to find valid dimensions
                        ushort width = 0, height = 0;
                        bool foundValid = false;
                        
                        for (int i = 0; i < possibleWidths.Length && !foundValid; i++)
                        {
                            if (possibleWidths[i] > 0 && possibleWidths[i] <= 800 &&
                                possibleHeights[i] > 0 && possibleHeights[i] <= 600)
                            {
                                width = possibleWidths[i];
                                height = possibleHeights[i];
                                foundValid = true;
                                Console.WriteLine($"Found valid dimensions: {width}x{height} (method {i})");
                            }
                        }
                        
                        if (!foundValid)
                        {
                            // If no valid dimensions found, try using the first byte of each as dimensions
                            width = dimensionBytes[0];
                            height = dimensionBytes[2];
                            
                            if (width > 0 && width <= 800 && height > 0 && height <= 600)
                            {
                                foundValid = true;
                                Console.WriteLine($"Using single-byte dimensions: {width}x{height}");
                            }
                        }
                        
                        if (!foundValid)
                        {
                            throw new Exception($"Could not determine valid dimensions for format 0x{formatId:X2} from bytes: {BitConverter.ToString(dimensionBytes)}");
                        }
                        
                        commandSprite.Width = width;
                        commandSprite.Height = height;
                        
                        // Read the compressed data
                        int dataStart = asset.offset + 5; // Skip format ID and dimensions
                        int compressedLength = asset.length - 5;
                        binaryReader.BaseStream.Position = dataStart;
                        
                        byte[] compressedData = binaryReader.ReadBytes(compressedLength);
                        
                        // Decompress using RLE
                        commandSprite.ImageData = DecompressRLE(compressedData);
                        
                        // Verify size
                        int expectedSize = width * height;
                        if (commandSprite.ImageData.Length != expectedSize)
                        {
                            Console.WriteLine($"Warning: Decompressed size {commandSprite.ImageData.Length} does not match expected size {expectedSize}");
                            Array.Resize(ref commandSprite.ImageData, expectedSize);
                        }
                        
                        return commandSprite;
                    }
                    // Handle command-based formats (0x15, 0x61, etc.)
                    else if ((formatId & 0x40) != 0 || formatId == 0x15 || formatId == 0x0E)
                    {
                        // Command-based format used for text, UI elements, and game options
                        Console.WriteLine($"Processing command-based format 0x{formatId:X2}");
                        
                        // First pass: determine dimensions
                        long startPos = binaryReader.BaseStream.Position - 1;
                        int maxX = 0, maxY = 0;
                        int x = 0, y = 0;
                        
                        while (binaryReader.BaseStream.Position < asset.offset + asset.length)
                        {
                            byte cmd = binaryReader.ReadByte();
                            
                            if (cmd == 0x00)
                                break;
                                
                            if ((cmd & 0x40) != 0)
                            {
                                // Draw command
                                int count = cmd & 0x3F;
                                binaryReader.ReadByte(); // Skip color
                                x += count;
                                maxX = Math.Max(maxX, x);
                            }
                            else if ((cmd & 0x80) != 0)
                            {
                                // Position command
                                x = cmd & 0x7F;
                                maxX = Math.Max(maxX, x);
                            }
                            else
                            {
                                // Line command
                                y = cmd;
                                x = 0;
                                maxY = Math.Max(maxY, y);
                            }
                        }
                        
                        // For format 0x0E (game options), use larger padding
                        int padding = formatId == 0x0E ? 16 : 8;
                        
                        // Set dimensions with padding
                        commandSprite.Width = maxX + padding;
                        commandSprite.Height = maxY + padding;
                        commandSprite.ImageData = new byte[commandSprite.Width * commandSprite.Height];
                        
                        // Second pass: draw the sprite
                        binaryReader.BaseStream.Position = startPos;
                        x = 0;
                        y = 0;
                        
                        while (binaryReader.BaseStream.Position < asset.offset + asset.length)
                        {
                            byte cmd = binaryReader.ReadByte();
                            
                            if (cmd == 0x00)
                                break;
                                
                            if ((cmd & 0x40) != 0)
                            {
                                // Draw command
                                int count = cmd & 0x3F;
                                byte color = binaryReader.ReadByte();
                                
                                for (int i = 0; i < count && x < commandSprite.Width; i++)
                                {
                                    int pos = y * commandSprite.Width + x;
                                    if (pos < commandSprite.ImageData.Length)
                                    {
                                        commandSprite.ImageData[pos] = color;
                                    }
                                    x++;
                                }
                            }
                            else if ((cmd & 0x80) != 0)
                            {
                                // Position command
                                x = cmd & 0x7F;
                            }
                            else
                            {
                                // Line command
                                y = cmd;
                                x = 0;
                            }
                        }
                        
                        return commandSprite;
                    }
                    else if (formatId == 0x02)
                    {
                        Console.WriteLine("Processing format 0x02 (multi-sprite format)");
                        
                        // Skip next byte (always 0)
                        byte skippedByte = binaryReader.ReadByte();
                        Console.WriteLine($"Skipped byte value: 0x{skippedByte:X2}");
                        
                        // Read number of sprites (2 bytes)
                        ushort numSprites = binaryReader.ReadUInt16();
                        Console.WriteLine($"Number of sprites: {numSprites} at position {binaryReader.BaseStream.Position}");
                        
                        // Skip next 2 bytes
                        ushort skippedValue = binaryReader.ReadUInt16();
                        Console.WriteLine($"Skipped 2 bytes value: 0x{skippedValue:X4}");
                        
                        Console.WriteLine($"Starting to read {numSprites} sprite offsets from position {binaryReader.BaseStream.Position}");
                        
                        // Read all sprite offsets first
                        var spriteOffsets = new uint[numSprites];
                        for (int i = 0; i < numSprites; i++)
                        {
                            spriteOffsets[i] = binaryReader.ReadUInt32();
                            Console.WriteLine($"Sprite {i} offset: 0x{spriteOffsets[i]:X8} (decimal: {spriteOffsets[i]}) at position {binaryReader.BaseStream.Position}");
                        }
                        
                        Console.WriteLine("Reading sprite dimensions:");
                        // Read dimensions for each sprite
                        var spriteDimensions = new (ushort width, ushort height)[numSprites];
                        int validSpriteCount = 0;
                        for (int i = 0; i < numSprites; i++)
                        {
                            // Seek to sprite data
                            long spriteStart = asset.offset + spriteOffsets[i];
                            Console.WriteLine($"Seeking to sprite {i} data at absolute position {spriteStart} (offset {spriteOffsets[i]})");
                            binaryReader.BaseStream.Seek(spriteStart, SeekOrigin.Begin);
                            
                            // Read width and height
                            ushort width = binaryReader.ReadUInt16();
                            ushort height = binaryReader.ReadUInt16();
                            Console.WriteLine($"Sprite {i} raw dimensions: 0x{width:X4}x0x{height:X4} at position {binaryReader.BaseStream.Position}");
                            
                            // Dump raw bytes to help debug
                            binaryReader.BaseStream.Position -= 4;
                            byte[] dimensionBytes = new byte[4];
                            binaryReader.Read(dimensionBytes, 0, 4);
                            Console.WriteLine($"Raw dimension bytes: {BitConverter.ToString(dimensionBytes)}");
                            
                            // Read width and height as individual bytes
                            width = (ushort)((dimensionBytes[0] << 8) | dimensionBytes[1]);
                            height = (ushort)((dimensionBytes[2] << 8) | dimensionBytes[3]);
                            Console.WriteLine($"Sprite {i} dimensions from bytes: {width}x{height}");
                            
                            // Skip sprites with 0xFFFF dimensions (terminator markers)
                            if (width == 0xFFFF && height == 0xFFFF)
                            {
                                Console.WriteLine($"Skipping sprite {i} as it appears to be a terminator marker");
                                continue;
                            }
                            
                            // Validate individual sprite dimensions
                            if (width <= 0 || width > 1024)
                            {
                                throw new Exception($"Invalid sprite width: {width} (max 1024)");
                            }
                            if (height <= 0 || height > 1024)
                            {
                                throw new Exception($"Invalid sprite height: {height} (max 1024)");
                            }
                            
                            spriteDimensions[validSpriteCount] = (width, height);
                            validSpriteCount++;
                            
                            // Dump next few bytes to help debug
                            long currentPos = binaryReader.BaseStream.Position;
                            byte[] nextBytes = new byte[16];
                            binaryReader.Read(nextBytes, 0, 16);
                            Console.WriteLine($"Next 16 bytes after dimensions: {BitConverter.ToString(nextBytes)}");
                            binaryReader.BaseStream.Position = currentPos;
                        }
                        
                        // Adjust sprite count to only include valid sprites
                        numSprites = (ushort)validSpriteCount;
                        Array.Resize(ref spriteDimensions, numSprites);
                        
                        // Calculate total dimensions needed
                        int totalWidth = 0;
                        int totalHeight = 0;
                        for (int i = 0; i < numSprites; i++)
                        {
                            var (width, height) = spriteDimensions[i];
                            totalWidth = Math.Max(totalWidth, width);
                            totalHeight += height;
                        }
                        Console.WriteLine($"Total dimensions needed: {totalWidth}x{totalHeight}");
                        
                        // Validate total dimensions
                        if (totalWidth <= 0 || totalWidth > 1024)
                        {
                            throw new Exception($"Invalid total width: {totalWidth} (max 1024)");
                        }
                        
                        // Create combined image data
                        byte[] combinedData = new byte[totalWidth * totalHeight];
                        int currentY = 0;
                        
                        // Read and decompress each sprite
                        for (int i = 0; i < numSprites; i++)
                        {
                            var (width, height) = spriteDimensions[i];
                            
                            // Seek to sprite data
                            long spriteStart = asset.offset + spriteOffsets[i];
                            binaryReader.BaseStream.Seek(spriteStart + 4, SeekOrigin.Begin);
                            
                            // Read compressed data for this sprite
                            long nextSpriteOffset = (i < numSprites - 1) ? spriteOffsets[i + 1] : asset.length;
                            int compressedLength = (int)(nextSpriteOffset - spriteOffsets[i] - 4);
                            byte[] compressedData = binaryReader.ReadBytes(compressedLength);
                            
                            // Decompress the data
                            byte[] rawData = DecompressRLE(compressedData);
                            Console.WriteLine($"Sprite {i} decompressed to {rawData.Length} bytes");
                            
                            // Verify decompressed size matches dimensions
                            int expectedSize = width * height;
                            if (rawData.Length != expectedSize)
                            {
                                Console.WriteLine($"Warning: Decompressed size {rawData.Length} does not match expected size {expectedSize}");
                                if (rawData.Length > expectedSize)
                                {
                                    Array.Resize(ref rawData, expectedSize);
                                    Console.WriteLine($"Truncated data to {expectedSize} bytes");
                                }
                                else
                                {
                                    Array.Resize(ref rawData, expectedSize);
                                    Console.WriteLine($"Padded data to {expectedSize} bytes");
                                }
                            }
                            
                            // Copy sprite data into combined image
                            for (int y = 0; y < height; y++)
                            {
                                Array.Copy(rawData, y * width, 
                                         combinedData, (currentY + y) * totalWidth, 
                                         width);
                            }
                            currentY += height;
                        }

                        Console.WriteLine($"Final dimensions: {totalWidth}x{totalHeight}");

                        commandSprite.Width = totalWidth;
                        commandSprite.Height = totalHeight;
                        commandSprite.ImageData = combinedData;
                    }
                    else
                    {
                        // Uncompressed format:
                        // - 1 byte: format ID
                        // - 2 bytes: width (uint16)
                        // - 2 bytes: height (uint16)
                        // - Remaining: raw pixel data
                        ushort width = binaryReader.ReadUInt16();
                        ushort height = binaryReader.ReadUInt16();

                        Console.WriteLine($"Sprite dimensions: {width}x{height}");

                        // Validate dimensions
                        if (width <= 0 || width > 800)
                        {
                            throw new Exception($"Invalid sprite width: {width} (max 800)");
                        }
                        if (height <= 0 || height > 600)
                        {
                            throw new Exception($"Invalid sprite height: {height} (max 600)");
                        }

                        commandSprite.Width = width;
                        commandSprite.Height = height;

                        // Read uncompressed data
                        int expectedSize = width * height;
                        Console.WriteLine($"Expected size: {expectedSize} bytes");

                        byte[] rawData = binaryReader.ReadBytes(expectedSize);
                        
                        // Verify we read enough data
                        if (rawData.Length < expectedSize)
                        {
                            Console.WriteLine($"Warning: Expected {expectedSize} bytes but only read {rawData.Length}");
                            // Pad with zeros if needed
                            Array.Resize(ref rawData, expectedSize);
                        }

                        commandSprite.ImageData = rawData;
                    }

                    // Debug output
                    Console.WriteLine($"Final image data size: {commandSprite.ImageData.Length} bytes");
                    if (commandSprite.ImageData.Length > 0)
                    {
                        Console.WriteLine("First 16 bytes of image data: " + 
                            BitConverter.ToString(commandSprite.ImageData.Take(16).ToArray()));
                    }

                    return commandSprite;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading PAK file {asset.name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        private byte[] DecompressRLE(byte[] data)
        {
            var output = new List<byte>();
            int i = 0;
            
            try
            {
                while (i < data.Length - 1) // Need at least 2 bytes to compare
                {
                    if (data[i] == data[i + 1])
                    {
                        // RLE block - repeated bytes
                        byte value = data[i];
                        byte count = 1;
                        i++;
                        
                        while (i < data.Length - 1 && count < 254)
                        {
                            if (data[i] != value)
                                break;
                            count++;
                            i++;
                        }
                        
                        // Output count and value
                        output.Add(count);
                        output.Add(value);
                    }
                    else
                    {
                        // Literal block - non-repeated bytes
                        output.Add(0xFF); // Marker for literal block
                        int countPos = output.Count;
                        output.Add(0); // Placeholder for count
                        byte count = 0;
                        
                        while (i < data.Length - 1 && count < 254)
                        {
                            if (data[i] == data[i + 1])
                                break;
                            output.Add(data[i]);
                            count++;
                            i++;
                        }
                        
                        // Update count
                        output[countPos] = count;
                    }
                }
                
                // Handle last byte if needed
                if (i < data.Length)
                {
                    output.Add(1); // Count of 1
                    output.Add(data[i]); // Last byte
                }
                
                // Add terminator
                output.Add(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during decompression at offset {i}: {ex.Message}");
            }
            
            return output.ToArray();
        }

        private byte[] DecompressWallpaperRLE(byte[] data)
        {
            var output = new List<byte>();
            int i = 0;
            
            try
            {
                while (i < data.Length)
                {
                    // Check for RLE marker (0x00)
                    if (data[i] == 0x00 && i + 2 < data.Length)
                    {
                        // Next byte is count, followed by value
                        byte count = data[i + 1];
                        byte value = data[i + 2];
                        
                        // Output repeated value
                        for (int j = 0; j < count; j++)
                        {
                            output.Add(value);
                        }
                        
                        i += 3;
                    }
                    else
                    {
                        // Literal byte
                        output.Add(data[i]);
                        i++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during wallpaper decompression at offset {i}: {ex.Message}");
            }
            
            // Ensure we have enough data for 640x480
            int expectedSize = 640 * 480;
            if (output.Count < expectedSize)
            {
                Console.WriteLine($"Warning: Decompressed size {output.Count} is less than expected {expectedSize}, padding with zeros");
                while (output.Count < expectedSize)
                {
                    output.Add(0);
                }
            }
            else if (output.Count > expectedSize)
            {
                Console.WriteLine($"Warning: Decompressed size {output.Count} is more than expected {expectedSize}, truncating");
                return output.Take(expectedSize).ToArray();
            }
            
            return output.ToArray();
        }

        public void SaveSprite(SpriteData sprite, string name)
        {
            try
            {
                using (var bitmap = new Bitmap(sprite.Width, sprite.Height, PixelFormat.Format32bppArgb))
                {
                    var rect = new Rectangle(0, 0, sprite.Width, sprite.Height);
                    var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    
                    int stride = bitmapData.Stride;
                    byte[] imageData = new byte[stride * sprite.Height];
                    
                    // Convert indexed color data to RGBA
                    for (int y = 0; y < sprite.Height; y++)
                    {
                        for (int x = 0; x < sprite.Width; x++)
                        {
                            int sourceIndex = y * sprite.Width + x;
                            int destIndex = y * stride + x * 4;
                            
                            byte colorIndex = sprite.ImageData[sourceIndex];
                            Color color = palette[colorIndex];
                            
                            // For lava and similar effects, preserve the original color
                            float alpha = colorIndex == 0 ? 0f : 1f;
                            
                            // Don't premultiply alpha for special effects
                            imageData[destIndex + 0] = color.B;
                            imageData[destIndex + 1] = color.G;
                            imageData[destIndex + 2] = color.R;
                            imageData[destIndex + 3] = (byte)(alpha * 255);
                        }
                    }
                    
                    Marshal.Copy(imageData, 0, bitmapData.Scan0, imageData.Length);
                    bitmap.UnlockBits(bitmapData);
                    
                    string filename = Path.Combine(outputDir, $"{name}.png");
                    bitmap.Save(filename, ImageFormat.Png);
                    Console.WriteLine($"Saved sprite: {filename}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving sprite {name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
} 