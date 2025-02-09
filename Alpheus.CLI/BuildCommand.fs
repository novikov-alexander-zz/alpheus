﻿module ItisLab.Alpheus.BuildCommand

open Argu
open ItisLab.Alpheus.CLI
open ItisLab.Alpheus.Trace
open System

let run (buildArgs:ParseResults<BuildArgs>) = 
    let deps = buildArgs.GetResults <@ D @>
    let outputs = buildArgs.GetResults <@ O @>
    let doNotCleanOutputs = buildArgs.Contains Disable_Outputs_Clean
    if List.length outputs = 0 then Error "You need to specify at least one output"
        else
        let unrecognized = buildArgs.UnrecognizedCliParams
        if List.length unrecognized = 0 then Error "You need to specify a command"
        else
            let command = String.Join(' ', unrecognized) // buildArgs.TryGetResult <@ Command @>
            // Checking that both dependencies and outputs are under the same experiment folder
            let allPathParams = List.append deps outputs
            let roots = List.map Config.tryLocateExperimentRoot allPathParams |> List.distinct
            traceVerbose(sprintf "Found following experiment roots among the supplied artefact paths: %A" roots)

            match roots with
            | [] -> Error "An output artefact is not specified"
            | [None] -> Error "Not an experiment folder: .alpheus"
            | [Some experimentRoot] -> API.buildAsync experimentRoot deps outputs command doNotCleanOutputs |> Async.RunSynchronously
            | _ -> Error "Not all of the input or output artefacts are under the same experiment root folder"