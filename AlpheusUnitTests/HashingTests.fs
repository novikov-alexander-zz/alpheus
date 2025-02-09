module ItisLab.Alpheus.Tests.Hashing

open System
open Xunit
open System.IO
open System.Threading.Tasks
open ItisLab.Alpheus.Tests.Utils

let assertByteArraysEqual array1 array2 =
    Assert.Equal(Array.length array1,Array.length array2)
    Array.iter2 (fun (x1:byte) (x2:byte) -> Assert.Equal(x1,x2)) array1 array2

let assertByteArrayContentsDiffer (array1:byte[]) array2 =
    Assert.Equal(Array.length array1,Array.length array2)
    Assert.True(Array.exists2 (<>) array1 array2)

let hashByteArrayAsync chunkSize toHash =
    async {
        let localCopy = Array.copy toHash
        use memStream = new MemoryStream(localCopy)
        return! ItisLab.Alpheus.Hash.hashStreamAsync chunkSize memStream
    }

let testRandomMemHashingAsync seed chunkSize testSize =
    async {
        let random1 = new Random(seed)
        let shortBuffer = Array.zeroCreate<Byte> chunkSize
        random1.NextBytes(shortBuffer)

        let! hash1 = hashByteArrayAsync chunkSize shortBuffer
        let! hash2 = hashByteArrayAsync chunkSize shortBuffer

        assertByteArraysEqual hash1 hash2
    }


[<Fact>]
let ``Same data produce same hash [chunked hashing]`` () =
    testRandomMemHashingAsync 1 1024 2048 |> toAsyncFact

[<Fact>]
let ``Same data produce same hash [unchunked hashing]`` () =
    testRandomMemHashingAsync 2 2048 1024 |> toAsyncFact

[<Fact>]
let ``Chunked hashing results match non-chunked`` () =
    async {
        let random1 = new Random(10)
        let shortBuffer = Array.zeroCreate<Byte> 1024
        random1.NextBytes(shortBuffer)

        let! chunkedHash = hashByteArrayAsync 16 shortBuffer
        let! notChunkedHash = hashByteArrayAsync 2048 shortBuffer

        assertByteArraysEqual chunkedHash notChunkedHash
    } |> toAsyncFact

[<Fact>]
let ``Different data produces different hashes`` ()=
    async {
        let random1 = new Random(12)
        let buffer1 = Array.zeroCreate<Byte> 1024
        let buffer2 = Array.zeroCreate<Byte> 1024
        random1.NextBytes(buffer1)
        random1.NextBytes(buffer2)

        let! hash1 = hashByteArrayAsync 2048 buffer1
        let! hash2 = hashByteArrayAsync 2048 buffer2

        assertByteArrayContentsDiffer hash1 hash2
        
    } |> toAsyncFact

[<Fact>]
let ``The same file gives the same hash`` () =
    async {
    let! hash1 = ItisLab.Alpheus.Hash.hashFileAsync @"data/texturalData.txt"
    let! hash2 = ItisLab.Alpheus.Hash.hashFileAsync @"data/texturalData.txt"

    assertByteArraysEqual hash1 hash2

    } |> toAsyncFact

[<Fact>]
let ``Consistency across alpheus versions and platforms`` () =
    async {
    let! hash1 = ItisLab.Alpheus.Hash.hashFileAsync @"data/texturalData.txt"

    let str = ItisLab.Alpheus.Hash.hashToString hash1

    // This test checks the consistency across alpheus version and across different platforms (where tests are executed)
    // the following hardcoded hash value is calculated on windows
    Assert.Equal("9D231A0BF5230973C4D2F519AD337136F5730B2DDB95E423C42459CBED7921E517BEC1F16F1840F257645A6F62315E90A035161815064C5C63674A7D11BD4C43",str)

    } |> toAsyncFact

[<Fact>]
let ``Different files result in different hashes`` () =
    async {
    let! hash1 = ItisLab.Alpheus.Hash.hashFileAsync @"data/texturalData.txt"
    let! hash2 = ItisLab.Alpheus.Hash.hashFileAsync @"data/folder_with_files/TextFile3.txt"

    assertByteArrayContentsDiffer hash1 hash2

    } |> toAsyncFact

[<Fact>]
let ``The same directory gives the same hash`` () =
    async {
    let! hash1 = ItisLab.Alpheus.Hash.hashDirectoryAsync @"data/folder_with_files"
    let! hash2 = ItisLab.Alpheus.Hash.hashDirectoryAsync @"data/folder_with_files"

    assertByteArraysEqual hash1 hash2

    } |> toAsyncFact

[<Fact>]
let ``Different directories result in different hashes`` () =
    async {
    let! hash1 = ItisLab.Alpheus.Hash.hashDirectoryAsync @"data/folder_with_files"
    let! hash2 = ItisLab.Alpheus.Hash.hashDirectoryAsync @"data/folder_with_files/subfolder"

    assertByteArrayContentsDiffer hash1 hash2

    } |> toAsyncFact

type FastHashTests(output)=
    inherit SingleUseOneTimeDirectory(output)

    [<Fact>]
    member s.``Fast hash creates hash file for hashed file`` () =
        async {
            let path = System.IO.Path.Combine(s.Path,"file1.txt")
            let hashFilePath = path+".hash"
            System.IO.File.WriteAllText(path,"Test content")

            Assert.False(System.IO.File.Exists(hashFilePath))
            let! hash1 = ItisLab.Alpheus.Hash.fastHashPathAsync path
            Assert.True(System.IO.File.Exists(hashFilePath))
        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash creates hash file for hashed directory`` () =
        async {
            let dirPath =  System.IO.Path.Combine(s.Path,"dir1")
            let filePath = System.IO.Path.Combine(dirPath,"file1.txt")
            let hashFilePath = dirPath+".hash"

            System.IO.Directory.CreateDirectory(dirPath) |> ignore
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(filePath,"Test content"))

            Assert.False(System.IO.File.Exists(hashFilePath))
            let! hash1 = ItisLab.Alpheus.Hash.fastHashPathAsync dirPath
            Assert.True(System.IO.File.Exists(hashFilePath))
        } |> toAsyncFact

    
    [<Fact>]
    member s. ``Hidden files are excluded from hash calculation`` () =
        async {
            let! hash1 = ItisLab.Alpheus.Hash.hashDirectoryAsync s.Path
    
            let hidFileName = System.IO.Path.Combine(s.Path,".gitfile1")
    
            do! File.WriteAllTextAsync(hidFileName,"test hidden files") |> Async.AwaitTask
            let initialAttrs = File.GetAttributes(hidFileName)
            File.SetAttributes(hidFileName, initialAttrs ||| FileAttributes.Hidden)
    
            let! hash2 = ItisLab.Alpheus.Hash.hashDirectoryAsync s.Path
    
            assertByteArraysEqual hash1 hash2
    
        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash respects up-to-date dot-hash file for file artefact`` () =
        async {
            let path = System.IO.Path.Combine(s.Path,"file1.txt")
            let hashFilePath = path+".hash"
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(path,"Test content"))
            // We need to wait to make .hash file-modification time to be later then original file modification time
            do! Async.Sleep 100

            // Writing 128 zeros to be used as expected hash value
            let textBuilder = new System.Text.StringBuilder()
            Seq.init 128 (=) |> Seq.iter (fun dummy -> textBuilder.Append('0') |> ignore)
            let hashText = textBuilder.ToString()

            System.IO.File.WriteAllText(hashFilePath,hashText)

            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync path            
            match hash1res with
            |   Some hash1 ->
                Assert.Equal(hash1,hashText)
            |   None ->
                Assert.True(false,"Hash must be read from disk")

        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash ignores stale dot-hash file for file artefact`` () =
        async {
            let path = System.IO.Path.Combine(s.Path,"file1.txt")
            let hashFilePath = path+".hash"

            // Writing 128 zeros to be used as precomputed hash value to be ignored
            // as its creation time is before the original file creation time
            let textBuilder = new System.Text.StringBuilder()
            Seq.init 128 (=) |> Seq.iter (fun dummy -> textBuilder.Append('0') |> ignore)
            let hashText = textBuilder.ToString()
            System.IO.File.WriteAllText(hashFilePath,hashText)

            // We need to wait to make .hash file-modification time to be before the original file modification time
            do! Async.Sleep 100
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(path,"Test content"))
            
            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync path            
            match hash1res with
            |   Some hash1 ->
                Assert.NotEqual<string>(hash1,hashText)
            |   None ->
                Assert.True(false,"Hash must be read from disk")

        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash respects up-to-date dot-hash file for directory artefact`` () =
        async {
            let dirPath = System.IO.Path.Combine(s.Path,"test1")
            let subdirPath = System.IO.Path.Combine(dirPath,"subdir1")
            let filePath = System.IO.Path.Combine(subdirPath,"text.txt")
            let hashFilePath = dirPath+".hash"
            Directory.CreateDirectory(dirPath) |> ignore
            Directory.CreateDirectory(subdirPath) |> ignore
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(filePath,"Test content"))
            // We need to wait to make .hash file-modification time to be later then original file modification time
            do! Async.Sleep 100

            // Writing 128 zeros to be used as expected hash value
            let textBuilder = new System.Text.StringBuilder()
            Seq.init 128 (=) |> Seq.iter (fun dummy -> textBuilder.Append('0') |> ignore)
            let hashText = textBuilder.ToString()

            System.IO.File.WriteAllText(hashFilePath,hashText)

            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync dirPath            
            match hash1res with
            |   Some hash1 ->
                Assert.Equal(hash1,hashText)
            |   None ->
                Assert.True(false,"Hash must be read from disk")

        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash ignores stale dot-hash file for directory artefact`` () =
        async {
            let dirPath = System.IO.Path.Combine(s.Path,"test1")
            let subdirPath = System.IO.Path.Combine(dirPath,"subdir1")
            let filePath = System.IO.Path.Combine(subdirPath,"text.txt")
            let hashFilePath = dirPath+".hash"
            
            // Writing 128 zeros to be used as precomputed hash value to be ignored
            // as its creation time is before the original file creation time
            let textBuilder = new System.Text.StringBuilder()
            Seq.init 128 (=) |> Seq.iter (fun dummy -> textBuilder.Append('0') |> ignore)
            let hashText = textBuilder.ToString()
            System.IO.File.WriteAllText(hashFilePath,hashText)

            // We need to wait to make .hash file-modification time to be before the original file modification time
            do! Async.Sleep 100
            Directory.CreateDirectory(dirPath) |> ignore
            Directory.CreateDirectory(subdirPath) |> ignore
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(filePath,"Test content"))
                
            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync dirPath
            match hash1res with
            |   Some hash1 ->
                Assert.NotEqual<string>(hash1,hashText)
            |   None ->
                Assert.True(false,"Hash must be read from disk")

        } |> toAsyncFact


    [<Fact>]
    member s.``Fast hash return None for not-exitent files`` () =
        async {
            let path = System.IO.Path.Combine(s.Path,"file1.txt")
            
            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync path     
            match hash1res with
            |   Some hash1 ->
                Assert.True(false,"Hash is not expected to be read from disk")
            |   None ->
                Assert.True(true)
        } |> toAsyncFact

    [<Fact>]
    member s.``Fast hash return None for not-exitent files if dot-hash exists`` () =
        async {
            let path = System.IO.Path.Combine(s.Path,"file1.txt")
            do! Async.AwaitTask(System.IO.File.WriteAllTextAsync(path+".hash","00000"))
        
            let! hash1res = ItisLab.Alpheus.Hash.fastHashPathAsync path     
            match hash1res with
            |   Some hash1 ->
                Assert.True(false,"Hash is not expected to be read from disk")
            |   None ->
                Assert.True(true)
        } |> toAsyncFact

    