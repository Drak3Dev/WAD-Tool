## WAD Tool

### Description
Packer/unpacker for Neversoft's "Big Guns" engine games.

* MDK
* Skeleton Warriors
* Apocalypse
* Spider-Man
* Spider-Man 2
* Tony Hawk's Pro Skater
* Tony Hawk's Pro Skater 2
* Tony Hawk's Pro Skater 3
* Tony Hawk's Pro Skater 4

Mat Hoffman's Pro BMX is not supported at this point.

## Working example
* https://www.youtube.com/watch?v=N1-Av0GBIeY

2016-2017, DCxDemo*.

## Command-line version (Linux / macOS / Windows)

`wadtool-cli` is a cross-platform console front-end for the same core code (`hedwadtool/hedwadtool/LegacyThps`),
built as a single native binary with .NET NativeAOT.

### Build
Requires .NET 10 SDK and clang.

```
cd wadtool-cli
dotnet publish -c Release -r linux-x64 -o ../bin    # or win-x64, osx-arm64, ...
```

The result is `bin/wadtool` plus `bin/filenames.txt` (name dictionary).

### Usage
```
wadtool extract <file.hed|file.wad> [-o <outdir>]
wadtool pack <folder|list.txt> [--raw|--hashed] [-o <out.wad>]
wadtool checksum <string> [--caps]
wadtool brute <checksum_hex> [--prefix <p>] [--suffix <s>] [--max-len <n>]
wadtool <file|folder>                 # same as drag-and-drop onto the GUI
```

`extract` detects the HED format (raw THPS1-style or hashed THPS2+-style) and prints the matching `pack` command.
Packing from the `.txt` list produced by `extract` keeps the original file order; extract -> pack round-trips byte-identically.

### Fixes compared to the original code
Pass `--nofix` to any command to run the original behaviour instead (byte-identical to the GUI).

* raw HED: wrong name padding when file size is a multiple of 2048
* unknown files extracted as `_XXXXXXXX_` are packed back with that checksum, not the hash of the placeholder name
* raw (THPS1-style) HED can be extracted, not only hashed
* empty folder / empty list / empty HED no longer crash; blank lines in a list are ignored
* truncated WAD gives a warning instead of silently short files
* brute: tests prefix+suffix itself and ignores letter case in prefix/suffix

Always applied (portability, matches the original behaviour on Windows):
packing from a list works on case-sensitive filesystems, folder contents are packed in alphabetical order,
`.hed`/`.wad` are found regardless of letter case.
