# Where this codec is used
The two places this codec is to store songs are in the FE_COMMON_STR.ast file in \Sound\Global, and in the .mus files in the \Sound\PFData folder. There are two versions of every song in the game - one that plays during races, and one that plays in the Soundtrack menu of the options. The PFData files correspond to the in-race versions, and FE_COMMMON_STR.ast contains all of the Soundtrack menu versions along with some other tracks. 

The codec is also used to encode some other stuff in .ast files in the Sound\IG_Global folder, but I believe that's mostly just sound effects and police audio clips. And it may be used other places in the game, but I haven't looked at all.

# Basic Structure
*(Note: the names I came up with for the sections of these files are just things I thought made 
sense - I reverse-engineered this from assembly so I don't know what the real terms are)*

An NFS Audio file consists of a number of sequential **chunks**, each with an 
8-byte header followed by some number of 76-byte **blocks**. If the first bit 
of the header is 1, then its' corresponding chunk is the final chunk of the song. 
Other than that,the header is just two 32-bit integers. The first integer is the 
size of the chunk, and the second integer is the length, in samples, of the audio 
data contained in the chunk. So, for example, if the chunk contains 1024 samples and
the song has 2 channels, the second integer will be 512.

If the chunk is the last chunk in the song, then the chunk will almost always decode to more samples than the second
integer specifies, so any extra samples can just be discarded.

Here's some example headers in case that helps:

| Header              | Meaning                                                         |   
| ------------------- | --------------------------------------------------------------- |
| `000007C0 00000680` | This chunk is 0x7C0 bytes long and will decode to 0x680 samples |
| `800004D8 00000392` | This chunk is the last chunk of the track. It's 0x4D8 bytes long and will decode to 0x392 samples. | 

(Also note: the generated audio files should be played at 32000Hz)

### Variations
For some reason the AST files and the MUS files are structured slightly differently. AST files' first chunk always starts at 0x40,
and most tracks are separated by a small single-block track that is just all zeroes. The MUS files, however, have their first chunk at 0x680. There is a bunch of data before that, but I have no idea what it means and it doesn't appear to affect the decoding algorithm in any way. 

In addition, AST files have all chunks directly following each other, including chunks from different tracks (i.e. there is no empty space in between tracks). The MUS files also have multiple tracks (for an explanation why, read the main readme), but every track is aligned so that it always starts at an address that is a multiple of 128. This means if a track ends before that, there will be some empty space in between it and the track that comes after it.

# Block format
Each block only contains data for one channel, and the blocks are interleaved within a chunk. So, a chunk with four channels will first have a block for the first channel, then the second channel, then the third, then the fourth, and the cycle starts over again. Left also comes before right.

Each block in a chunk is 76 bytes long and decodes to 128 single-precision floating point numbers. The 128 samples are divided
into 4 32-sample **section**. The first 4 4-byte words of the 76 bytes contain information about their corresponding section, and the remaining 60 bytes represent samples - one nibble per sample. (Yes, that doesn't match up, but that will be explained in a bit.)

To turn a nibble into a sample, the nibble is shifted to be the top nibble of a 32-bit signed integer (i.e. it's mapped to the range 1879048192 to -2147483648 in steps of 268435456), and then multiplied by a section-wide **scale float**. Then, the previous two samples are multiplied by **predictor floats** and added to that. In short:

sample = (shifted nibble * scale) + (prev float * predictor 1) + (prev prev float * predictor 2).

Those first 4 words at the start of the block determine the scale and predictor floats that are used, and also encode the first two samples for each sections. Each word can be thought of as 8 nibbles, and are interpereted like so:

`0x QR ST UV WX`
 * `R` is an index into the predictor float table
 * `0xSTQ0` (sign-extended) is linearly mapped to the range (-1, 1) and becomes the first sample of the section
 * `V` is an index into the scale float table
 * `0xWXU0` (sign-extended) is linearly mapped to the range (-1, 1) and becomes the second sample of the section

Those tables are as follows:

Predictor floats

| Index | Prev sample multiplier | Prev prev sample multiplier | Prev sample multiplier (hex) | Prev prev sample multiplier (hex) |
| ----- | ----------- | ---------------- | ----------------- | ----------|
| 0     | 0 | 0 | 0x00000000 | 0x00000000 |
| 1     | 0.9375 | 0 | 0x3f700000 | 0x00000000 |
| 2     | 1.796875 | -0.8125 | 0x3fe60000 | 0xbf500000 |
| 3     | 1.53125 | -0.859375 | 0x3fc40000 | 0xbf5c0000 |

Scale floats

| Index | Scale        | Scale (hex)| Largest amplitude of nibble if this scale is selected
| ----- | ------------ | ---------- | -----------
| 0     | 4.656613E-10 | 0x30000000 | 1
| 1     | 2.328306E-10 | 0x2f800000 | 0.5
| 2     | 1.164153E-10 | 0x2f000000 | 0.25
| 3     | 5.820766E-11 | 0x2e800000 | 0.125
| 4     | 2.910383E-11 | 0x2e000000 | 0.0625
| 5     | 1.455192E-11 | 0x2d800000 | 0.03125
| 6     | 7.275958E-12 | 0x2d000000 | 0.015625
| 7     | 3.637979E-12 | 0x2c800000 | 0.0078125
| 8     | 1.818989E-12 | 0x2c000000 | 0.00390625
| 9     | 9.094947E-13 | 0x2b800000 | 0.001953125
| 10    | 4.547474E-13 | 0x2b000000 | 0.0009765625
| 11    | 2.273737E-13 | 0x2a800000 | 0.00048828125
| 12    | 1.136868E-13 | 0x2a000000 | 0.000244140625

So that's why there are only 120 sample nibbles - eight samples come from the info words.

The nibbles are also interlaced - each byte corresponds to two samples for a section, and the next byte is for the next section, and so on. 

# How I checked my algorithm
To check that my algorithm was correct, I ran the game in a modified version of Dolphin that would spit out any audio that the
game decoded to a file. The code just added a breakpoint at 0x80397eac, which is the location of an instruction at the end of the decoding method, and then instead of breaking when it hit the breakpoint, it instead ran this code:

```C++
u32 lineStart = GPR(4) - 76; //Location of the start of the block
u32 decodedDataStart = GPR(3) - 128; //Location of the start of the decoded audio from this block

std::vector<char> line_bytes(76);
Memory::CopyFromEmu(line_bytes.data(), lineStart, 76);

std::vector<char> decoded_bytes(512);
Memory::CopyFromEmu(decoded_bytes.data(), decodedDataStart, 512);

std::ofstream stream;
stream.open("C:\\Users\\rolan\\Desktop\\Need For Speed Nitro\\testdata.raw", std::ios::out | std::ios::binary | std::ios::app);
stream.write(line_bytes.data(), 76);
stream.write(decoded_bytes.data(), 512);
stream.close();
```
