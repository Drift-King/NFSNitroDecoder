(Currently very WIP, just throwing down a draft here)

(Also, I don't know what the proper audio format terminology is, so I'm just using 
what kinda makes sense to me. I'll look into better naming at some point)

A NFS Audio file consists of a number of sequential **blocks**, each with an 
8-byte header followed by some number of 76-byte **lines**. The header is just
two (big-endian) 32-bit integers. The first integer is the length of the block,
and the second integer is the length, in samples, of the audio data that this block
contains (aka total # of samples / number of channels). The blocks are almost always around 0x800-0x700 bytes long.

If the block is the last block of an audio file, the first bit of the header will be 1.
Usually the buffer of samples generated from the block will need to be truncated to match
the desired length.

Some example headers:

| Header                    | Meaning                                                         |   
| ------------------------- | --------------------------------------------------------------- |
| `000007C0 00000680` | This block is 0x7C0 bytes long and will decode to 0x680 samples |
| `800004D8 00000392` | This block is the last block of the track. It's 0x4D8 bytes long and will decode to 0x392 samples. |     

Each line in a block is 76 bytes long and decodes to 128 single-precision floating point numbers. The 128 samples are divided
into 4 32-sample chunks. The first 4 words of the 76 bytes each give information about the corresponding chunk. The remaining
60 bytes represent are used to generate the rest of the sample data. In detail:

Each 4-byte word:
0x QR ST UV WX
 * R is used to select prediction floats (for that chunk) from a table
 * 0xSTQ0 (sign-extended) is linearly mapped to the range (-1, 1) and becomes the first sample of the chunk
 * V is used to select a multiplier float (for that chunk) from a table
 * 0xWXU0 (sign-extended) is linearly mapped to the range (-1, 1) and becomes the second sample of the chunk

This means the remaing 60 bytes correspond to 30 samples for each block. Each nibble represents a single sample,
and the nibbles are arranged so they correspond to blocks like 00 11 22 33 00 11 22 33

The nibbles determine the float like so:
 * First they're mapped from 1879048192 to -2147483648 (in steps of 268435456); 0x0-0x7 
   maps to the positive numbers, and 0x8-0xF maps to the negative numbers
 * Then that is multiplied by the multiplier float selected by V above
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
