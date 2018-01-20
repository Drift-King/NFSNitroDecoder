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
            uint pos = 0x2407b80;//0x0000000002407b80;

            FileStream currentFile = File.Create($"outfile {pos:x}.raw");

            bool a = true;

            bool newFileMarkerAppeared = false;
            while (pos > 0 && pos < tfile.Length)
            {
                uint val = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(tfile, (int)pos));
                uint val2 = (uint)System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(tfile, (int)pos + 4));
                //Console.WriteLine($"{pos:x}: {val:x} / {val2:x}    ({val} / {val2})");

                bool lastBlock = false;
                if ((val & 0x80000000) != 0)
                {
                    val = val ^ 0x80000000;

                    if (newFileMarkerAppeared)
                    {
                        lastBlock = true;
                        newFileMarkerAppeared = false;
                    }
                    else
                    {
                        newFileMarkerAppeared = true;
                    }
                }
                else if (newFileMarkerAppeared)
                {
                    Console.WriteLine("Strange bits???");
                    newFileMarkerAppeared = false;
                }

                for(uint i = 8; i < val; i += 76)
                {
                    float[] data = Decode(tfile, pos + i);
                    if (a)
                    {
                        foreach (float f in data)
                        {
                            currentFile.Write(BitConverter.GetBytes(f), 0, sizeof(float));
                        }
                    }
                    a = !a;
                }

                pos += val;

                if (lastBlock)
                {
                    currentFile.Close();
                    Console.WriteLine("End of file");
                    currentFile = File.Create($"outfile {pos:x}.raw");
                }
            }
            Console.ReadKey();
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
