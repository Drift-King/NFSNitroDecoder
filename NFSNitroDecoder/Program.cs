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
                                throw new Exception("Conversion algorithm is not working properly.");
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
                    String afterPart = i == 50 ? " (actually a sound effect)" : "";
                    List<float>[] part = ConvertTrack(file, pos, $"{dirName}/{songName} - Part {i}{afterPart}.wav", out pos);
                    if (i != 50)
                    {
                        fullSongData[0].AddRange(part[0]);
                        fullSongData[1].AddRange(part[1]);
                    }

                    //Align pos to determine location of next track
                    pos = (uint)Math.Ceiling(pos / 128f) * 128;
                    i++;
                }
                Console.WriteLine("Joining parts to form full track...");
                SaveChannelsToWAV(fullSongData, $"{dirName}/{songName} - Full track (All parts except 50 joined together).wav");
            }
        }

        private static List<float>[] ConvertTrack(byte[] file, uint trackStartLocation, String filename, out uint nextTrackStartLocation)
        {
            uint firstval1 = GetUintFromFile(file, trackStartLocation);
            uint firstval2 = GetUintFromFile(file, trackStartLocation + 4);

            if ((firstval1 & 0x80000000) == 0x80000000)
            {
                Console.WriteLine($"(Skipped single-chunk track @ {trackStartLocation:x})");
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
                uint amntToNextChunk = GetUintFromFile(file, curPos);
                uint outputLength = GetUintFromFile(file, curPos + 4);

                bool lastBlock = false;
                if ((amntToNextChunk & 0x80000000) != 0)
                {
                    amntToNextChunk = amntToNextChunk ^ 0x80000000;

                    lastBlock = true;
                }

                int curLength = 0;
                uint curBlockPos = curPos + 8;
                while (curLength < outputLength)
                {
                    foreach (List<float> channel in channels)
                    {
                        float[] data = Decode(file, curBlockPos);
                        if (curLength + 128 > outputLength)
                        {
                            data = data.Take((int)outputLength - curLength).ToArray();
                        }
                        channel.AddRange(data);
                        curBlockPos += 76;

                        if (curBlockPos > curPos + amntToNextChunk)
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

                curPos += amntToNextChunk;
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



        static float[] predictorPairTable = new uint[]
                                { 0x00000000, 0x00000000, 0x3F700000, 0x00000000,
                                  0x3FE60000, 0xBF500000, 0x3FC40000, 0xBF5C0000 }.Select(u => BitConverter.ToSingle(BitConverter.GetBytes(u), 0)).ToArray();

        static float[] scaleTable = new uint[]
                                { 0x30000000, 0x2F800000, 0x2F000000, 0x2E800000,
                                  0x2E000000, 0x2D800000, 0x2D000000, 0x2C800000,
                                  0x2C000000, 0x2B800000, 0x2B000000, 0x2A800000,
                                  0x2A000000 }.Select(u => BitConverter.ToSingle(BitConverter.GetBytes(u), 0)).ToArray();

        static double f2 = BitConverter.Int64BitsToDouble(0x3f00000000000000);

        private static float[] Decode(byte[] file, uint filePos)
        {
            float[] generatedData = new float[128];

            float[] prevFloatPredictorPerSection = new float[4];
            float[] prevPrevFloatPredictorPerSection = new float[4];
            float[] scalePerSection = new float[4];

            for (int i = 0; i < 4; i++)
            {
                int curSectionOffset = i * 32;

                uint byte1 = file[filePos];
                uint tableSelection = (byte1 & 0x0F) * 2;
                prevFloatPredictorPerSection[i] = predictorPairTable[tableSelection];
                prevPrevFloatPredictorPerSection[i] = predictorPairTable[tableSelection + 1];

                int byte2SignExtend = (sbyte)file[filePos + 1];

                //r9 is a number in the range 32752 to -32768 (0x00007ff0 to 0xffff80f0)
                //and it's last nibble is always 0 (i.e. it is always a multiple of 16).
                //f2 is 1/32768, so multiplying it by f2 scales it to the (1, -1) range
                //(same goes for the second copy of this line)

                int r9 = (byte2SignExtend << 8) | (int)(byte1 & 0xF0);
                double otherf1 = f2 * r9;
                generatedData[curSectionOffset] = (float)otherf1;


                uint byte3 = file[filePos + 2];
                scalePerSection[i] = scaleTable[byte3 & 0x0F];

                int byte4SignExtend = (sbyte)file[filePos + 3];
                r9 = (byte4SignExtend << 8) | (int)(byte3 & 0xF0);
                double otherf0 = f2 * r9;
                generatedData[curSectionOffset + 1] = (float)otherf0;

                filePos += 4;
            }

            for(int i = 0; i < 15; i++)
            {
                for (int b = 0; b < 4; b++)
                {
                    byte by = file[filePos];

                    //These nibbles is mapped linearly from 1879048192 to -2147483648 (in steps of 268435456).
                    //0x0-0x7 maps to the positive numbers, and 0x8-0xF maps to the negative numbers

                    uint upperNibble = (by & (uint)0xF0) >> 4;
                    float float1baseValue = (int)(upperNibble * 268435456);

                    uint lowerNibble = (by & (uint)0x0F);
                    float float2baseValue = (int)(lowerNibble * 268435456);

                    filePos += 1;

                    int section = b;

                    int writeLoc = (section * 32) + ((i+1) * 2);
                    float prevPrevFloat = generatedData[writeLoc - 2];
                    float prevFloat = generatedData[writeLoc - 1];

                    //Need to do it in this order for accuracy
                    float float1 = (float1baseValue * scalePerSection[section]);
                    float1 = (prevFloatPredictorPerSection[section] * prevFloat) + float1;
                    float1 = (prevPrevFloatPredictorPerSection[section] * prevPrevFloat) + float1;

                    float float2 = (float2baseValue * scalePerSection[section]);
                    float2 = (prevFloatPredictorPerSection[section] * float1) + float2;
                    float2 = (prevPrevFloatPredictorPerSection[section] * prevFloat) + float2;

                    generatedData[writeLoc] = float1;
                    generatedData[writeLoc + 1] = float2;
                }
            }
            return generatedData;
        }
    }
}
