# Miku Miku Library (Ver.FGOAC)

Format library and file editor for Hatsune Miku: Project DIVA games.

This version also includes FATE/Grand Order Arcade.

## FGOAC Feature

- FARc archive unpack/repack
- FGO master table edit/export
- Texture export/replace
- Sprite table preview
- AET preview (Not fully correct)

# Building

* [Stable (release) builds](https://github.com/blueskythlikesclouds/MikuMikuLibrary/releases)
* [Unstable (development) builds](https://github.com/blueskythlikesclouds/MikuMikuLibrary/releases/tag/nightly)

## Manually building

1. Clone the repository with the `--recursive` option. `git clone --recursive https://github.com/blueskythlikesclouds/MikuMikuLibrary.git`
2. Install FBX SDK. (See instructions [here.](https://github.com/blueskythlikesclouds/MikuMikuLibrary/tree/master/MikuMikuLibrary.Native/Dependencies/FBX))
3. Install the .NET 10 SDK/Runtime through Visual Studio Installer.
4. Open the solution in a Visual Studio version that supports .NET 10 and C++/CLI.
5. Restore the missing NuGet packages.
6. Build the solution.

For a complete publish build, open a Visual Studio 2026 Developer Command
Prompt and run `publish.bat`. It builds the native decoder and publishes the
CLI tools and GUI for x86/x64. The native project still requires the
DirectXTex submodule and the FBX SDK described above.

The native project is a Visual C++/CLI project, so `dotnet restore` on the full
solution may emit `NU1503` for the `.vcxproj`. That warning is expected; use
`publish.bat` (or Visual Studio/MSBuild) for the native project and use
`dotnet restore` only on the managed projects.

If the native DLL is built elsewhere, set `MIKUMIKULIBRARY_NATIVE_PATH` to the
DLL itself (or its containing directory) before launching MikuMikuModel.


# Projects

## Miku Miku Library

This is the main library of the solution, providing methods and classes to read, edit and write file formats from Hatsune Miku: Project DIVA games.

## Miku Miku Model

A GUI front-end of the library that allows you to work with models, textures, motions and sprites.

## Command line tools

These are command line front-ends for certain functionalities of the library.

### Database Converter

A program that allows you to convert database files to XML or vice versa.

Supported files:

* aet_db.bin/.aei
* bone_data.bin/.bon
* mot_db.bin
* obj_db.bin/.osi
* spr_db.bin/.spi
* stage_data.bin/.stg
* str_array.bin/string_array.bin/.str
* tex_db.bin/.txi

### FARC Pack

A program that allows you to extract or create FARC files. MM+ CPK files are also supported.

The extractor auto-detects FATE/Grand Order Arcade `FARc` archives and uses
`FgoFarcArchive`. DIVA `FArC` archives continue to use `FarcArchive`. FGO
raw, gzip, and chunked Zstandard entries can be extracted directly. The FGO
archive implementation also handles the title's encrypted index and payload
layout when the built-in title key applies.

# Special thanks

* [ActualMandM](https://github.com/ActualMandM)
* [BroGamer4256](https://github.com/BroGamer4256)
* [Brolijah](https://github.com/Brolijah)
* [Charl-Ep](https://github.com/Charl-Ep)
* [chrrox](https://www.deviantart.com/chrrox)
* [featjinsoul](https://github.com/featjinsoul)
* [keikei14](https://github.com/keikei14)
* [korenkonder](https://github.com/korenkonder)
* [lybxlpsv](https://github.com/lybxlpsv)
* [minmode](https://www.deviantart.com/minmode)
* [nastys](https://github.com/nastys)
* [s117](https://github.com/s117)
* [samyuu](https://github.com/samyuu)
* [Stewie100](https://github.com/Stewie100)
* [thtrandomlurker](https://github.com/thtrandomlurker)
* [Waelwindows](https://github.com/Waelwindows)
