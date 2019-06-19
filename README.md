# Alpheus

A tool for organizing and managing computational experiments.

---

| Platform        | Build           |
| ------------- |:-------------:|
| Visual Studio 2019   | [![VS2019 Build Status](https://img.shields.io/appveyor/ci/dgrechka/alpheus.svg)](https://ci.appveyor.com/project/dgrechka/alpheus) |
| Linux     | [![Linux Build Status](https://travis-ci.org/itislab/alpheus.svg?branch=master)](https://travis-ci.org/itislab/alpheus)  |

[![Tests](https://img.shields.io/appveyor/tests/dgrechka/alpheus.svg)](https://ci.appveyor.com/project/dgrechka/alpheus/build/tests)
![NuGet](https://img.shields.io/nuget/v/Alpheus-cli.svg)
[![NuGet](https://img.shields.io/nuget/dt/Alpheus-cli.svg)](https://www.nuget.org/packages/Alpheus-cli/)

## Installation
You need to have [.Net Core SDK 2.1.300 or newer](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial) installed.

If you have it install the latest version of Alpheus with

```
dotnet tool install --global Alpheus-cli
```

## Usage

You can call `alpheus` in the command line

```
C:\project1>alpheus help
USAGE: alpheus [help] [<subcommand> [<options>]]

SUBCOMMANDS:

    init <options>        Make the current directory an Alpheus experiment directory
    config <options>      Modify configuration of research directory
    build <options>       Creates an experiment graph node
    compute <options>     Tries to compute the graph to make the outdated node up to date
    status <options>      Prints the graph status for particular .alph file
    save <options>        Save a copy of file/directory to the storage(s)
    restore <options>     Restore a copy of the file/directory from storage

    Use 'alpheus <subcommand> help' for additional information.

OPTIONS:

    help                  display this list of options.
```
