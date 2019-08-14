﻿module ItisLab.Alpheus.Hash

open System.IO
open System
open System.Text
open ItisLab.Alpheus
open AlphFiles
open Angara.Data

        
let hashToString (data : byte array) : HashString = System.BitConverter.ToString(data).Replace("-", System.String.Empty)

let createHashAlgorithm() : System.Security.Cryptography.HashAlgorithm =
    upcast System.Security.Cryptography.SHA512.Create()

/// Hashes the content of the stream by reading it by chunks of supplied size till the end of the stream.
/// Stream must have readable Length property
let hashStreamAsync chunkSizeBytes (stream:Stream) =    
    async {
        use hashAlg = createHashAlgorithm()
        
        let bytesCount = stream.Length
        let fullReads = (int)(bytesCount / (int64)chunkSizeBytes)
        let partialBlockSize = (int)(bytesCount % (int64)chunkSizeBytes)
        for i in 0..(fullReads-1) do                
            let! contentBytes = stream.AsyncRead chunkSizeBytes
            let readBytes = hashAlg.TransformBlock(contentBytes,0,chunkSizeBytes,contentBytes,0)
            assert(readBytes = chunkSizeBytes)
        if fullReads = 0 then
            // according to documentation:
            //   "You must call the TransformBlock method before calling the TransformFinalBlock method.
            //    You must call both methods before you retrieve the final hash value. "
            let dummy = Array.zeroCreate<byte> 0
            hashAlg.TransformBlock(dummy,0,0,dummy,0) |> ignore
        let finalBytes = Array.zeroCreate<byte> partialBlockSize
        if partialBlockSize > 0 then
            let! _ = stream.AsyncRead(finalBytes,0,partialBlockSize)                            
            ()
        let finalRead = hashAlg.TransformFinalBlock(finalBytes,0,partialBlockSize).Length
        assert(finalRead = partialBlockSize)
        return hashAlg.Hash
    }

/// Hashes the content of the file supplied
/// Filename as well as attributes are ignored
let hashFileAsync fullPath =
    async {
        let readChunkSize = 100 * 1024 * 1024 //in Bytes
        use f_stream = File.OpenRead fullPath
        let! hash = hashStreamAsync readChunkSize f_stream
        f_stream.Dispose()
        return hash
    }

let rec hashDirectoryAsync (fullPath:string) =
    async {
        let fullPath =
            if (fullPath.EndsWith(Path.DirectorySeparatorChar)) || (fullPath.EndsWith(Path.AltDirectorySeparatorChar)) then
                fullPath
            else
                fullPath + Path.DirectorySeparatorChar.ToString()
        let fileNamesAbs =
            Directory.GetFiles fullPath            
            |> Array.filter (fun name -> not(name.EndsWith(".hash") || name.EndsWith(".alph"))) // ignoring hash file and .alph files
            // Assuming that if the code traverses .alph file, it means that the parent directory is being referenced by alpheus
            // And standalone files (also referenced by alpheus and having .alph files) within this directory should not influence the hash of the parent dir
            |> Array.sort
        let fileNamesRel =
            fileNamesAbs
            |> Array.map (fun path -> path.Remove(0,fullPath.Length)) // removing abs path part
        let fileHashComputation = Seq.map (fun filename -> hashFileAsync filename) fileNamesAbs //full paths here   
        let fileNameBytes = fileNamesRel |> Array.collect (fun x -> Encoding.UTF8.GetBytes(x)) //but relative paths in names

        let dirNamesAbs =
            Directory.GetDirectories fullPath
            |> Array.sort       
        let dirNames = //relative names
            dirNamesAbs 
            |> Array.map (fun x -> x.Remove(0,fullPath.Length))
        let dirHashComputations = Seq.map (fun dirname -> hashDirectoryAsync dirname) dirNamesAbs //full paths are passed here
        let dirNameBytes = dirNames |> Array.collect (fun x -> Encoding.UTF8.GetBytes(x)) // but the relative names are hashed

        let! allHashes = Seq.concat [fileHashComputation; dirHashComputations ] |> Array.ofSeq |> Async.Parallel
        let allNames = Array.append fileNameBytes dirNameBytes
        let flattenHashes = Seq.collect (fun x -> x) allHashes |> Array.ofSeq

        use hashAlg = createHashAlgorithm()
        let namesTransformedBytes = hashAlg.TransformBlock(allNames, 0,allNames.Length,allNames,0)
        assert(namesTransformedBytes = allNames.Length)
        let finalBytes = hashAlg.TransformFinalBlock(flattenHashes,0,flattenHashes.Length)
        assert(finalBytes.Length = flattenHashes.Length)
        return hashAlg.Hash
    }


let hashPathAsync fullPath =
    async {
            Logger.logVerbose Logger.ExperimentFolder (sprintf "Hashing %s ..." fullPath)
            // todo: let items = PathUtils.enumeratePath fullPath
            if File.Exists fullPath then
                let! hash = hashFileAsync fullPath
                return Some(hashToString hash)
            elif Directory.Exists fullPath then
                let! hash = hashDirectoryAsync fullPath
                return Some(hashToString hash)
            else
                return None
    }
    
/// optimization that caches the computed hashes into *.hash files
let hashPathAndSave (fullPath:string) =
    let hashFilePath = PathUtils.pathToHashFile fullPath
    let hashAndSave() =            
        async {
            let! hashStr = hashPathAsync fullPath
            match hashStr with
            |   None -> return None
            |   Some(hashStr) ->
                Logger.logVerbose Logger.ExperimentFolder (sprintf "writing %s " hashFilePath)
                do! File.WriteAllTextAsync(hashFilePath, hashStr) |> Async.AwaitTask
                Logger.logVerbose Logger.ExperimentFolder (sprintf "%s written successfully" hashFilePath)
                // File.SetAttributes(hashFilePath, FileAttributes.Hidden)
                return Some(hashStr)
        }
    let rec getDirLastWriteTimeDeepUTC dirpath = 
        let curDirtime = Directory.GetLastWriteTimeUtc(dirpath)
        let subDirs = Directory.GetDirectories(dirpath) 
        let subFiles = Directory.GetFiles(dirpath) |> Array.filter (fun name -> not(name.EndsWith(".hash")))
        let subDirTimes = Array.map getDirLastWriteTimeDeepUTC subDirs
        let subFilesTimes = Array.map File.GetLastWriteTimeUtc subFiles
        if subDirTimes.Length > 0 || subFilesTimes.Length > 0 then
            let maxSubTimes = Seq.append subFilesTimes subDirTimes |> Seq.max
            max curDirtime maxSubTimes
        else // empty directory
            curDirtime
    async {        
        if File.Exists hashFilePath then
            Logger.logVerbose Logger.ExperimentFolder (sprintf "%s hash file exists" hashFilePath)
            let precomHashTime = File.GetLastWriteTimeUtc hashFilePath
            let dataTime =
                if File.Exists fullPath then
                    Some(File.GetLastWriteTimeUtc fullPath)
                elif Directory.Exists fullPath then
                    Some(getDirLastWriteTimeDeepUTC fullPath)
                else
                    None
            match dataTime with
            |   None -> //data not exists
                Logger.logVerbose Logger.ExperimentFolder (sprintf "deleteing %s hash file as the artefact is absent" hashFilePath)
                File.Delete hashFilePath
                Logger.logVerbose Logger.ExperimentFolder (sprintf "sucessfuly deleted %s hash file" hashFilePath)
                return None
            |   Some(dataWriteTime) ->                               
                    if precomHashTime > dataWriteTime then // considered up to date
                        let! hashStr = File.ReadAllTextAsync(hashFilePath) |> Async.AwaitTask
                        let hashStr2 : HashString = hashStr.Trim()
                        return Some(hashStr2)
                    else
                        return! hashAndSave()                
        else
            return! hashAndSave()
    }

/// The give path can contain patterns and is resolved into the actual list of files so for each of the files
/// the hash is computed and saved into a file.
let hashVectorPathAndSave (fullPath:string) =
    async {
        let resolvedFullPaths = PathUtils.enumeratePath fullPath
        return! resolvedFullPaths |> AsyncUtils.mapAsync (fun (_, path) -> hashPathAndSave path)
    }


/// Computes signature of supplied computeSection (Signature and isTracked member inside this computeSection is ignored during hash calculation)
let getSignature (computeSection:CommandOutput) =
    use sha = System.Security.Cryptography.SHA1.Create()
    let addHash (str:string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes(str)
        sha.TransformBlock(bytes,0,bytes.Length,bytes,0) |> ignore
    addHash computeSection.Command 
    addHash computeSection.WorkingDirectory
    let hashArtefact art =
        addHash art.RelativePath
        art.Hash |> MdMap.toSeq |> Seq.map(snd >> Option.map addHash) |> ignore
    Seq.iter hashArtefact (Seq.append computeSection.Inputs computeSection.Outputs)
    sha.TransformFinalBlock(Array.zeroCreate<byte> 0,0,0) |> ignore
    hashToString sha.Hash

/// Computes the actual signature and compares with the expected.
/// If they are different, the hash invalidates in the computeSection.
let validateSignature (computeSection:CommandOutput) =
    let readSignature = computeSection.Signature
    let actualSignature = getSignature computeSection
    if readSignature = actualSignature then
        computeSection
    else
        let invalidate (version:ArtefactVersion) : ArtefactVersion = version |> MdMap.map(fun _ -> None)
        // wiping out result hashes
        {
            computeSection with
                Outputs = computeSection.Outputs |> List.map (fun output -> {output with Hash = invalidate output.Hash}) 
        }