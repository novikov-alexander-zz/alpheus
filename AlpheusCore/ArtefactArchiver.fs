﻿module ItisLab.Alpheus.ArtefactArchiver

open System.IO
open System.IO.Compression
open System.Threading.Tasks
open System.Threading
open System.Diagnostics
open System

type ArchivingMessage =
    | LoadFileRequest of filename:string
    | CompressRequest of filename:string*data:Stream
    | Done of filename:string
    | ExpectedFileCount of int

let archiveSingleFileToStreamAsync (fileAbsPath:string) (streamToWriteTo:Stream) =
    async {
        use archive = new ZipArchive(streamToWriteTo,ZipArchiveMode.Create,true)
        use fileStream = new FileStream(fileAbsPath, FileMode.Open, FileAccess.Read)
        use fileStreamInZip = archive.CreateEntry("__artefact__", CompressionLevel.Fastest).Open()
        do! Async.AwaitTask (fileStream.CopyToAsync fileStreamInZip)
        return ()                            
    }

let archiveDirFilesToStreamAsync (fileCompleteCallback:string->unit) (directoryFullPath:string) (fileFullPaths:string seq) (streamToWriteTo:Stream) =
    async {        
        let fileFullPaths = Array.ofSeq fileFullPaths
        use archive = new ZipArchive(streamToWriteTo,ZipArchiveMode.Create,true)
        
        use archivingDoneEvent = new ManualResetEvent(false)
        let readConcurrency = 16       
        let archivingAgent = MailboxProcessor.Start(fun inbox -> async {
            let filenamesQ = System.Collections.Generic.Queue<string>()
            let filedataQ = System.Collections.Generic.Queue<string*Stream>()
            let reading = ref 0
            let archiving = ref 0
            let processed = ref 0
            let mutable totalFiles = 1            
            while processed.Value<totalFiles do
                // 1) handling message
                let! msg = inbox.Receive()
                match msg with
                |   LoadFileRequest fn -> filenamesQ.Enqueue fn                        
                |   CompressRequest(name,stream) -> filedataQ.Enqueue (name,stream)
                |   Done name ->
                    decr reading // permit one more reading
                    decr archiving // permit archiving
                    incr processed
                    fileCompleteCallback name                   
                |   ExpectedFileCount count ->
                    totalFiles <- count                    
                    
                // 2) handling queues and counters
                while (filenamesQ.Count>0 && reading.Value<readConcurrency) || (filedataQ.Count>0 && archiving.Value<1)  do
                    if (filenamesQ.Count>0 && reading.Value<readConcurrency) then
                        let filePath = filenamesQ.Dequeue()
                        incr reading                    
                        async {
                            //printfn "Reading %s" filePath
                            let fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read)
                            let memStream = new MemoryStream()
                            do! Async.AwaitTask(fileStream.CopyToAsync memStream)
                            fileStream.CopyTo memStream
                            fileStream.Close()
                            fileStream.Dispose()
                            memStream.Seek(0L,SeekOrigin.Begin) |> ignore
                            //printfn "Read %s into memory" filePath
                            inbox.Post(CompressRequest (filePath,memStream))
                        } |> Async.Start
                    if (filedataQ.Count>0 && archiving.Value<1) then
                        let filePath, memStream = filedataQ.Dequeue()
                        incr archiving
                        async {
                            //printfn "Archiving %s" filePath
                            let nameInArchive =                                 
                                // relative path
                                Path.GetRelativePath(directoryFullPath,filePath)                                
                               
                            printfn "archiver: Adding archive item %s" nameInArchive
                            let fileStreamInZip = archive.CreateEntry(nameInArchive, CompressionLevel.Fastest).Open()
                            do! Async.AwaitTask (memStream.CopyToAsync fileStreamInZip)
                            memStream.Dispose()
                            fileStreamInZip.Dispose()
                            printfn "archiver: Archived %s as %s" filePath nameInArchive
                            inbox.Post(Done filePath)
                        } |> Async.Start
            printfn "All %d files are saved" totalFiles |> ignore
            archivingDoneEvent.Set() |> ignore          
            })    

        archivingAgent.Post(ExpectedFileCount(Array.length fileFullPaths))
        fileFullPaths |> Array.iter (fun name -> archivingAgent.Post(LoadFileRequest name))            
        do! Async.AwaitTask (Task.Run(System.Action(fun () -> archivingDoneEvent.WaitOne() |> ignore)))
        
        return ()
    }

let artefactFromArchiveStreamAsync (targetAbsPath:string) (streamToReadFrom:Stream) isSingleFile =
    async {
        do! Async.SwitchToThreadPool()
        
        //let dbgAzureStream = new DebuggingStream("storage stream (for"+targetAbsPath+")",streamToReadFrom)

        //dbgAzureStream.IsTraceEnabled <- false

        // use memStream = new MemoryStream()
        
        //printfn "archiver: copying storage stream"
        //do! Async.AwaitTask(dbgAzureStream.CopyToAsync(memStream))
        //printfn "archiver: storage stream copied"
        
        use archive = new ZipArchive(streamToReadFrom, ZipArchiveMode.Read, true)
        
        printfn "archiver: Archive content to extract to disk"
        archive.Entries |> Seq.iter (fun entry -> printfn "archiver: %s:%s" entry.Name entry.FullName)


        //printfn "archiver: compressed stream opened"
        if isSingleFile then
            let entry = archive.GetEntry("__artefact__")
            entry.ExtractToFile(targetAbsPath,true)
            printfn "%s single file artefact restored" targetAbsPath
        else
            printfn "archiver: decompressing stream into %s" targetAbsPath
            //let tmpDirName = Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())
            
            try
                //let dirInfo = Directory.CreateDirectory tmpDirName
                //printfn "archiver: temp dir (%s) created" tmpDirName

                archive.ExtractToDirectory targetAbsPath
                //printfn "archiver: decompressed %s" targetAbsPath
            with
                | exc ->
                    printfn "%A" exc
                    raise exc
            
    }