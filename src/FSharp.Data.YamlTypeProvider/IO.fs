namespace FSharp.Configuration

open System
open System.IO

module File =
    let tryOpenFile filePath =
        try Some (new FileStream (filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        with _ -> None

    let tryReadNonEmptyTextFile filePath =
        let maxAttempts = 5
        let rec sleepAndRun attempt = async {
            do! Async.Sleep 1000
            return! loop (attempt - 1) }

        and loop attempt = async {
            match tryOpenFile filePath with
            | Some file ->
                try
                    use reader = new StreamReader (file)
                    match attempt, reader.ReadToEnd() with
                    | 0, x -> return x
                    | _, "" -> 
                        printfn "Attempt %d of %d: %s is empty. Sleep for 1 sec, then retry..." attempt maxAttempts filePath
                        return! sleepAndRun (attempt - 1)
                    | _, content -> return content 
                finally file.Dispose() 
            | None -> 
                printfn "Attempt %d of %d: cannot read %s. Sleep for 1 sec, then retry..." attempt maxAttempts filePath
                return! sleepAndRun (attempt - 1) }
        loop maxAttempts |> Async.RunSynchronously

    type private State = 
        { LastFileWriteTime: DateTime
          Updated: DateTime }

    let watch changesOnly filePath onChanged =
        let getLastWrite() = File.GetLastWriteTime filePath
        let state = ref { LastFileWriteTime = getLastWrite(); Updated = DateTime.Now }
        
        let changed (args: FileSystemEventArgs) =
            let curr = getLastWrite()
            // log (sprintf "%A. Last = %A, Curr = %A" args.ChangeType !lastWrite curr)
            if curr <> (!state).LastFileWriteTime && DateTime.Now - (!state).Updated > TimeSpan.FromMilliseconds 500. then
//                try 
                    onChanged()
                    state := { LastFileWriteTime = curr; Updated = DateTime.Now }
//                with e -> ()
                //log "call onChanged"
                

        let w = new FileSystemWatcher(Path.GetDirectoryName filePath, Path.GetFileName filePath)
        w.NotifyFilter <- NotifyFilters.CreationTime ||| NotifyFilters.LastWrite ||| NotifyFilters.Size
        w.Changed.Add changed
        if not changesOnly then 
            w.Deleted.Add changed
            w.Renamed.Add changed
        w.EnableRaisingEvents <- true
        w :> IDisposable

// change
//[07.12.2013 12:46:14, 36] Deleted (Deleted). LWT = 01.01.1601 4:00:00 (504911376000000000) 
//[07.12.2013 12:46:14, 36] Renamed (Renamed). LWT = 07.12.2013 12:46:14 (635220171742164426) 
//[07.12.2013 12:46:14, 46] Changed (Changed). LWT = 07.12.2013 12:46:14 (635220171742164426)

// another change
//[07.12.2013 12:44:03, 13] Deleted (Deleted). LWT = 07.12.2013 12:44:03 (635220170437799821) 
//[07.12.2013 12:44:03, 13] Renamed (Renamed). LWT = 07.12.2013 12:44:03 (635220170437799821) 
//[07.12.2013 12:44:03, 13] Changed (Changed). LWT = 07.12.2013 12:44:03 (635220170437799821) 

// delete
//[07.12.2013 12:44:53, 37] Deleted (Deleted). LWT = 01.01.1601 4:00:00 (504911376000000000) 

// appearing deleted file
//[07.12.2013 12:51:49, 35] Renamed (Renamed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:51:49, 35] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:51:49, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 

// rename our file -> another
//[07.12.2013 12:49:57, 20] Renamed (Renamed). LWT = 01.01.1601 4:00:00 (504911376000000000) 

// rename another file -> our
//[07.12.2013 12:50:55, 20] Renamed (Renamed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:50:55, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:50:55, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 


