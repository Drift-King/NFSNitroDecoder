(Currently very WIP, just throwing down a draft here)

(Also, I don't know what the proper audio format terminology is, so I'm just using 
what kinda makes sense to me. I'll look into better naming at some point)

A NFS Audio file consists of a number of sequential **chunks**, each with an 
8-byte header followed by some number of 76-byte **blocks**. The header is just
two (big-endian) 32-bit integers. The first integer is the length of the chunk,
and the second integer is the length, in samples, of the audio data that this chunk
contains (aka total # of samples / number of channels). The chunks are almost always around 0x800-0x700 bytes long.

If the chunk is the last chunk of an audio file, the first bit of the header will be 1.
Usually the buffer of samples generated from the chunk will need to be truncated to match
the desired length.

Some example headers:

| Header                    | Meaning                                                         |   
| ------------------------- | --------------------------------------------------------------- |
| `000007C0 00000680` | This chunk is 0x7C0 bytes long and will decode to 0x680 samples |
| `800004D8 00000392` | This chunk is the last chunk of the track. It's 0x4D8 bytes long and will decode to 0x392 samples. |     

Each block in a chunk is 76 bytes long and decodes to 128 single-precision floating point numbers. The 128 samples are divided
into 4 32-sample **sections**. The first 4 words of the 76 bytes each give information about the corresponding section. The remaining
60 bytes represent are used to generate the rest of the sample data. In detail:

Each 4-byte word:
0x QR ST UV WX
 * R is used to select predictor floats (for that section) from a table
 * 0xSTQ0 (sign-extended) is linearly mapped to the range (-1, 1) and becomes the first sample of the section
 * V is used to select a scale float (for that section) from a table
 * 0xWXU0 (sign-extended) is linearly mapped to the range (-1, 1) and becomes the second sample of the section

The float tables:

Predictors

| prev sample | prev prev sample | prev sample (hex) | prev prev sample (hex) |
| ----------- | ---------------- | ----------------- | ----------|
| 0 | 0 | 0x00000000 | 0x00000000 |
| 0.9375 | 0 | 0x3f700000 | 0x3f700000 |
| 1.796875 | -0.8125 | 0x3fe60000 | 0x3fe60000 |
| 1.53125 | -0.859375 | 0x3fc40000 | 0x3fc40000 |

Scales

| scale        | scale (hex)| largest amplitude w/ scale
| ------------ | ---------- | -----------
| 4.656613E-10 | 0x30000000 | 1
| 2.328306E-10 | 0x2f800000 | 0.5
| 1.164153E-10 | 0x2f000000 | 0.25
| 5.820766E-11 | 0x2e800000 | 0.125
| 2.910383E-11 | 0x2e000000 | 0.0625
| 1.455192E-11 | 0x2d800000 | 0.03125
| 7.275958E-12 | 0x2d000000 | 0.015625
| 3.637979E-12 | 0x2c800000 | 0.0078125
| 1.818989E-12 | 0x2c000000 | 0.00390625
| 9.094947E-13 | 0x2b800000 | 0.001953125
| 4.547474E-13 | 0x2b000000 | 0.0009765625
| 2.273737E-13 | 0x2a800000 | 0.0004882813



This means the remaing 60 bytes correspond to 30 samples for each section. Each nibble represents a single sample,
and the nibbles are arranged so they correspond to sections like 00 11 22 33 00 11 22 33

The nibbles determine the float like so:
 * First they're mapped from 1879048192 to -2147483648 (in steps of 268435456); 0x0-0x7 
   maps to the positive numbers, and 0x8-0xF maps to the negative numbers
 * Then that is multiplied by the scale float selected by V above
 * It is then added to the previous 2 samples multiplied by their respective predictor floats (selected by R above)


The AST files and PFData files are slightly different in format 
[stuff stuff stuff]



To dump the audio data from the game I added a breakpoint at 0x80397eac (this instruction is part of the stack
?unrolling? done in the decoding function) and to the method PowerPC::CheckBreakPoints I replaced `CPU::Break()`
with the following code:
```C++
if (PC == 0x80397eac)
{
  u32 lineStart = GPR(4) - 76;
  u32 decodedDataStart = GPR(3) - 128;

  std::vector<char> line_bytes(76);
  Memory::CopyFromEmu(line_bytes.data(), lineStart, 76);

  std::vector<char> decoded_bytes(512);
  Memory::CopyFromEmu(decoded_bytes.data(), decodedDataStart, 512);

  std::ofstream stream;
  stream.open("C:\\Users\\rolan\\Desktop\\Need For Speed Nitro\\testdata.raw", std::ios::out | std::ios::binary | std::ios::app);
  stream.write(line_bytes.data(), 76);
  stream.write(decoded_bytes.data(), 512);
  stream.close();
}
else
{
  CPU::Break();
}
```
