# Need For Speed Nitro Audio Decoder
Need for Speed: Nitro has some cool exclusive songs as well as some versions of songs that can't be found anywhere else. Unfortunately, as far as I am aware, no one had figured out the format the songs are encoded in on the disc. So I reverse-engineered the audio decoding code using Dolphin and wrote a decoder in C#.

## Basic usage
NFS: Nitro has two versions of each song - one that plays in the Soundtrack menu of the settings, and one that is used during a race. The one that plays in the Soundtrack menu is just a single audio file per song, but the versions used during a race are split into multiple parts. They each have an intro loop (which loops until the race starts), a main loop (which loops during the race), and an outro (which plays on the results screen). The versions used during a race also seem to be slightly different overall - it might be a mixing difference, or some sort of filter or something. I'm not sure.

The Soundtrack versions are in a file called FE_COMMON_STR.ast in the folder /Sound/Global, and the in-race versions are in the /Sound/PFData folder. Within the /Sound/PFData folder, each song has its own folder, which contains two .mus files which are just two copies of the same song (don't ask me why, I have no idea). There are also some other .ast files on the disc, and this program can extract them as well.

Anyway! Now that you know that, here are the possible arguments you can pass to this program:
 * `--ast <path to .ast file>`: Extracts tracks from an .ast file. Note that .ast files have no info about track names, so the names are just the location of the track within the file.
  * `--pfdata <path to PFData folder>`: Converts songs from the PFData folder. For some reason, the PFData songs also each include a sound effect (the same one for each file). I have no idea when it's used, but it's extracted for completeness' sake.
  * `--all-songs <path to Sound folder>`: Extracts all songs from FE_COMMON_STR.ast and from the PFData folder. Note that the FE_COMMON_STR.ast file has more than just the 26 licensed songs.

This program will convert any files it extracts to WAV.

## Extra stuff
In order to ensure that my program is decoding files correctly, I got some real sample data using Dolphin and tested it against my method. If you'd like to run the test for yourself, just make sure you have the testdata.raw file and then run the program with the `--verify-algorithm` argument. I always test it before committing, but C# doesn't guarantee that floating-point operations are consistent across platforms, so it would be a good idea to do this just in case. At some point I should change the algorithm so it doesn't rely on inconsistent floating-point operations, or just do a rewrite in C++, but for now just verify before you run and let me know if it fails.

If you're interested in the technical details of the format of the files and/or the decoding algorithm, I've described it in detail in the Technical Details.md file.

Finally, thanks a ton to the Dolphin developers, as without Dolphin I couldn't have decoded these files. And thanks in particular to the devs who worked on the debugger, which was an invaluable tool for exploring the game's code.
