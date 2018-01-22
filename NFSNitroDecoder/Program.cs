using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NFSNitroDecoder
{
    static class Program
    {
        static void Main(string[] args)
        {
            String mode = args[0];
            String path = String.Join(" ", args.Skip(1));
            switch (mode)
            {
                case "--ast":
                    Console.WriteLine("Extracting AST file...");
                    ExtractASTFile(path);
                    break;
                case "--pfdata":
                    Console.WriteLine("Extracting all PFData files...");
                    ConvertPFDataFiles(path);
                    break;
                case "--all-songs": //path should be to sound folder
                    Console.WriteLine("Extracting FE_COMMON_STR.ast...");
                    ExtractASTFile(Path.Combine(path, @"Global\FE_COMMON_STR.ast"));
                    Console.WriteLine("Extracting PFData files...");
                    ConvertPFDataFiles(Path.Combine(path, @"PFData\\"));
                    break;

                case "--verify-algorithm":
                    Console.WriteLine("Verifying...");
                    VerifyConversionAlgorithm();
                    Console.WriteLine("Verification complete.");
                    break;
                default:
                    throw new InvalidOperationException("Invalid mode!");
            }
        }

        private static void VerifyConversionAlgorithm()
        {
            Console.CursorLeft = 30 + 2;
            Console.Write("]");
            Console.CursorLeft = 0;
            Console.Write("[");

            int prevProgress = 0;
            using (BinaryReader f = new BinaryReader(File.OpenRead("testdata.raw"), Encoding.ASCII))
            {
                while (f.PeekChar() != -1)
                {
                    byte[] input = f.ReadBytes(76);
                    byte[] expectedOutput = f.ReadBytes(512);
                    float[] actualOutput = Decode(input, 0);

                    for (int i = 0; i < 128; i++)
                    {
                        //Reverse because of endianness
                        byte[] actualFloatAsBytes = BitConverter.GetBytes(actualOutput[i]).Reverse().ToArray();
                        byte[] expectedFloatAsBytes = expectedOutput.Skip(i * 4).Take(4).ToArray();
                        for (int b = 0; b < 4; b++)
                        {
                            if (actualFloatAsBytes[b] != expectedFloatAsBytes[b])
                            {
                                throw new Exception("Conversion algorithm is incorrect.");
                            }
                        }
                    }

                    if(Math.Floor((f.BaseStream.Position / (float)f.BaseStream.Length) * 30) > prevProgress)
                    {
                        Console.Write("=");
                        prevProgress++;
                    }
                }
            }
            Console.WriteLine("=");
        }

        private static void ExtractASTFile(String astFilePath)
        {
            byte[] tfile = File.ReadAllBytes(astFilePath);
            String filename = Path.GetFileName(astFilePath);

            //First track always starts at 0x40
            uint pos = 0x40;

            Directory.CreateDirectory(filename);
            while (pos > 0 && pos < tfile.Length)
            {
                ConvertTrack(tfile, pos, Path.Combine(filename, $"Track starting at {pos}.wav"), out pos);
            }
        }

        private static void ConvertPFDataFiles(String PFDataFolderPath)
        {
            foreach (String dir in Directory.EnumerateDirectories(PFDataFolderPath))
            {
                byte[] file = File.ReadAllBytes(Path.Combine(dir, "Track.mus"));
                String songName = Path.GetFileName(dir);
                String dirName = $"PFData {songName}";
                Console.WriteLine($"Converting {songName}...");

                Directory.CreateDirectory(dirName);

                List<float>[] fullSongData = { new List<float>(), new List<float>() };

                int i = 1;
                uint pos = 0x680;
                while (pos + 4 < file.Length)
                {
                    List<float>[] part = ConvertTrack(file, pos, $"{dirName}/{songName} part {i}.wav", out pos);
                    if (i != 50)
                    {
                        fullSongData[0].AddRange(part[0]);
                        fullSongData[1].AddRange(part[1]);
                    }

                    //Align pos to determine location of next track
                    pos = (uint)Math.Ceiling(pos / 128f) * 128;
                    i++;
                }

                SaveChannelsToWAV(fullSongData, $"{dirName}/{songName} Full track (All parts except 50 joined together).wav");
            }
        }

        private static List<float>[] ConvertTrack(byte[] file, uint trackStartLocation, String filename, out uint nextTrackStartLocation)
        {
            uint firstval1 = GetUintFromFile(file, trackStartLocation);
            uint firstval2 = GetUintFromFile(file, trackStartLocation + 4);

            if ((firstval1 & 0x80000000) == 0x80000000)
            {
                Console.WriteLine($"(Skipped single-block track @ {trackStartLocation:x})");
                nextTrackStartLocation = trackStartLocation + (firstval1 ^ 0x80000000);
                return null;
            }

            float ccFloat = (((firstval1 - 8f) / 76f) * 128f) / firstval2;
            if (ccFloat - Math.Floor(ccFloat) != 0)
            {
                throw new Exception();
            }
            int channelCount = (int)ccFloat;

            List<float>[] channels = new List<float>[channelCount];
            for (int i = 0; i < channelCount; i++)
                channels[i] = new List<float>();

            uint curPos = trackStartLocation;
            while (true)
            {
                uint amntToNextBlock = GetUintFromFile(file, curPos);
                uint outputLength = GetUintFromFile(file, curPos + 4);

                bool lastBlock = false;
                if ((amntToNextBlock & 0x80000000) != 0)
                {
                    amntToNextBlock = amntToNextBlock ^ 0x80000000;

                    lastBlock = true;
                }

                int curLength = 0;
                uint curLinePos = curPos + 8;
                while (curLength < outputLength)
                {
                    foreach (List<float> channel in channels)
                    {
                        float[] data = Decode(file, curLinePos);
                        if (curLength + 128 > outputLength)
                        {
                            data = data.Take((int)outputLength - curLength).ToArray();
                        }
                        channel.AddRange(data);
                        curLinePos += 76;

                        if (curLinePos > curPos + amntToNextBlock)
                        {
                            throw new Exception();
                        }
                    }
                    curLength += 128;
                }

                if (channels.Any(c => c.Count != channels[0].Count))
                {
                    throw new Exception();
                }

                curPos += amntToNextBlock;
                if (lastBlock)
                {
                    break;
                }
            }

            if (channels.Any(c => c.Count != channels[0].Count))
            {
                throw new Exception();
            }

            SaveChannelsToWAV(channels, filename);

            Console.WriteLine($"Converted {channelCount}-channel {channels[0].Count / 32000f:f2}-second-long track");
            nextTrackStartLocation = curPos;
            return channels;
        }

        private static void SaveChannelsToWAV(List<float>[] channels, String filename)
        {
            WaveFormat waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(32000, channels.Length);
            using (WaveFileWriter writer = new WaveFileWriter(filename, waveFormat))
            {
                for (int i = 0; i < channels[0].Count; i++)
                {
                    //Left channel comes before right (same as in a WAV, so no reordering needed)
                    foreach (List<float> channel in channels)
                    {
                        writer.WriteSample(channel[i]);
                    }
                }
            }
        }

        private static uint GetUintFromFile(byte[] file, uint location)
        {
            return (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(file, (int)location));
        }

        private static float[] Decode(byte[] file, uint filePos)
        {
            float[] prevInfluencePairTable = new uint[]
                                { 0x00000000, 0x00000000, 0x3F700000, 0x00000000,
                                  0x3FE60000, 0xBF500000, 0x3FC40000, 0xBF5C0000 }.Select(u => BitConverter.ToSingle(BitConverter.GetBytes(u), 0)).ToArray();
            float[] valueMultiplierTable = new uint[]
                                { 0x30000000, 0x2F800000, 0x2F000000, 0x2E800000,
                                  0x2E000000, 0x2D800000, 0x2D000000, 0x2C800000,
                                  0x2C000000, 0x2B800000, 0x2B000000, 0x2A800000,
                                  0x2A000000 }.Select(u => BitConverter.ToSingle(BitConverter.GetBytes(u), 0)).ToArray();

            double f2 = BitConverter.Int64BitsToDouble(0x3f00000000000000);
            double f3 = BitConverter.Int64BitsToDouble(0x4330000080000000);

            float[] generatedData = new float[128];

            //float[] array1 = new float[8];
            float[] prevFloatInfluencePerBlock = new float[4];
            float[] prevPrevFloatInfluencePerBlock = new float[4];
            float[] valueMultiplierPerBlock = new float[4];

            int curChunkOffset = 0;
            for (int i = 0; i < 4; i++)
            {
                uint byte1 = file[filePos];
                uint tableSelection = (byte1 & 0x0F) * 2;
                prevFloatInfluencePerBlock[i] = prevInfluencePairTable[tableSelection];
                prevPrevFloatInfluencePerBlock[i] = prevInfluencePairTable[tableSelection + 1];

                int byte2SignExtend = (sbyte)file[filePos + 1];
                int r9 = (byte2SignExtend << 8) | (int)(byte1 & 0xF0);
                double otherf1 = f2 * r9;
                generatedData[curChunkOffset] = (float)otherf1;
                //array1[i] = (float)f1;


                uint byte3 = file[filePos + 2];
                valueMultiplierPerBlock[i] = valueMultiplierTable[byte3 & 0x0F];

                int byte4SignExtend = (sbyte)file[filePos + 3];
                r9 = (byte4SignExtend << 8) | (int)(byte3 & 0xF0);
                double otherf0 = f2 * r9;
                generatedData[curChunkOffset + 1] = (float)otherf0;
                //array1[i + 4] = (float)f0;

                curChunkOffset += 32;
                filePos += 4;
            }

            for(int i = 0; i < 15; i++)
            {
                float[] float1baseValuePerBlock = new float[4];
                float[] float2baseValuePerBlock = new float[4];

                for (int b = 0; b < 4; b++)
                {
                    byte by = file[filePos];

                    //268435456 = -Int32.Min / 8

                    uint upperNibble = (by & (uint)0xF0) >> 4;
                    float1baseValuePerBlock[b] = (int)(upperNibble * 268435456);

                    uint lowerNibble = (by & (uint)0x0F);
                    float2baseValuePerBlock[b] = (int)(lowerNibble * 268435456);

                    filePos += 1;
                }

                for(int block = 0; block < 4; block++)
                {
                    int writeLoc = (block * 32) + ((i+1) * 2);
                    float prevPrevFloat = generatedData[writeLoc - 2];
                    float prevFloat = generatedData[writeLoc - 1];

                    //Need to do it in this order for accuracy
                    float a = (float1baseValuePerBlock[block] * valueMultiplierPerBlock[block]);
                    float b = (prevFloatInfluencePerBlock[block] * prevFloat) + a;
                    float float1 = (prevPrevFloatInfluencePerBlock[block] * prevPrevFloat) + b;

                    float c = (float2baseValuePerBlock[block] * valueMultiplierPerBlock[block]);
                    float d = (prevFloatInfluencePerBlock[block] * float1) + c;
                    float float2 = (prevPrevFloatInfluencePerBlock[block] * prevFloat) + d;

                    generatedData[writeLoc] = float1;
                    generatedData[writeLoc + 1] = float2;
                }
            }
            return generatedData;
        }
    }
}
