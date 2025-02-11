using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;


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
            for (int i = 0; i < 256; i++)
            {
                int r = ((i >> 5) & 7) * 255 / 7;
                int g = ((i >> 2) & 7) * 255 / 7;
                int b = (i & 3) * 255 / 3;
                palette[i] = Color.FromArgb(255, r, g, b);
            }
        }

        public void LoadPaletteFromPrs(string prsPath, int offset, int length)
        {
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(prsPath)))
                {
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                    
                    // Skip CLU header (4 bytes)
                    uint magic = reader.ReadUInt32();
                    Console.WriteLine($"CLU Magic: 0x{magic:X8}");
                    
                    // Skip additional header bytes if present
                    if (magic == 0x554C43) // "CLU" magic
                    {
                        reader.ReadInt32(); // Skip size
                    }
                    
                    // Read palette data - 256 colors, each 4 bytes (RGBA)
                    for (int i = 0; i < 256; i++)
                    {
                        // Read as bytes to avoid endianness issues
                        int colorValue = reader.ReadInt32();
                        byte r = (byte)((colorValue >> 16) & 0xFF);
                        byte g = (byte)((colorValue >> 8) & 0xFF);
                        byte b = (byte)(colorValue & 0xFF);
                        
                        // Store in palette
                        palette[i] = Color.FromArgb(255, r, g, b);
                        
                        Console.WriteLine($"Color {i}: R={r} G={g} B={b}");
                    }
                    hasPalette = true;
                    Console.WriteLine("Loaded palette from CLU file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading palette: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                InitializeDefaultPalette();
            }
        }

        public SpriteData LoadPak(Asset asset)
        {
            // First check if we need to load a palette from the PRS file
            if (!hasPalette)
            {
                // Look for CLU file in the same PRS
                using (var reader = new BinaryReader(File.OpenRead(asset.prsPath)))
                {
                    reader.BaseStream.Seek(0, SeekOrigin.Begin);
                    byte[] searchBuffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = reader.Read(searchBuffer, 0, searchBuffer.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead - 4; i++)
                        {
                            if (searchBuffer[i] == 'C' && searchBuffer[i + 1] == 'L' && 
                                searchBuffer[i + 2] == 'U' && searchBuffer[i + 3] == 0)
                            {
                                LoadPaletteFromPrs(asset.prsPath, i, 768); // 256 colors * 3 bytes
                                break;
                            }
                        }
                        if (hasPalette) break;
                    }
                }
            }

            using (var binaryReader = new BinaryReader(File.Open(asset.prsPath, FileMode.Open)))
            {
                binaryReader.BaseStream.Seek(asset.offset, SeekOrigin.Begin);
                
                var spriteData = new SpriteData();
                
                // Read PAK header - first 4 bytes are size
                uint pakSize = binaryReader.ReadUInt32();
                Console.WriteLine($"PAK size: {pakSize}");

                // Next 4 bytes are dimensions
                ushort width = binaryReader.ReadUInt16();
                ushort height = binaryReader.ReadUInt16();
                spriteData.Width = width;
                spriteData.Height = height;
                
                Console.WriteLine($"PAK dimensions: {width}x{height}");

                // Read image data - it's stored as 8-bit indices
                int dataSize = width * height;
                spriteData.ImageData = binaryReader.ReadBytes(dataSize);

                // Read frame data if present
                long remainingBytes = asset.length - (binaryReader.BaseStream.Position - asset.offset);
                if (remainingBytes > 0)
                {
                    // Frame count is 16-bit
                    ushort frameCount = binaryReader.ReadUInt16();
                    Console.WriteLine($"Frame count: {frameCount}");

                    if (frameCount > 0 && frameCount < 1000) // Sanity check
                    {
                        spriteData.Frames = new Rectangle[frameCount];
                        for (int i = 0; i < frameCount; i++)
                        {
                            // Each frame is 4 16-bit values: x, y, width, height
                            spriteData.Frames[i] = new Rectangle(
                                binaryReader.ReadUInt16(),  // X
                                binaryReader.ReadUInt16(),  // Y
                                binaryReader.ReadUInt16(),  // Width
                                binaryReader.ReadUInt16()   // Height
                            );
                            Console.WriteLine($"Frame {i}: {spriteData.Frames[i]}");
                        }
                    }
                }

                // Try to save the sprite
                if (spriteData.ImageData != null && spriteData.ImageData.Length > 0)
                {
                    SaveSprite(asset.name, spriteData);
                }
                
                return spriteData;
            }
        }

        private void SaveSprite(string name, SpriteData sprite)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                
                // Create 32-bit RGBA bitmap instead of indexed
                using (var bitmap = new Bitmap(sprite.Width, sprite.Height, PixelFormat.Format32bppArgb))
                {
                    // Lock bits for fast pixel access
                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, sprite.Width, sprite.Height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    // Copy pixel data with color lookup
                    int stride = bitmapData.Stride;
                    byte[] imageData = new byte[stride * sprite.Height];
                    
                    for (int y = 0; y < sprite.Height; y++)
                    {
                        for (int x = 0; x < sprite.Width; x++)
                        {
                            int sourceIndex = y * sprite.Width + x;
                            int destIndex = y * stride + x * 4; // 4 bytes per pixel (RGBA)
                            
                            if (sourceIndex < sprite.ImageData.Length)
                            {
                                byte colorIndex = sprite.ImageData[sourceIndex];
                                Color color = palette[colorIndex];
                                
                                // Write RGBA values
                                imageData[destIndex] = color.B;     // Blue
                                imageData[destIndex + 1] = color.G; // Green
                                imageData[destIndex + 2] = color.R; // Red
                                imageData[destIndex + 3] = color.A; // Alpha
                            }
                        }
                    }
                    
                    Marshal.Copy(imageData, 0, bitmapData.Scan0, imageData.Length);
                    bitmap.UnlockBits(bitmapData);

                    // Save as PNG
                    string filename = Path.Combine(outputDir, $"{name}_sheet.png");
                    bitmap.Save(filename, ImageFormat.Png);
                    Console.WriteLine($"Saved sprite sheet: {filename}");

                    // Handle frames
                    if (sprite.Frames != null && sprite.Frames.Length > 0 && 
                        sprite.Frames[0].Width > 0 && sprite.Frames[0].Height > 0)
                    {
                        string framesDir = Path.Combine(outputDir, name);
                        Directory.CreateDirectory(framesDir);
                        
                        for (int i = 0; i < sprite.Frames.Length; i++)
                        {
                            var frame = sprite.Frames[i];
                            using (var frameBitmap = new Bitmap(frame.Width, frame.Height))
                            {
                                using (var g = Graphics.FromImage(frameBitmap))
                                {
                                    g.DrawImage(bitmap, 
                                        new Rectangle(0, 0, frame.Width, frame.Height),
                                        frame,
                                        GraphicsUnit.Pixel);
                                }
                                string frameFile = Path.Combine(framesDir, $"frame_{i}.png");
                                frameBitmap.Save(frameFile, ImageFormat.Png);
                                Console.WriteLine($"Saved frame {i}: {frameFile}");
                            }
                        }
                    }
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