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
            byte[] tfile = File.ReadAllBytes(@"C:\Users\rolan\Desktop\Need For Speed Nitro\Extracted Partition 1\Sound\Global\FE_COMMON_STR.ast");
            uint pos = 0x40;//0x0000000002407b80;

            FileStream currentFile = File.Create($"outfile {pos:x}.raw");

            while (pos > 0 && pos < tfile.Length)
            {
                pos = ConvertTrack(tfile, pos);
            }
            Console.ReadKey();
        }

        private static uint ConvertTrack(byte[] file, uint trackStartLocation)
        {
            uint firstval1 = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(file, (int)trackStartLocation));
            uint firstval2 = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(file, (int)trackStartLocation + 4));

            if ((firstval1 & 0x80000000) == 0x80000000)
            {
                Console.WriteLine($"Skipped single-block track @ {trackStartLocation:x}");
                return trackStartLocation + (firstval1 ^ 0x80000000);
            }

            float ccFloat = (((firstval1 - 8f) / 76f) * 128f) / firstval2;
            if(ccFloat - Math.Floor(ccFloat) != 0)
            {
                throw new Exception();
            }
            int channelCount = (int)ccFloat;

            List<float>[] channels = new List<float>[channelCount];
            for (int i = 0; i < channelCount; i++)
                channels[i] = new List<float>();

            uint curPos = trackStartLocation;
            while(true)
            {
                uint amntToNextBlock = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(file, (int)curPos));
                uint outputLength = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(file, (int)curPos + 4));

                bool lastBlock = false;
                if ((amntToNextBlock & 0x80000000) != 0)
                {
                    amntToNextBlock = amntToNextBlock ^ 0x80000000;

                    lastBlock = true;
                }

                int curLength = 0;
                uint curLinePos = curPos + 8;
                while(curLength < outputLength)
                {
                    foreach(List<float> channel in channels)
                    {
                        float[] data = Decode(file, curLinePos);
                        if (curLength + 128 > outputLength)
                        {
                            data = data.Take((int)outputLength - curLength).ToArray();
                        }
                        channel.AddRange(data);
                        curLinePos += 76;

                        if(curLinePos > curPos + amntToNextBlock)
                        {
                            throw new Exception();
                        }
                    }
                    curLength += 128;
                }

                //for (uint i = 8; i < amntToNextBlock; i += 76)
                //{
                //    if(i + 76 > amntToNextBlock)
                //    {
                //        Console.WriteLine($"Cutoff @ {curPos:x}");
                //        break;
                //    }

                //    float[] data = Decode(file, curPos + i);

                //    channels[curChannel].AddRange(data);
                //    curChannel = (curChannel + 1) % channelCount;
                //}

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

            if(channels.Any(c => c.Count != channels[0].Count))
            {
                throw new Exception();
            }

            WaveFormat waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(32000, channelCount);
            using (WaveFileWriter writer = new WaveFileWriter($"extracted at {trackStartLocation}.wav", waveFormat))
            {
                for(int i = 0; i < channels[0].Count; i++)
                {
                    foreach(List<float> channel in channels)
                    {
                        writer.WriteSample(channel[i]);
                    }
                }
            }

            Console.WriteLine($"Converted {channelCount}-channel {channels[0].Count / 32000}-second-long track");
            return curPos;
        }

        private static float[] Decode(byte[] file, uint filePos)
        {
#if DEBUG
            uint origFilePos = filePos;
#endif
            float[] t80544810 = new uint[]
                                { 0x00000000, 0x00000000, 0x3F700000, 0x00000000,
                                  0x3FE60000, 0xBF500000, 0x3FC40000, 0xBF5C0000,
                                  0x30000000, 0x2F800000, 0x2F000000, 0x2E800000,
                                  0x2E000000, 0x2D800000, 0x2D000000, 0x2C800000,
                                  0x2C000000, 0x2B800000, 0x2B000000, 0x2A800000,
                                  0x2A000000, 0x00000000, 0x00000000, 0x00000000,
                                  0x506C6163, 0x65486F6C, 0x64657200, 0x00000000,
                                  0x00000000, 0x00000000, 0x00000000, 0x00000000 }.Select(u => BitConverter.ToSingle(BitConverter.GetBytes(u), 0)).ToArray();

            float[] TABLE_R28 = t80544810;
            float[] TABLE_R12 = t80544810.Skip(8).ToArray(); //float[30]

            double f2 = BitConverter.Int64BitsToDouble(0x3f00000000000000);
            double f3 = BitConverter.Int64BitsToDouble(0x4330000080000000);

            float[] generatedData = new float[128];// * 4];

            float[] array1 = new float[8];
            float[] array2 = new float[4];
            float[] array3 = new float[4];
            float[] array4 = new float[4];

            int curChunkOffset = 0;
            int arrayPos = 0;
            for (int i = 0; i < 4; i++)
            {
                uint r31 = file[filePos];
                uint r10 = r31 & 0x000000F0;
                r31 = (r31 & 0x0000000F) << 3;
                array2[arrayPos] = TABLE_R28[r31 / 4];
                array3[arrayPos] = TABLE_R28[(r31 / 4) + 1];

                int r9 = (sbyte)file[filePos + 1];
                r9 *= 256;
                r9 += (int)r10;
                r9 = (int)((uint)r9 ^ 0x80000000);
                double f0 = BitConverter.Int64BitsToDouble(0x43300000_00000000 | (uint)r9);
                double f1 = f2 * (f0 - f3);
                generatedData[curChunkOffset] = (float)f1;
                array1[arrayPos] = (float)f1;



                r31 = file[filePos + 2];
                r10 = r31 & 0x000000F0;
                r31 = (r31 & 0x0000000F) << 2;
                array4[arrayPos] = TABLE_R12[r31 / 4];

                r9 = (sbyte)file[filePos + 3];
                r9 *= 256;
                r9 += (int)r10;
                r9 = (int)((uint)r9 ^ 0x80000000);
                f0 = BitConverter.Int64BitsToDouble(0x43300000_00000000 | (uint)r9);
                f0 = f2 * (f0 - f3);
                generatedData[curChunkOffset + 1] = (float)f0;
                array1[arrayPos + 4] = (float)f0;



                curChunkOffset += 32;// * 4;
                filePos += 4;
                arrayPos += 1;
            }

            for(int i = 0; i < 15; i++)
            {
                float[] array5 = new float[4];
                float[] array6 = new float[4];

                double f12 = BitConverter.Int64BitsToDouble(0x4330000080000000);
                for (int b = 0; b < 4; b++)
                {
                    byte by = file[filePos];

                    uint n = (by & (uint)0xF0) << 24;
                    n = n = n ^ 0x80000000;
                    double f = BitConverter.Int64BitsToDouble(0x43300000_00000000 | (uint)n);
                    f = f - f12;
                    array5[b] = (float)f;

                    n = (by & (uint)0x0F) << 28;
                    n = n = n ^ 0x80000000;
                    f = BitConverter.Int64BitsToDouble(0x43300000_00000000 | (uint)n);
                    f = f - f12;
                    array6[b] = (float)f;

                    filePos += 1;
                }

                for(int block = 0; block < 4; block++)
                {
                    int writeLoc = (block * 32) + ((i+1) * 2);
                    float prevFloat1 = generatedData[writeLoc - 2];
                    float prevFloat2 = generatedData[writeLoc - 1];

                    float float1 = (array3[block] * prevFloat1) + (array2[block] * prevFloat2) + (array5[block] * array4[block]);
                    float float2 = (array3[block] * prevFloat2) + (array2[block] * float1)     + (array6[block] * array4[block]);
                    generatedData[writeLoc] = float1;
                    generatedData[writeLoc + 1] = float2;
                }
            }

#if DEBUG
            if(filePos != origFilePos + 76)
            {
                Debugger.Break();
            }
#endif
            return generatedData;
        }
    }
}
